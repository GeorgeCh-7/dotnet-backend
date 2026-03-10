namespace DotnetBackend.Middleware;

public class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly bool _enabled;
    private readonly IReadOnlySet<string> _validKeys;
    private const string ApiKeyHeader = "X-API-Key";

    public ApiKeyMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _enabled = configuration.GetValue<bool>("RequireApiKey");
        var keys = configuration.GetSection("ApiKeys").Get<string[]>() ?? [];
        _validKeys = new HashSet<string>(keys, StringComparer.Ordinal);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_enabled || IsPublic(context.Request.Path.Value ?? ""))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(ApiKeyHeader, out var keyValues) || keyValues.Count == 0)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(
                new { error = $"API key required. Add the '{ApiKeyHeader}' header." });
            return;
        }

        if (!_validKeys.Contains(keyValues.ToString()))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new { error = "Invalid API key." });
            return;
        }

        await _next(context);
    }

    private static bool IsPublic(string path) =>
        path == "/health" || path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase);
}
