using DotnetBackend.Models;

namespace DotnetBackend.Data;

/// <summary>Storage backend abstraction — swappable between the JSON-backed in-memory store and SQLite.</summary>
public interface IDataStore
{
    string StorageInfo { get; }

    List<User> GetUsers();
    User? GetUserById(int id);
    bool UserExists(int id);
    List<TaskItem> GetTasks(string? status, string? userId);
    StatsResponse GetStats();
    User AddUser(User user);
    TaskItem AddTask(TaskItem task);

    /// <summary>Partial update — only non-null arguments are applied.</summary>
    TaskItem? UpdateTask(int id, string? title, string? status, int? userId);
}
