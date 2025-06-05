namespace ImageReader.Models;

public class GenerativeAiOptions
{
    public string ApiKey { get; set; } = null!;
    public string DefaultModel { get; set; } = "gemini-pro";
    public string SystemPrompt { get; set; } = "";
    
}
