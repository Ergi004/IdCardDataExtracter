using System.Text;
using System.Text.Json;
using ImageReader.Models;

namespace ImageReader.Services
{
    public class JsonToCsvService : IJsonToCsvService
    {
        private const string OUTPUTS_BASE_DIR = "Outputs";
        private const string MISSING_FIELD_MARKER = "----";
        
        private static readonly List<string> CsvHeaders = new()
        {
            "id", "FullName", "IdNumber", "DateOfBirth", "DateOfIssue", 
            "PlaceOfBirth", "CountyOfIssue", "Authority", "Gender", "DateOfExpiry"
        };
        
        public async Task<string> CreateCsvFromJsonsAsync(
            string outputCsvFileName = "summary.csv", 
            CancellationToken cancellationToken = default)
        {
            var sourceDirectory = Path.Combine(Directory.GetCurrentDirectory(), OUTPUTS_BASE_DIR);
            
            if (!Directory.Exists(sourceDirectory))
            {
                throw new DirectoryNotFoundException($"The source directory was not found: {sourceDirectory}");
            }
            
            var csvOutputPath = Path.Combine(sourceDirectory, outputCsvFileName);
            var csvBuilder = new StringBuilder();

            csvBuilder.AppendLine(string.Join(",", CsvHeaders));
            
            var jsonFiles = Directory.EnumerateFiles(sourceDirectory, "*.json", SearchOption.AllDirectories)
                                     .Where(f => !Path.GetFileName(f).Equals(outputCsvFileName, StringComparison.OrdinalIgnoreCase));

            var serializerOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            foreach (var filePath in jsonFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fileContent = await File.ReadAllTextAsync(filePath, cancellationToken);
                if (string.IsNullOrWhiteSpace(fileContent)) continue;
                
                try
                {
                    var cardData = JsonSerializer.Deserialize<IdCardData>(fileContent, serializerOptions);

                    if (cardData?.IsUnreadable == true)
                    {
                        Console.WriteLine($"Skipping unreadable file: {Path.GetFileName(filePath)}");
                        continue;
                    }

                    var rowData = new Dictionary<string, string?>
                    {
                        ["id"] = Path.GetFileNameWithoutExtension(filePath),
                        ["FullName"] = cardData?.FullName,
                        ["IdNumber"] = cardData?.IdNumber,
                        ["DateOfBirth"] = cardData?.DateOfBirth,
                        ["DateOfIssue"] = cardData?.DateOfIssue,
                        ["PlaceOfBirth"] = cardData?.PlaceOfBirth,
                        ["CountyOfIssue"] = cardData?.CountyOfIssue,
                        ["Authority"] = cardData?.Authority,
                        ["Gender"] = cardData?.Gender,
                        ["DateOfExpiry"] = cardData?.DateOfExpiry
                    };

                    var line = CsvHeaders.Select(header => rowData.GetValueOrDefault(header) ?? MISSING_FIELD_MARKER);
                    csvBuilder.AppendLine(string.Join(",", line));
                }
                catch (JsonException ex)
                {
                    Console.WriteLine($"[ERROR] Failed to parse JSON file {filePath}: {ex.Message}");
                }
            }

            await File.WriteAllTextAsync(csvOutputPath, csvBuilder.ToString(), cancellationToken);
            
            return csvOutputPath;
        }
    }
}