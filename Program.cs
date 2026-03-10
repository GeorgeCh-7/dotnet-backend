using System.Diagnostics;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using DotnetBackend.Data;
using DotnetBackend.HealthChecks;
using DotnetBackend.Middleware;
using DotnetBackend.Models;
using DotnetBackend.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMemoryCache();

// Switch between SQLite (UseDatabase=true) and the default JSON-backed in-memory store
if (builder.Configuration.GetValue<bool>("UseDatabase"))
{
    var dbPath = builder.Configuration["DatabasePath"] is { Length: > 0 } p
        ? p
        : Path.Combine(AppContext.BaseDirectory, "app.db");

    builder.Services.AddDbContextFactory<AppDbContext>(opts =>
        opts.UseSqlite($"Data Source={dbPath}"));

    builder.Services.AddSingleton<IDataStore, DatabaseDataStore>();
}
else
{
    builder.Services.AddSingleton<IDataStore, DataStore>();
}

builder.Services.AddSingleton<MetricsCollector>();

builder.Services.AddHealthChecks()
    .AddCheck<DataStoreHealthCheck>("datastore");

// RateLimitPerMinute is overridable in tests so the test suite never hits 429
var rateLimit = builder.Configuration.GetValue<int>("RateLimitPerMinute", 100);

builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = rateLimit,
                Window = TimeSpan.FromMinutes(1)
            }));

    options.OnRejected = async (ctx, token) =>
    {
        ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        ctx.HttpContext.Response.ContentType = "application/json";
        await ctx.HttpContext.Response.WriteAsJsonAsync(
            new { error = "Rate limit exceeded. Please slow down and try again." }, token);
    };
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

var app = builder.Build();

// Logging wraps everything so we get a log line for every request, including
// rate-limited and auth-rejected ones with the correct final status code.
app.Use(async (context, next) =>
{
    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
    var metrics = context.RequestServices.GetRequiredService<MetricsCollector>();
    var start = Stopwatch.GetTimestamp();

    await next();

    var elapsed = Stopwatch.GetElapsedTime(start);
    logger.LogInformation("{Method} {Path} {StatusCode} {ElapsedMs}ms",
        context.Request.Method,
        context.Request.Path,
        context.Response.StatusCode,
        elapsed.TotalMilliseconds.ToString("F1"));

    metrics.Record(context.Request.Method, context.Request.Path,
        context.Response.StatusCode, elapsed.TotalMilliseconds);
});

app.UseExceptionHandler(errApp =>
{
    errApp.Run(async context =>
    {
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        var ex = context.Features.Get<IExceptionHandlerFeature>()?.Error;

        if (ex is not null)
            logger.LogError(ex, "Unhandled exception processing {Method} {Path}",
                context.Request.Method, context.Request.Path);

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { error = "An internal server error occurred" });
    });
});

app.UseCors();
app.UseRateLimiter();
app.UseMiddleware<ApiKeyMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

const int defaultPort = 8080;
var portEnv = Environment.GetEnvironmentVariable("PORT");
if (!int.TryParse(portEnv, out var port))
    port = defaultPort;

// /health is a public endpoint — ApiKeyMiddleware bypasses it
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            status = report.Status == HealthStatus.Healthy ? "ok" : report.Status.ToString().ToLower(),
            message = ".NET backend is running",
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString().ToLower(),
                description = e.Value.Description,
                data = e.Value.Data
            })
        });
    }
});

app.MapGet("/metrics", (MetricsCollector metrics) => Results.Json(metrics.GetSummary()));

app.MapGet("/api/users", (IDataStore store) =>
{
    var users = store.GetUsers();
    return Results.Json(new UsersResponse { Users = users, Count = users.Count });
});

app.MapGet("/api/users/{id:int}", (int id, IDataStore store) =>
{
    var user = store.GetUserById(id);
    return user is null
        ? Results.NotFound(new { error = "User not found" })
        : Results.Json(user);
});

app.MapGet("/api/tasks", (string? status, string? userId, IDataStore store) =>
{
    var tasks = store.GetTasks(status, userId);
    return Results.Json(new TasksResponse { Tasks = tasks, Count = tasks.Count });
});

app.MapGet("/api/stats", (IDataStore store) => Results.Json(store.GetStats()));

app.MapPost("/api/users", (CreateUserRequest req, IDataStore store) =>
{
    var user = new User { Name = req.Name!.Trim(), Email = req.Email!.Trim(), Role = req.Role!.Trim() };
    var created = store.AddUser(user);
    return Results.Created($"/api/users/{created.Id}", created);
})
.AddEndpointFilter<ValidationFilter<CreateUserRequest>>();

app.MapPost("/api/tasks", (CreateTaskRequest req, IDataStore store) =>
{
    if (!store.UserExists(req.UserId!.Value))
        return Results.BadRequest(new { error = $"User with id {req.UserId} not found" });

    var task = new TaskItem { Title = req.Title!.Trim(), Status = req.Status!, UserId = req.UserId.Value };
    var created = store.AddTask(task);
    return Results.Created($"/api/tasks/{created.Id}", created);
})
.AddEndpointFilter<ValidationFilter<CreateTaskRequest>>();

app.MapPut("/api/tasks/{id:int}", (int id, UpdateTaskRequest req, IDataStore store) =>
{
    if (req.UserId.HasValue && !store.UserExists(req.UserId.Value))
        return Results.BadRequest(new { error = $"User with id {req.UserId} not found" });

    var updated = store.UpdateTask(id, req.Title?.Trim(), req.Status, req.UserId);
    return updated is null
        ? Results.NotFound(new { error = "Task not found" })
        : Results.Json(updated);
})
.AddEndpointFilter<ValidationFilter<UpdateTaskRequest>>();

app.Run($"http://0.0.0.0:{port}");

// Required for WebApplicationFactory in integration tests
public partial class Program { }
