
using ImageReader.Models;

namespace ImageReader.Services
{
    public class IdCardUploadService : IIdCardUploadService
    {
        private readonly IEnumerable<IChatService> _chatServices;
        
        private const int    MAX_REQUESTS_PER_MIN  = 8;
        private const long   MAX_TOKENS_PER_HOUR   = 1_000_000;
        private readonly TimeSpan MINUTE_WINDOW     = TimeSpan.FromMinutes(1);
        private readonly TimeSpan HOUR_WINDOW       = TimeSpan.FromHours(1);
        private readonly TimeSpan BETWEEN_CALL_WAIT = TimeSpan.FromSeconds(5);
        private readonly TimeSpan ABUSE_PAUSE       = TimeSpan.FromHours(2);
        private readonly TimeSpan CUSHION           = TimeSpan.FromSeconds(2);

        private int      _requestsThisMinute = 0;
        private DateTime _minuteWindowStart  = DateTime.UtcNow;
        private long     _tokensThisHour     = 0;
        private DateTime _hourWindowStart    = DateTime.UtcNow;

        public IdCardUploadService(IEnumerable<IChatService> chatServices)
        {
            _chatServices = chatServices;
        }

        public async Task<IEnumerable<IdCardReturnResult>> ProcessUploadsAsync(
            IEnumerable<string> uploadPaths,
            CancellationToken cancellationToken = default)
        {
            // Zip each folder to its own chat‐service, then run in parallel
            var tasks = uploadPaths
                .Zip(_chatServices, (folder, chat) => ProcessSingleFolderAsync(folder, chat, cancellationToken));

            return await Task.WhenAll(tasks);
        }

        private async Task<IdCardReturnResult> ProcessSingleFolderAsync(
            string uploadsPath,
            IChatService chatService,
            CancellationToken cancellationToken)
        {
            var results   = new List<ImageTextResult>();
            var filePaths = Directory
                .EnumerateFiles(uploadsPath, "*.*", SearchOption.TopDirectoryOnly)
                .Where(p => p.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                         || p.EndsWith(".jpeg",StringComparison.OrdinalIgnoreCase)
                         || p.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                .ToList();

            int total = filePaths.Count;
            int processed = 0;
            string outputsFolder = Path.Combine(Directory.GetCurrentDirectory(), "Outputs", Path.GetFileName(uploadsPath));
            Directory.CreateDirectory(outputsFolder);

            Console.WriteLine($"[{uploadsPath}] Found {total} images.");

            for (int i = 0; i < total; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await EnforceRateLimitsAsync(cancellationToken);

                var path     = filePaths[i];
                var fileName = Path.GetFileName(path);
                Console.WriteLine($"[{uploadsPath}] Processing {fileName}…");

                ImageTextResult result;
                while (true)
                {
                    try
                    {
                        // 1) Read image
                        var imageBytes = await File.ReadAllBytesAsync(path, cancellationToken);
                        var mimeType   = path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                            ? "image/png"
                            : "image/jpeg";

                        // 2) Call Gemini
                        var response = await chatService.SendImageMessageAsync(
                            prompt: @"
You are an expert OCR and data-extraction assistant. Extract these fields to JSON only:
  ""FullName"", ""IdNumber"", ""DateOfBirth"" (YYYY-MM-DD), ""CountryOfIssue"".".Trim(),
                            mimeType:   mimeType,
                            imageBytes: imageBytes
                        );

                        _requestsThisMinute++;
                        _tokensThisHour += response.Usage.TotalTokens ?? 0;

                        var cleaned = CleanCodeFences(response.Reply);
                        result = new ImageTextResult
                        {
                            FileName      = fileName,
                            ExtractedText = cleaned,
                            Usage         = response.Usage
                        };
                        break;
                    }
                    catch (HttpRequestException httpEx) when (httpEx.Message.Contains("429"))
                    {
                        // abuse-pause on 429
                        Console.WriteLine($"[{uploadsPath}] Received 429, pausing {ABUSE_PAUSE.TotalHours}h…");
                        await Task.Delay(ABUSE_PAUSE, cancellationToken);
                        Console.WriteLine($"[{uploadsPath}] Resuming after 429 pause.");
                    }
                    catch (Exception ex)
                    {
                        // non-429 errors → record and move on
                        Console.WriteLine($"[ERROR] {uploadsPath}/{fileName}: {ex.Message}");
                        result = new ImageTextResult
                        {
                            FileName      = fileName,
                            ExtractedText = $"{{ \"message\": \"<error: {ex.Message}>\" }}",
                            Usage         = new UsageDto()
                        };
                        break;
                    }
                }

                // write JSON output
                var outPath = Path.Combine(outputsFolder, Path.GetFileNameWithoutExtension(fileName) + ".json");
                await File.WriteAllTextAsync(outPath, result.ExtractedText, cancellationToken);
                results.Add(result);

                // progress bookkeeping
                processed++;
                DrawProgressBar(uploadsPath, processed, total);

                // abuse-pause every 250 images
                if (processed % 250 == 0 && processed < total)
                {
                    Console.WriteLine($"\n[{uploadsPath}] Reached {processed} images, pausing {ABUSE_PAUSE.TotalHours}h…");
                    await Task.Delay(ABUSE_PAUSE, cancellationToken);
                    Console.WriteLine($"[{uploadsPath}] Resuming after bulk pause.");
                }

                // delay between normal calls
                await Task.Delay(BETWEEN_CALL_WAIT, cancellationToken);
            }

            Console.WriteLine($"\n[{uploadsPath}] Done. Processed {processed}/{total} images.");
            return new IdCardReturnResult { Length = results.Count, Result = results };
        }

        private async Task EnforceRateLimitsAsync(CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;

            if (now - _minuteWindowStart >= MINUTE_WINDOW)
            {
                _minuteWindowStart   = now;
                _requestsThisMinute = 0;
            }
            if (now - _hourWindowStart >= HOUR_WINDOW)
            {
                _hourWindowStart  = now;
                _tokensThisHour   = 0;
            }
            if (_requestsThisMinute >= MAX_REQUESTS_PER_MIN)
            {
                var wait = (_minuteWindowStart + MINUTE_WINDOW) - now + CUSHION;
                Console.WriteLine($"[RateLimit] Waiting {wait.TotalSeconds:N0}s…");
                await Task.Delay(wait, cancellationToken);
                _minuteWindowStart   = DateTime.UtcNow;
                _requestsThisMinute = 0;
            }
            if (_tokensThisHour >= MAX_TOKENS_PER_HOUR)
            {
                var wait = (_hourWindowStart + HOUR_WINDOW) - now + CUSHION;
                Console.WriteLine($"[TokenLimit] Waiting {wait.TotalMinutes:N0}m…");
                await Task.Delay(wait, cancellationToken);
                _hourWindowStart  = DateTime.UtcNow;
                _tokensThisHour   = 0;
            }
        }

        private static string CleanCodeFences(string input)
        {
            var s = input.Trim();
            if (s.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
            {
                var idx = s.IndexOf('\n', StringComparison.Ordinal);
                if (idx >= 0) s = s[(idx + 1)..];
            }
            if (s.StartsWith("```")) s = s[3..];
            if (s.EndsWith("```"))  s = s[..^3];
            return s.Trim();
        }

        private void DrawProgressBar(string tag, int value, int max)
        {
            const int BAR_WIDTH = 50;
            double pct = (double)value / max;
            int filled = (int)(pct * BAR_WIDTH);
            string bar = new string('#', filled) + new string('-', BAR_WIDTH - filled);
            Console.Write($"\r[{tag}] [{bar}] {pct:P1}");
        }
    }
}
