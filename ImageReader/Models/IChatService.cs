using System.Threading.Tasks;
using ImageReader.Models;

namespace ImageReader.Services
{
    public interface IChatService
    {
        Task<ChatResponseDto> SendImageMessageAsync(
            string prompt,
            string mimeType,
            byte[] imageBytes);
    }
}