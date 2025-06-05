using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ImageReader.Models;

namespace ImageReader.Services
{
    public class IdCardUploadService : IIdCardUploadService
    {
        private readonly IChatService _chatService;

        // Track requests per minute
        private int _requestsThisMinute = 0;
        private DateTime _minuteWindowStart = DateTime.UtcNow;

        // Track tokens per hour
        private long _tokensThisHour = 0;
        private DateTime _hourWindowStart = DateTime.UtcNow;

        public IdCardUploadService(IChatService chatService)
        {
            _chatService = chatService;
        }

        public async Task<IList<ImageTextResult>> ProcessUploadsAsync(
            string uploadsPath,
            CancellationToken cancellationToken = default)
        {
            var results = new List<ImageTextResult>();

            var filePaths = Directory
                .EnumerateFiles(uploadsPath, "*.*", SearchOption.TopDirectoryOnly)
                .Where(path =>
                    path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                )
                .ToList();

            string baseDir = Directory.GetCurrentDirectory();
            string outputsFolder = Path.Combine(baseDir, "Outputs");
            if (!Directory.Exists(outputsFolder))
                Directory.CreateDirectory(outputsFolder);

            foreach (var path in filePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var now = DateTime.UtcNow;
                if ((now - _minuteWindowStart).TotalMinutes >= 1)
                {
                    _minuteWindowStart = now;
                    _requestsThisMinute = 0;
                }
                else if (_requestsThisMinute >= 8)
                {
                    var waitTime = TimeSpan.FromMinutes(1.2) - (now - _minuteWindowStart);
                    Console.WriteLine($"[RateLimit] 15 requests reached. Waiting {waitTime.TotalSeconds:N0}s …");
                    await Task.Delay(waitTime, cancellationToken);
                    _minuteWindowStart = DateTime.UtcNow;
                    _requestsThisMinute = 0;
                }

                now = DateTime.UtcNow;
                if ((now - _hourWindowStart).TotalHours >= 1)
                {
                    _hourWindowStart = now;
                    _tokensThisHour = 0;
                }

                string fileName = Path.GetFileName(path);
                Console.WriteLine($"Processing {fileName} ...");

                try
                {
                    byte[] imageBytes = await File.ReadAllBytesAsync(path, cancellationToken);

                    string mimeType = path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                        ? "image/png"
                        : "image/jpeg";

                    string prompt = @"
You are an expert OCR and data-extraction assistant. The next payload is a raw ID card image (binary).
Extract exactly these four fields and output only a JSON object (no extra text, no markdown fences):
  ""FullName"" (full name as printed),
  ""IdNumber"" (ID or passport number),
  ""DateOfBirth"" (YYYY-MM-DD),
  ""CountryOfIssue"" (three-letter country code or full country name).
If you cannot read it, return in JSON format exactly: {""message"":""cannot read image""}.";

                    ChatResponseDto chatResponse = await _chatService.SendImageMessageAsync(
                        prompt: prompt.Trim(),
                        mimeType: mimeType,
                        imageBytes: imageBytes
                    );

                    _requestsThisMinute++;

                    var usedTokens = chatResponse.Usage.TotalTokens ?? 0;
                    _tokensThisHour += usedTokens;

                    if (_tokensThisHour >= 1_000_000)
                    {
                        Console.WriteLine($"[TokenLimit] Reached {_tokensThisHour} tokens. Waiting 65 minutes …");
                        await Task.Delay(TimeSpan.FromMinutes(65), cancellationToken);
                        _hourWindowStart = DateTime.UtcNow;
                        _tokensThisHour = 0;
                    }

                    string rawReply = chatResponse.Reply.Trim();
                    string cleanedJson = CleanCodeFences(rawReply);
                    var usageMeta = chatResponse.Usage;

                    var imageResult = new ImageTextResult
                    {
                        FileName      = fileName,
                        ExtractedText = cleanedJson,
                        Usage         = usageMeta
                    };
                    results.Add(imageResult);

                    string outName = Path.GetFileNameWithoutExtension(fileName) + ".json";
                    string outPath = Path.Combine(outputsFolder, outName);
                    await File.WriteAllTextAsync(outPath, cleanedJson, cancellationToken);

                    Console.WriteLine($" → Wrote Outputs/{outName}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($" ERROR on {fileName}: {ex.Message}");

                    var errorJson = $"{{ \"message\": \"<error: {ex.Message}>\" }}";
                    results.Add(new ImageTextResult
                    {
                        FileName      = fileName,
                        ExtractedText = errorJson
                    });

                    string outName = Path.GetFileNameWithoutExtension(fileName) + ".json";
                    string outPath = Path.Combine(outputsFolder, outName);
                    await File.WriteAllTextAsync(outPath, errorJson, cancellationToken);
                }
            }

            return results;
        }

        private static string CleanCodeFences(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return input;

            string s = input.Trim();

            if (s.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
            {
                int firstNewLine = s.IndexOf('\n', StringComparison.Ordinal);
                if (firstNewLine >= 0)
                    s = s[(firstNewLine + 1)..];
            }

            if (s.StartsWith("```"))
                s = s[3..];

            if (s.EndsWith("```"))
                s = s[..^3];

            return s.Trim();
        }
    }
}
