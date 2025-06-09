// Services/IIdCardUploadService.cs
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ImageReader.Models;

namespace ImageReader.Services
{
    public interface IIdCardUploadService
    {
        /// <summary>
        /// Processes the configured upload directories in parallel, each using its own Gemini API key.
        /// Returns one IdCardReturnResult per folder.
        /// </summary>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        Task<IEnumerable<IdCardReturnResult>> ProcessUploadsAsync(
            CancellationToken cancellationToken = default);
    }
}