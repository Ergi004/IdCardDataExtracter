
using System.ComponentModel.DataAnnotations;

namespace ImageReader.Models;
public class Chat
{
    [Key]
    public int Id { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Prompt> Prompts { get; set; } = new List<Prompt>();
}
