using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Xunit;
using DotnetBackend.Data;
using DotnetBackend.Models;

namespace DotnetBackend.Tests;

/// <summary>Unit tests for <see cref="DataStore"/> — no HTTP stack involved.</summary>
public class DataStoreTests : IDisposable
{
    private readonly DataStore _store;
    private readonly string _tempFile;

    public DataStoreTests()
    {
        _tempFile = Path.GetTempFileName();
        _store = CreateStore(_tempFile);
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile))
            File.Delete(_tempFile);
    }

    private static DataStore CreateStore(string? filePath = null)
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataFilePath"] = filePath ?? Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}.json")
            })
            .Build();
        return new DataStore(cache, config);
    }

    // ── Initial seed state ────────────────────────────────────────────────────

    [Fact]
    public void GetUsers_InitiallyReturnsSeedUsers()
    {
        var users = _store.GetUsers();

        Assert.Equal(3, users.Count);
        Assert.Contains(users, u => u.Email == "john@example.com");
        Assert.Contains(users, u => u.Email == "jane@example.com");
        Assert.Contains(users, u => u.Email == "bob@example.com");
    }

    [Fact]
    public void GetTasks_InitiallyReturnsSeedTasks()
    {
        var tasks = _store.GetTasks(null, null);

        Assert.Equal(3, tasks.Count);
        Assert.Contains(tasks, t => t.Status == "pending");
        Assert.Contains(tasks, t => t.Status == "in-progress");
        Assert.Contains(tasks, t => t.Status == "completed");
    }

    // ── User operations ───────────────────────────────────────────────────────

    [Fact]
    public void AddUser_IncreasesCountAndAssignsId()
    {
        var newUser = new User { Name = "Alice", Email = "alice@test.com", Role = "tester" };

        var created = _store.AddUser(newUser);

        Assert.Equal(4, created.Id);
        Assert.Equal(4, _store.GetUsers().Count);
    }

    [Fact]
    public void AddUser_AssignsMaxIdPlusOne()
    {
        var first  = _store.AddUser(new User { Name = "A", Email = "a@test.com", Role = "r" });
        var second = _store.AddUser(new User { Name = "B", Email = "b@test.com", Role = "r" });

        Assert.Equal(second.Id, first.Id + 1);
    }

    [Fact]
    public void AddUser_NewUserAppearsInGetUsers()
    {
        _store.AddUser(new User { Name = "Alice", Email = "alice@test.com", Role = "tester" });

        var users = _store.GetUsers();

        Assert.Contains(users, u => u.Email == "alice@test.com");
    }

    [Fact]
    public void GetUserById_ReturnsCorrectUser()
    {
        var user = _store.GetUserById(1);

        Assert.NotNull(user);
        Assert.Equal("john@example.com", user.Email);
    }

    [Fact]
    public void GetUserById_ReturnsNull_WhenNotFound()
    {
        var user = _store.GetUserById(9999);

        Assert.Null(user);
    }

    [Fact]
    public void UserExists_ReturnsTrue_ForExistingUser()
    {
        Assert.True(_store.UserExists(1));
    }

    [Fact]
    public void UserExists_ReturnsFalse_ForMissingUser()
    {
        Assert.False(_store.UserExists(9999));
    }

    // ── Task operations ───────────────────────────────────────────────────────

    [Fact]
    public void AddTask_IncreasesCountAndAssignsId()
    {
        var newTask = new TaskItem { Title = "New task", Status = "pending", UserId = 1 };

        var created = _store.AddTask(newTask);

        Assert.Equal(4, created.Id);
        Assert.Equal(4, _store.GetTasks(null, null).Count);
    }

    [Fact]
    public void AddTask_NewTaskAppearsInGetTasks()
    {
        _store.AddTask(new TaskItem { Title = "New task", Status = "pending", UserId = 1 });

        var tasks = _store.GetTasks(null, null);

        Assert.Contains(tasks, t => t.Title == "New task");
    }

    [Fact]
    public void GetTasks_FiltersByStatus()
    {
        var pending = _store.GetTasks("pending", null);

        Assert.All(pending, t => Assert.Equal("pending", t.Status));
        Assert.DoesNotContain(pending, t => t.Status == "completed");
    }

    [Fact]
    public void GetTasks_FiltersByUserId()
    {
        var tasks = _store.GetTasks(null, "1");

        Assert.All(tasks, t => Assert.Equal(1, t.UserId));
    }

    [Fact]
    public void GetTasks_FiltersByBothStatusAndUserId()
    {
        var tasks = _store.GetTasks("pending", "1");

        Assert.All(tasks, t =>
        {
            Assert.Equal("pending", t.Status);
            Assert.Equal(1, t.UserId);
        });
    }

    [Fact]
    public void GetTasks_UnrecognisedUserId_ReturnsAll()
    {
        // Non-integer userId is ignored — returns full list
        var tasks = _store.GetTasks(null, "not-a-number");

        Assert.Equal(3, tasks.Count);
    }

    // ── Task update ───────────────────────────────────────────────────────────

    [Fact]
    public void UpdateTask_UpdatesTitle()
    {
        var updated = _store.UpdateTask(1, "Updated title", null, null);

        Assert.NotNull(updated);
        Assert.Equal("Updated title", updated.Title);
    }

    [Fact]
    public void UpdateTask_UpdatesStatus()
    {
        var updated = _store.UpdateTask(1, null, "completed", null);

        Assert.NotNull(updated);
        Assert.Equal("completed", updated.Status);
    }

    [Fact]
    public void UpdateTask_PartialUpdate_OnlyChangesProvidedFields()
    {
        var original = _store.GetTasks(null, null).First(t => t.Id == 2);
        var originalTitle = original.Title;

        // Only update status, leave title and userId unchanged
        var updated = _store.UpdateTask(2, null, "completed", null);

        Assert.NotNull(updated);
        Assert.Equal(originalTitle, updated.Title);
        Assert.Equal("completed", updated.Status);
        Assert.Equal(original.UserId, updated.UserId);
    }

    [Fact]
    public void UpdateTask_ReturnsNull_WhenNotFound()
    {
        var result = _store.UpdateTask(9999, "title", null, null);

        Assert.Null(result);
    }

    // ── Stats ──────────────────────────────────────────────────────────────────

    [Fact]
    public void GetStats_ReturnsCorrectTotals()
    {
        var stats = _store.GetStats();

        Assert.Equal(3, stats.Users.Total);
        Assert.Equal(3, stats.Tasks.Total);
    }

    [Fact]
    public void GetStats_ReturnsCorrectStatusBreakdown()
    {
        var stats = _store.GetStats();

        Assert.Equal(1, stats.Tasks.Pending);
        Assert.Equal(1, stats.Tasks.InProgress);
        Assert.Equal(1, stats.Tasks.Completed);
    }

    [Fact]
    public void GetStats_UpdatesAfterAddingTask()
    {
        _store.AddTask(new TaskItem { Title = "Extra", Status = "pending", UserId = 1 });
        var stats = _store.GetStats();

        Assert.Equal(2, stats.Tasks.Pending);
        Assert.Equal(4, stats.Tasks.Total);
    }

    // ── Persistence ───────────────────────────────────────────────────────────

    [Fact]
    public void AddUser_PersistsToFile()
    {
        _store.AddUser(new User { Name = "Alice", Email = "alice@test.com", Role = "tester" });

        // Create a new store instance pointing to the same file — it should load Alice
        var store2 = CreateStore(_tempFile);
        var users = store2.GetUsers();

        Assert.Contains(users, u => u.Email == "alice@test.com");
    }

    [Fact]
    public void AddTask_PersistsToFile()
    {
        _store.AddTask(new TaskItem { Title = "Persisted task", Status = "pending", UserId = 1 });

        var store2 = CreateStore(_tempFile);
        var tasks = store2.GetTasks(null, null);

        Assert.Contains(tasks, t => t.Title == "Persisted task");
    }
}
