using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using DotnetBackend.Models;

namespace DotnetBackend.Data;

public class DataStore : IDataStore
{
    private readonly ReaderWriterLockSlim _lock = new();
    private readonly IMemoryCache _cache;
    private readonly List<User> _users;
    private readonly List<TaskItem> _tasks;

    private const string UsersCacheKey = "users";
    private const string TasksCacheKey = "tasks";
    private const string StatsCacheKey = "stats";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    public string DataFilePath { get; }
    public string StorageInfo => $"JSON file: {DataFilePath}";

    public DataStore(IMemoryCache cache, IConfiguration configuration)
    {
        _cache = cache;
        DataFilePath = configuration["DataFilePath"]
            ?? Path.Combine(AppContext.BaseDirectory, "data.json");

        var snapshot = LoadFromFile();
        _users = snapshot?.Users ?? SeedUsers();
        _tasks = snapshot?.Tasks ?? SeedTasks();

        if (snapshot is null)
            SaveToFile();
    }

    public List<User> GetUsers()
    {
        if (_cache.TryGetValue(UsersCacheKey, out List<User>? cached) && cached is not null)
            return cached;

        _lock.EnterReadLock();
        try
        {
            var users = _users.ToList();
            _cache.Set(UsersCacheKey, users, CacheTtl);
            return users;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public User? GetUserById(int id)
    {
        _lock.EnterReadLock();
        try
        {
            return _users.FirstOrDefault(u => u.Id == id);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public bool UserExists(int id)
    {
        _lock.EnterReadLock();
        try
        {
            return _users.Any(u => u.Id == id);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    // Cache the full unfiltered list and apply filters in-memory.
    // This keeps cache invalidation simple — one key per entity type.
    public List<TaskItem> GetTasks(string? status, string? userId)
    {
        if (!_cache.TryGetValue(TasksCacheKey, out List<TaskItem>? allTasks) || allTasks is null)
        {
            _lock.EnterReadLock();
            try
            {
                allTasks = _tasks.ToList();
                _cache.Set(TasksCacheKey, allTasks, CacheTtl);
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        IEnumerable<TaskItem> query = allTasks;

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(t => t.Status == status);

        if (!string.IsNullOrWhiteSpace(userId) && int.TryParse(userId, out var uid))
            query = query.Where(t => t.UserId == uid);

        return query.ToList();
    }

    public StatsResponse GetStats()
    {
        if (_cache.TryGetValue(StatsCacheKey, out StatsResponse? cached) && cached is not null)
            return cached;

        _lock.EnterReadLock();
        try
        {
            var stats = new StatsResponse
            {
                Users = { Total = _users.Count },
                Tasks = { Total = _tasks.Count }
            };

            foreach (var task in _tasks)
            {
                switch (task.Status)
                {
                    case "pending": stats.Tasks.Pending++; break;
                    case "in-progress": stats.Tasks.InProgress++; break;
                    case "completed": stats.Tasks.Completed++; break;
                }
            }

            _cache.Set(StatsCacheKey, stats, CacheTtl);
            return stats;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public User AddUser(User user)
    {
        _lock.EnterWriteLock();
        try
        {
            user.Id = _users.Count > 0 ? _users.Max(u => u.Id) + 1 : 1;
            _users.Add(user);
        }
        finally
        {
            _lock.ExitWriteLock();
        }

        SaveToFile();
        _cache.Remove(UsersCacheKey);
        _cache.Remove(StatsCacheKey);
        return user;
    }

    public TaskItem AddTask(TaskItem task)
    {
        _lock.EnterWriteLock();
        try
        {
            task.Id = _tasks.Count > 0 ? _tasks.Max(t => t.Id) + 1 : 1;
            _tasks.Add(task);
        }
        finally
        {
            _lock.ExitWriteLock();
        }

        SaveToFile();
        _cache.Remove(TasksCacheKey);
        _cache.Remove(StatsCacheKey);
        return task;
    }

    // Only non-null arguments are applied so callers can do partial updates
    public TaskItem? UpdateTask(int id, string? title, string? status, int? userId)
    {
        TaskItem? task;

        _lock.EnterWriteLock();
        try
        {
            task = _tasks.FirstOrDefault(t => t.Id == id);
            if (task is null) return null;

            if (title is not null) task.Title = title;
            if (status is not null) task.Status = status;
            if (userId.HasValue) task.UserId = userId.Value;
        }
        finally
        {
            _lock.ExitWriteLock();
        }

        SaveToFile();
        _cache.Remove(TasksCacheKey);
        _cache.Remove(StatsCacheKey);
        return task;
    }

    private DataSnapshot? LoadFromFile()
    {
        try
        {
            if (!File.Exists(DataFilePath)) return null;
            var json = File.ReadAllText(DataFilePath);
            return JsonSerializer.Deserialize<DataSnapshot>(json);
        }
        catch
        {
            // Corrupt or unreadable — fall back to seed data
            return null;
        }
    }

    private void SaveToFile()
    {
        try
        {
            _lock.EnterReadLock();
            DataSnapshot snapshot;
            try
            {
                snapshot = new DataSnapshot { Users = _users.ToList(), Tasks = _tasks.ToList() };
            }
            finally
            {
                _lock.ExitReadLock();
            }

            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(DataFilePath, json);
        }
        catch
        {
            // Non-critical — data is still in memory
        }
    }

    private static List<User> SeedUsers() =>
    [
        new() { Id = 1, Name = "John Doe", Email = "john@example.com", Role = "developer" },
        new() { Id = 2, Name = "Jane Smith", Email = "jane@example.com", Role = "designer" },
        new() { Id = 3, Name = "Bob Johnson", Email = "bob@example.com", Role = "manager" }
    ];

    private static List<TaskItem> SeedTasks() =>
    [
        new() { Id = 1, Title = "Implement authentication", Status = "pending", UserId = 1 },
        new() { Id = 2, Title = "Design user interface", Status = "in-progress", UserId = 2 },
        new() { Id = 3, Title = "Review code changes", Status = "completed", UserId = 3 }
    ];

    private class DataSnapshot
    {
        [JsonPropertyName("users")]
        public List<User> Users { get; set; } = [];

        [JsonPropertyName("tasks")]
        public List<TaskItem> Tasks { get; set; } = [];
    }
}
