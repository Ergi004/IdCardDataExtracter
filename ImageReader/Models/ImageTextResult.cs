namespace ImageReader.Models
{
    public class ImageTextResult
    {
        public string FileName { get; set; } = string.Empty;
        public string ExtractedText { get; set; } = string.Empty;

        public UsageDto Usage { get; set; } = new();
    }
}
