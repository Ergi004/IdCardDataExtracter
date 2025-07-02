namespace ImageReader.Models
{
    public interface IJsonToCsvService
    {
        /// <summary>
        /// Scans the 'Outputs' directory for JSON files, processes them, and generates a single CSV file.
        /// </summary>
        /// <param name="outputCsvFileName">The name of the CSV file to be created in the 'Outputs' directory.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>The full path to the generated CSV file.</returns>
        Task<string> CreateCsvFromJsonsAsync(
            string outputCsvFileName = "summary.csv",
            CancellationToken cancellationToken = default);
    }
}