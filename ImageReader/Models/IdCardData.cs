using System.Text.Json.Serialization;

namespace ImageReader.Models
{
    public class IdCardData
    {
        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("FullName")]
        public string? FullName { get; set; }

        [JsonPropertyName("IdNumber")]
        public string? IdNumber { get; set; }

        [JsonPropertyName("DateOfBirth")]
        public string? DateOfBirth { get; set; }

        [JsonPropertyName("CountryOfIssue")]
        public string? CountyOfIssue { get; set; }

        [JsonPropertyName("DateOfIssue")]
        public string? DateOfIssue { get; set; }

        [JsonPropertyName("PlaceOfBirth")]
        public string? PlaceOfBirth { get; set; }
        
        [JsonPropertyName("Authority")]
        public string? Authority { get; set; }

        [JsonPropertyName("Gender")]
        public string? Gender { get; set; }

        [JsonPropertyName("DateOfExpiry")]
        public string? DateOfExpiry { get; set; }

        [JsonIgnore]
        public bool IsUnreadable => Message == "Cannot read image";
    }
}