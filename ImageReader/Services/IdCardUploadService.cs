
using ImageReader.Models;
using Microsoft.Extensions.Options;

namespace ImageReader.Services
{
    public class IdCardUploadService : IIdCardUploadService
    {
        private readonly Func<string, IChatService> _chatServiceFactory;
        private readonly GenerativeAiOptions _options;

        private const int MAX_REQUESTS_PER_MIN = 10;
        private const long MAX_TOKENS_PER_HOUR = 900_000;
        private readonly TimeSpan MINUTE_WINDOW = TimeSpan.FromMinutes(1);
        private readonly TimeSpan HOUR_WINDOW = TimeSpan.FromHours(1);
        private readonly TimeSpan BETWEEN_CALL_WAIT = TimeSpan.FromSeconds(6);
        private readonly TimeSpan CUSHION = TimeSpan.FromSeconds(3);

        public IdCardUploadService(
            Func<string, IChatService> chatServiceFactory,
            IOptions<GenerativeAiOptions> options)
        {
            _chatServiceFactory = chatServiceFactory;
            _options = options.Value;
        }

        public async Task<IEnumerable<IdCardReturnResult>> ProcessUploadsAsync(
            CancellationToken cancellationToken = default)
        {
            var folderApiPairs = _options.UploadFolders
                .Zip(_options.ApiKeys, (folder, apiKey) => new { Folder = folder, ApiKey = apiKey })
                .ToList();

            var tasks = folderApiPairs.Select(pair => 
                ProcessSingleFolderAsync(
                    uploadsPath: pair.Folder,
                    apiKey: pair.ApiKey,
                    cancellationToken: cancellationToken
                )).ToList();

            Console.WriteLine($"Starting parallel processing of {tasks.Count} folders...");

            var results = await Task.WhenAll(tasks);
            
            Console.WriteLine("All folders processed successfully!");
            return results;
        }

        private async Task<IdCardReturnResult> ProcessSingleFolderAsync(
            string uploadsPath,
            string apiKey,
            CancellationToken cancellationToken)
        {
            var results = new List<ImageTextResult>();
            var chatService = _chatServiceFactory(apiKey);

            var rateLimiter = new RateLimiter
            {
                RequestsThisMinute = 0,
                MinuteWindowStart = DateTime.UtcNow,
                TokensThisHour = 0,
                HourWindowStart = DateTime.UtcNow
            };

            Console.WriteLine($"[{uploadsPath}] Starting processing with API key: {apiKey[..10]}...");

            var filePaths = Directory
                .EnumerateFiles(uploadsPath, "*.*", SearchOption.TopDirectoryOnly)
                .Where(p => p.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                         || p.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                         || p.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                .ToList();

            Console.WriteLine($"[{uploadsPath}] Found {filePaths.Count} image files to process");

            string outputsFolder = Path.Combine(
                Directory.GetCurrentDirectory(),
                "Outputs",
                Path.GetFileName(uploadsPath)
            );
            if (!Directory.Exists(outputsFolder))
            {
                Directory.CreateDirectory(outputsFolder);
                Console.WriteLine($"[{uploadsPath}] Created output directory: {outputsFolder}");
            }

            foreach (var path in filePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                rateLimiter = await EnforceRateLimitsAsync(rateLimiter, uploadsPath, cancellationToken);

                var fileName = Path.GetFileName(path);
                Console.WriteLine($"[{uploadsPath}] Processing {fileName}...");

                try
                {
                    var imageBytes = await File.ReadAllBytesAsync(path, cancellationToken);
                    var mimeType = path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                        ? "image/png"
                        : "image/jpeg";

                    var prompt = @"
You are an expert OCR and data-extraction assistant. The next payload is a raw ID card image (binary).
Extract exactly these four fields and output only a JSON object (no extra text, no markdown fences):
""FullName"", ""IdNumber"", ""DateOfBirth"" (YYYY-MM-DD), ""CountryOfIssue"".";

                    var response = await chatService.SendImageMessageAsync(
                        prompt: prompt.Trim(),
                        mimeType: mimeType,
                        imageBytes: imageBytes
                    );

                    rateLimiter.RequestsThisMinute++;
                    rateLimiter.TokensThisHour += response.Usage.TotalTokens ?? 0;

                    var cleaned = CleanCodeFences(response.Reply);

                    results.Add(new ImageTextResult
                    {
                        FileName = fileName,
                        ExtractedText = cleaned,
                        Usage = response.Usage
                    });

                    var outPath = Path.Combine(
                        outputsFolder,
                        Path.GetFileNameWithoutExtension(fileName) + ".json"
                    );
                    await File.WriteAllTextAsync(outPath, cleaned, cancellationToken);
                    Console.WriteLine($"[{uploadsPath}] → Wrote {outPath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] {uploadsPath}/{fileName}: {ex.Message}");
                    var err = $"{{ \"message\": \"<error: {ex.Message}>\" }}";
                    results.Add(new ImageTextResult
                    {
                        FileName = fileName,
                        ExtractedText = err,
                        Usage = new UsageDto()
                    });
                    
                    var outPath = Path.Combine(
                        outputsFolder,
                        Path.GetFileNameWithoutExtension(fileName) + ".json"
                    );
                    await File.WriteAllTextAsync(outPath, err, cancellationToken);
                }

                Console.WriteLine($"[{uploadsPath}] Waiting {BETWEEN_CALL_WAIT.TotalSeconds}s before next request...");
                await Task.Delay(BETWEEN_CALL_WAIT, cancellationToken);
            }

            Console.WriteLine($"[{uploadsPath}] Completed processing {results.Count} files");

            return new IdCardReturnResult
            {
                Length = results.Count,
                Result = results
            };
        }

        private class RateLimiter
        {
            public int RequestsThisMinute { get; set; }
            public DateTime MinuteWindowStart { get; set; }
            public long TokensThisHour { get; set; }
            public DateTime HourWindowStart { get; set; }
        }

        private async Task<RateLimiter> EnforceRateLimitsAsync(
            RateLimiter limiter,
            string folderName,
            CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;

            if (now - limiter.MinuteWindowStart >= MINUTE_WINDOW)
            {
                limiter.MinuteWindowStart = now;
                limiter.RequestsThisMinute = 0;
            }
            if (now - limiter.HourWindowStart >= HOUR_WINDOW)
            {
                limiter.HourWindowStart = now;
                limiter.TokensThisHour = 0;
            }

            if (limiter.RequestsThisMinute >= MAX_REQUESTS_PER_MIN)
            {
                var wait = (limiter.MinuteWindowStart + MINUTE_WINDOW) - now + CUSHION;
                Console.WriteLine($"[{folderName}] Rate limit reached. Waiting {wait.TotalSeconds:N0}s...");
                await Task.Delay(wait, cancellationToken);
                limiter.MinuteWindowStart = DateTime.UtcNow;
                limiter.RequestsThisMinute = 0;
            }
            if (limiter.TokensThisHour >= MAX_TOKENS_PER_HOUR)
            {
                var wait = (limiter.HourWindowStart + HOUR_WINDOW) - now + CUSHION;
                Console.WriteLine($"[{folderName}] Token limit reached. Waiting {wait.TotalMinutes:N0}m...");
                await Task.Delay(wait, cancellationToken);
                limiter.HourWindowStart = DateTime.UtcNow;
                limiter.TokensThisHour = 0;
            }

            return limiter;
        }

        private static string CleanCodeFences(string input)
        {
            var s = input.Trim();
            if (s.StartsWith("", StringComparison.OrdinalIgnoreCase))
            {
                var idx = s.IndexOf('\n', StringComparison.Ordinal);
                if (idx >= 0) s = s[(idx + 1)..];
            }
            if (s.StartsWith("")) s = s[3..];
            if (s.EndsWith("```")) s = s[..^3];
            return s.Trim();
        }
    }
}