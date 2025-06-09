using System.Text;
using System.Text.Json;
using ImageReader.Models;

namespace ImageReader.Services
{
    public class ChatService : IChatService
    {
        private readonly IHttpClientFactory _httpFactory;
        private readonly string _geminiUrl;
        private readonly string _systemPrompt;

        public ChatService(
            IHttpClientFactory httpFactory,
            string apiKey,
            string systemPrompt,
            string model = "gemini-2.0-flash")
        {
            _httpFactory = httpFactory;
            _systemPrompt = systemPrompt.Trim();
            _geminiUrl = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";
        }

        public async Task<ChatResponseDto> SendImageMessageAsync(
            string prompt,
            string mimeType,
            byte[] imageBytes)
        {
            var data = Convert.ToBase64String(imageBytes);

            var combined = string.IsNullOrWhiteSpace(_systemPrompt)
                ? prompt
                : $"{_systemPrompt}\n{prompt}";

            var payload = new
            {
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new object[]
                        {
                            new { text = combined },
                            new
                            {
                                inlineData = new
                                {
                                    mimeType = mimeType,
                                    data = data
                                }
                            }
                        }
                    }
                }
            };

            var json = JsonSerializer.Serialize(payload);
            var client = _httpFactory.CreateClient();
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var res = await client.PostAsync(_geminiUrl, content);
            res.EnsureSuccessStatusCode();

            var body = await res.Content.ReadAsStringAsync();

            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                var parts = root
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")
                    .EnumerateArray();

                var sb = new StringBuilder();
                foreach (var p in parts)
                    if (p.TryGetProperty("text", out var t))
                        sb.AppendLine(t.GetString());

                var usage = root.GetProperty("usageMetadata");
                long total = usage.GetProperty("totalTokenCount").GetInt64();
                long promptT = usage.GetProperty("promptTokenCount").GetInt64();

                return new ChatResponseDto
                {
                    Reply = sb.ToString().Trim(),
                    Usage = new UsageDto
                    {
                        PromptTokens = promptT,
                        TotalTokens = total
                    }
                };
            }
            catch (Exception ex)
            {
                return new ChatResponseDto
                {
                    Reply = $"<parse error: {ex.Message}>\n{body}",
                    Usage = new UsageDto()
                };
            }
        }
    }
}