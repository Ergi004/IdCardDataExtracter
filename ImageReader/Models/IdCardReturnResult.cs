namespace ImageReader.Models
{
    public class IdCardReturnResult
    {
        public int Length { get; set; }

        // Initialize to avoid null warnings
        public List<ImageTextResult> Result { get; set; } = new List<ImageTextResult>();
    }
}