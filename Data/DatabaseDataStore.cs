using Microsoft.EntityFrameworkCore;
using DotnetBackend.Models;

namespace DotnetBackend.Data;

public class DatabaseDataStore : IDataStore
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly string _dbPath;

    public string StorageInfo => $"SQLite: {_dbPath}";

    public DatabaseDataStore(IDbContextFactory<AppDbContext> contextFactory, IConfiguration configuration)
    {
        _contextFactory = contextFactory;
        _dbPath = configuration["DatabasePath"] is { Length: > 0 } p
            ? p
            : Path.Combine(AppContext.BaseDirectory, "app.db");

        using var ctx = contextFactory.CreateDbContext();
        ctx.Database.EnsureCreated();
    }

    public List<User> GetUsers()
    {
        using var ctx = _contextFactory.CreateDbContext();
        return ctx.Users.ToList();
    }

    public User? GetUserById(int id)
    {
        using var ctx = _contextFactory.CreateDbContext();
        return ctx.Users.Find(id);
    }

    public bool UserExists(int id)
    {
        using var ctx = _contextFactory.CreateDbContext();
        return ctx.Users.Any(u => u.Id == id);
    }

    public List<TaskItem> GetTasks(string? status, string? userId)
    {
        using var ctx = _contextFactory.CreateDbContext();
        IQueryable<TaskItem> query = ctx.Tasks;

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(t => t.Status == status);

        if (!string.IsNullOrWhiteSpace(userId) && int.TryParse(userId, out var uid))
            query = query.Where(t => t.UserId == uid);

        return query.ToList();
    }

    public StatsResponse GetStats()
    {
        using var ctx = _contextFactory.CreateDbContext();
        return new StatsResponse
        {
            Users = { Total = ctx.Users.Count() },
            Tasks =
            {
                Total = ctx.Tasks.Count(),
                Pending = ctx.Tasks.Count(t => t.Status == "pending"),
                InProgress = ctx.Tasks.Count(t => t.Status == "in-progress"),
                Completed = ctx.Tasks.Count(t => t.Status == "completed")
            }
        };
    }

    public User AddUser(User user)
    {
        using var ctx = _contextFactory.CreateDbContext();
        ctx.Users.Add(user);
        ctx.SaveChanges();
        return user;
    }

    public TaskItem AddTask(TaskItem task)
    {
        using var ctx = _contextFactory.CreateDbContext();
        ctx.Tasks.Add(task);
        ctx.SaveChanges();
        return task;
    }

    public TaskItem? UpdateTask(int id, string? title, string? status, int? userId)
    {
        using var ctx = _contextFactory.CreateDbContext();
        var task = ctx.Tasks.Find(id);
        if (task is null) return null;

        if (title is not null) task.Title = title;
        if (status is not null) task.Status = status;
        if (userId.HasValue) task.UserId = userId.Value;

        ctx.SaveChanges();
        return task;
    }
}
