// Entity Framework Core DbContext for the application
// Used by EF migrations and runtime database access

using Microsoft.EntityFrameworkCore;

namespace WebApp.Data;

/// <summary>
/// Application database context.
/// Referenced in build.ando via: Ef.DbContextFrom(WebApp, "AppDbContext")
/// </summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<TodoItem> Todos => Set<TodoItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TodoItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
        });
    }
}

/// <summary>
/// Simple todo item entity for demonstration.
/// </summary>
public class TodoItem
{
    public int Id { get; set; }
    public required string Title { get; set; }
    public bool IsComplete { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
