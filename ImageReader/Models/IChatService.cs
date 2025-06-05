

using System.Drawing;

namespace ImageReader.Models;

public interface IChatService
{
       Task<ChatResponseDto> SendImageMessageAsync(
            string prompt,
            string mimeType,
            byte[] imageBytes);

}