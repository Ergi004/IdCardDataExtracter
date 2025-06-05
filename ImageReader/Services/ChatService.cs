using System.Text;
using System.Text.Json;
using ImageReader.Models;

namespace ImageReader.Services
{
    public class ChatService : IChatService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _geminiUrl;


        private readonly string _systemPrompt;

        public ChatService(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration)
        {
            _httpClientFactory = httpClientFactory;

            _systemPrompt = configuration["SystemPrompt"];
            _geminiUrl = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent?key=AIzaSyDDkX5zu-iFnIHOBIPw3COgXaoRTBJA0zQ";
        }

        public async Task<ChatResponseDto> SendImageMessageAsync(
            string prompt,
            string mimeType,
            byte[] imageBytes)
        {
            string base64Data = Convert.ToBase64String(imageBytes);

            string combinedPrompt = string.IsNullOrWhiteSpace(_systemPrompt)
                ? prompt
                : $"{_systemPrompt.Trim()}\n{prompt.Trim()}";
             var payloadObject = new
            {
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new object[]
                        {
                            new { text = combinedPrompt },
                            new
                            {
                                inlineData = new
                                {
                                    mimeType = mimeType,
                                    data = base64Data
                                }
                            }
                        }
                    }
                }
            };

            var jsonString = JsonSerializer.Serialize(payloadObject, new JsonSerializerOptions
            {
                WriteIndented = false
            });

             Console.WriteLine("[DEBUG] Gemini Payload:");
            Console.WriteLine(combinedPrompt);
            var client = _httpClientFactory.CreateClient();
            using var content = new StringContent(jsonString, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(_geminiUrl, content);

            response.EnsureSuccessStatusCode();

            string responseBody = await response.Content.ReadAsStringAsync();

            try
            {
                using var doc = JsonDocument.Parse(responseBody);
                var root = doc.RootElement;

                var candidates = root.GetProperty("candidates");
                if (candidates.GetArrayLength() == 0)
                    throw new Exception("No candidates in Gemini response.");

                var firstCandidate = candidates[0];
                var contentObj = firstCandidate.GetProperty("content");
                var parts = contentObj.GetProperty("parts");

                var sb = new StringBuilder();
                foreach (var part in parts.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var textElement))
                    {
                        sb.AppendLine(textElement.GetString());
                    }
                }
                var usageMetadata = root.GetProperty("usageMetadata"); 

                var promptTokenCount = usageMetadata.GetProperty("promptTokenCount").GetInt32();
                var totalTokenCount = usageMetadata.GetProperty("totalTokenCount").GetInt32();

                return new ChatResponseDto
                {
                    Reply = sb.ToString().Trim(),
                        Usage = new UsageDto
                    {
                        PromptTokens = promptTokenCount,
                        TotalTokens = totalTokenCount
                    }
                };
            }
            catch (Exception parseEx)
            {
                return new ChatResponseDto
                {
                    Reply = $"<could not parse response: {parseEx.Message}>\nRaw JSON:\n{responseBody}"
                     
                };
            }
        }
    }
}
