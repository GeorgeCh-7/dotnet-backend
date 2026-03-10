using Microsoft.EntityFrameworkCore;
using DotnetBackend.Models;

namespace DotnetBackend.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>().HasData(
            new User { Id = 1, Name = "John Doe", Email = "john@example.com", Role = "developer" },
            new User { Id = 2, Name = "Jane Smith", Email = "jane@example.com", Role = "designer" },
            new User { Id = 3, Name = "Bob Johnson", Email = "bob@example.com", Role = "manager" }
        );

        modelBuilder.Entity<TaskItem>().HasData(
            new TaskItem { Id = 1, Title = "Implement authentication", Status = "pending", UserId = 1 },
            new TaskItem { Id = 2, Title = "Design user interface", Status = "in-progress", UserId = 2 },
            new TaskItem { Id = 3, Title = "Review code changes", Status = "completed", UserId = 3 }
        );
    }
}
