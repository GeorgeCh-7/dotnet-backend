using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace DotnetBackend.Tests;

/// <summary>
/// Integration tests that spin up the full ASP.NET Core pipeline via
/// <see cref="WebApplicationFactory{TEntryPoint}"/> and exercise every endpoint.
/// </summary>
public class ApiIntegrationTests : IClassFixture<ApiIntegrationTests.AppFactory>
{
    private readonly HttpClient _client;

    private const string TestApiKey = "test-api-key";

    public ApiIntegrationTests(AppFactory factory)
    {
        _client = factory.CreateClient();
        // All test requests carry the API key by default
        _client.DefaultRequestHeaders.Add("X-API-Key", TestApiKey);
    }

    /// <summary>
    /// Custom factory that isolates tests from the development data file,
    /// sets a known API key, and disables the rate limiter so test suites
    /// never receive 429 responses.
    /// </summary>
    public class AppFactory : WebApplicationFactory<Program>
    {
        private readonly string _tempFile = Path.GetTempFileName();

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseSetting("DataFilePath", _tempFile);
            builder.UseSetting("RequireApiKey", "true");
            builder.UseSetting("ApiKeys:0", TestApiKey);
            builder.UseSetting("RateLimitPerMinute", "10000"); // effectively unlimited in tests
            builder.UseSetting("UseDatabase", "false");
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && File.Exists(_tempFile))
                File.Delete(_tempFile);
        }
    }

    // ── Health (public — no API key needed) ────────────────────────────────────

    [Fact]
    public async Task GET_Health_Returns200WithOkStatus()
    {
        // Health is a public endpoint — test without key to verify bypass works
        using var client = new AppFactory().CreateClient();

        var response = await client.GetAsync("/health");
        var body     = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", body.GetProperty("status").GetString());
        Assert.Equal(".NET backend is running", body.GetProperty("message").GetString());
    }

    // ── Authentication ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GET_Users_Returns401_WhenNoApiKey()
    {
        using var client = new AppFactory().CreateClient(); // no key on default headers

        var response = await client.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GET_Users_Returns401_WhenApiKeyInvalid()
    {
        using var client = new AppFactory().CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", "wrong-key");

        var response = await client.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── Metrics ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GET_Metrics_Returns200WithMetricsShape()
    {
        // Make a request first so there's something to report
        await _client.GetAsync("/api/users");

        var response = await _client.GetAsync("/metrics");
        var body     = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(body.GetProperty("totalRequests").GetInt64() > 0);
        Assert.Equal(JsonValueKind.Object, body.GetProperty("requestsByEndpoint").ValueKind);
    }

    // ── Users – GET ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GET_Users_Returns200WithUsersArray()
    {
        var response = await _client.GetAsync("/api/users");
        var body     = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(body.GetProperty("count").GetInt32() >= 3);
        Assert.Equal(JsonValueKind.Array, body.GetProperty("users").ValueKind);
    }

    [Fact]
    public async Task GET_UserById_Returns200_ForExistingUser()
    {
        var response = await _client.GetAsync("/api/users/1");
        var body     = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, body.GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task GET_UserById_Returns404_ForMissingUser()
    {
        var response = await _client.GetAsync("/api/users/9999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Users – POST ───────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_Users_Returns201_WithCreatedUser()
    {
        var payload  = new { name = "Alice", email = "alice@test.com", role = "tester" };
        var response = await _client.PostAsJsonAsync("/api/users", payload);
        var body     = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("Alice", body.GetProperty("name").GetString());
        Assert.Equal("alice@test.com", body.GetProperty("email").GetString());
        Assert.True(body.GetProperty("id").GetInt32() > 0);
    }

    [Fact]
    public async Task POST_Users_Returns400_WhenNameMissing()
    {
        var response = await _client.PostAsJsonAsync("/api/users", new { email = "x@test.com", role = "dev" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task POST_Users_Returns400_WhenEmailMissing()
    {
        var response = await _client.PostAsJsonAsync("/api/users", new { name = "Bob", role = "dev" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task POST_Users_Returns400_WhenEmailInvalid()
    {
        var response = await _client.PostAsJsonAsync("/api/users", new { name = "Bob", email = "not-an-email", role = "dev" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task POST_Users_Returns400_WhenRoleMissing()
    {
        var response = await _client.PostAsJsonAsync("/api/users", new { name = "Bob", email = "bob@test.com" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task POST_Users_NewUserAppearsInGetUsers()
    {
        await _client.PostAsJsonAsync("/api/users", new { name = "NewUser", email = "newuser@test.com", role = "dev" });

        var body  = await (await _client.GetAsync("/api/users")).Content.ReadFromJsonAsync<JsonElement>();
        var users = body.GetProperty("users").EnumerateArray();

        Assert.Contains(users, u => u.GetProperty("email").GetString() == "newuser@test.com");
    }

    // ── Tasks – GET ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GET_Tasks_Returns200WithTasksArray()
    {
        var response = await _client.GetAsync("/api/tasks");
        var body     = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(JsonValueKind.Array, body.GetProperty("tasks").ValueKind);
    }

    [Fact]
    public async Task GET_Tasks_FiltersByStatus()
    {
        var body  = await (await _client.GetAsync("/api/tasks?status=pending")).Content.ReadFromJsonAsync<JsonElement>();
        var tasks = body.GetProperty("tasks").EnumerateArray().ToList();

        Assert.NotEmpty(tasks);
        Assert.All(tasks, t => Assert.Equal("pending", t.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task GET_Tasks_FiltersByUserId()
    {
        var body  = await (await _client.GetAsync("/api/tasks?userId=1")).Content.ReadFromJsonAsync<JsonElement>();
        var tasks = body.GetProperty("tasks").EnumerateArray().ToList();

        Assert.NotEmpty(tasks);
        Assert.All(tasks, t => Assert.Equal(1, t.GetProperty("userId").GetInt32()));
    }

    // ── Tasks – POST ───────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_Tasks_Returns201_WithCreatedTask()
    {
        var response = await _client.PostAsJsonAsync("/api/tasks",
            new { title = "Write tests", status = "pending", userId = 1 });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("Write tests", body.GetProperty("title").GetString());
        Assert.True(body.GetProperty("id").GetInt32() > 0);
    }

    [Fact]
    public async Task POST_Tasks_Returns400_WhenTitleMissing()
    {
        var response = await _client.PostAsJsonAsync("/api/tasks", new { status = "pending", userId = 1 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task POST_Tasks_Returns400_WhenStatusInvalid()
    {
        var response = await _client.PostAsJsonAsync("/api/tasks",
            new { title = "Task", status = "invalid-status", userId = 1 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task POST_Tasks_Returns400_WhenUserIdDoesNotExist()
    {
        var response = await _client.PostAsJsonAsync("/api/tasks",
            new { title = "Task", status = "pending", userId = 9999 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task POST_Tasks_NewTaskAppearsInGetTasks()
    {
        await _client.PostAsJsonAsync("/api/tasks",
            new { title = "Integration task", status = "in-progress", userId = 1 });

        var body  = await (await _client.GetAsync("/api/tasks")).Content.ReadFromJsonAsync<JsonElement>();
        var tasks = body.GetProperty("tasks").EnumerateArray();

        Assert.Contains(tasks, t => t.GetProperty("title").GetString() == "Integration task");
    }

    // ── Tasks – PUT ────────────────────────────────────────────────────────────

    [Fact]
    public async Task PUT_Tasks_Returns200_WithUpdatedTask()
    {
        var response = await _client.PutAsJsonAsync("/api/tasks/1", new { status = "completed" });
        var body     = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("completed", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task PUT_Tasks_SupportsPartialUpdate()
    {
        var original      = await (await _client.GetAsync("/api/tasks?userId=2")).Content.ReadFromJsonAsync<JsonElement>();
        var originalStatus = original.GetProperty("tasks")[0].GetProperty("status").GetString();

        await _client.PutAsJsonAsync("/api/tasks/2", new { title = "Updated title only" });

        var updated     = await (await _client.GetAsync("/api/tasks?userId=2")).Content.ReadFromJsonAsync<JsonElement>();
        var updatedTask = updated.GetProperty("tasks")[0];

        Assert.Equal("Updated title only", updatedTask.GetProperty("title").GetString());
        Assert.Equal(originalStatus, updatedTask.GetProperty("status").GetString());
    }

    [Fact]
    public async Task PUT_Tasks_Returns404_WhenTaskNotFound()
    {
        var response = await _client.PutAsJsonAsync("/api/tasks/9999", new { title = "Ghost" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PUT_Tasks_Returns400_WhenStatusInvalid()
    {
        var response = await _client.PutAsJsonAsync("/api/tasks/1", new { status = "wrong-status" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PUT_Tasks_Returns400_WhenUserIdDoesNotExist()
    {
        var response = await _client.PutAsJsonAsync("/api/tasks/1", new { userId = 9999 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Stats ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GET_Stats_Returns200WithStatsSummary()
    {
        var response = await _client.GetAsync("/api/stats");
        var body     = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(body.GetProperty("users").GetProperty("total").GetInt32() >= 3);
        Assert.True(body.GetProperty("tasks").GetProperty("total").GetInt32() >= 3);
    }
}
