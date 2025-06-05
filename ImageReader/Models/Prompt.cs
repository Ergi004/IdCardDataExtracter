using System.ComponentModel.DataAnnotations;

namespace ImageReader.Models; 
public class Prompt
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string Content { get; set; } = string.Empty;

    [Required]
    public Role Role { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int ChatId { get; set; }
    public Chat Chat { get; set; } = null!;
}
