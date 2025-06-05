using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ImageReader.Models;

namespace ImageReader.Services
{
    public interface IIdCardUploadService
    {
        /// <summary>
        /// For each .jpg/.jpeg/.png in uploadsPath, send it to Gemini for OCR,
        /// write a .txt file in Outputs/, and return a list of results.
        /// </summary>
        Task<IList<ImageTextResult>> ProcessUploadsAsync(
            string uploadsPath,
            CancellationToken cancellationToken = default);
    }
}
