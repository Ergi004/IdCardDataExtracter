// Models/GenerativeAiOptions.cs
namespace ImageReader.Models
{
    public class GenerativeAiOptions
    {
        public List<string> ApiKeys { get; set; } = new();
        public List<string> UploadFolders { get; set; } = new();
        public string SystemPrompt { get; set; } = "";
    }
}