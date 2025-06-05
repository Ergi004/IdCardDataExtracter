using ImageReader.Models;
using Microsoft.EntityFrameworkCore;


namespace ImageReader.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
      : base(options) { }

    public DbSet<Chat> Chats { get; set; } = null!;
    public DbSet<Prompt> Prompts { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<Chat>()
               .Property(c => c.CreatedAt)
               .HasDefaultValueSql("GETDATE()");

        builder.Entity<Prompt>()
               .Property(p => p.CreatedAt)
               .HasDefaultValueSql("GETDATE()");
        builder.Entity<Prompt>().Property(p => p.Role).HasConversion<string>();
    }
}