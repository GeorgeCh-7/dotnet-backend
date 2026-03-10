using System.Collections.Concurrent;
using System.Text.Json.Serialization;

namespace DotnetBackend.Services;

public class MetricsCollector
{
    private long _totalRequests;
    private long _successRequests;
    private long _clientErrors;
    private long _serverErrors;
    // stored as microseconds so we can use Interlocked.Add with a long
    private long _totalDurationUs;

    private readonly ConcurrentDictionary<string, long> _requestsByEndpoint = new();

    public void Record(string method, string path, int statusCode, double durationMs)
    {
        Interlocked.Increment(ref _totalRequests);
        Interlocked.Add(ref _totalDurationUs, (long)(durationMs * 1_000));

        if (statusCode >= 500) Interlocked.Increment(ref _serverErrors);
        else if (statusCode >= 400) Interlocked.Increment(ref _clientErrors);
        else Interlocked.Increment(ref _successRequests);

        _requestsByEndpoint.AddOrUpdate($"{method} {path}", 1, (_, n) => n + 1);
    }

    public MetricsSummary GetSummary()
    {
        var total = Interlocked.Read(ref _totalRequests);
        return new MetricsSummary
        {
            TotalRequests = total,
            SuccessRequests = Interlocked.Read(ref _successRequests),
            ClientErrors = Interlocked.Read(ref _clientErrors),
            ServerErrors = Interlocked.Read(ref _serverErrors),
            AverageResponseMs = total > 0
                ? Math.Round(Interlocked.Read(ref _totalDurationUs) / (total * 1_000.0), 2)
                : 0,
            RequestsByEndpoint = new Dictionary<string, long>(_requestsByEndpoint)
        };
    }
}

public class MetricsSummary
{
    [JsonPropertyName("totalRequests")]
    public long TotalRequests { get; init; }

    [JsonPropertyName("successRequests")]
    public long SuccessRequests { get; init; }

    [JsonPropertyName("clientErrors")]
    public long ClientErrors { get; init; }

    [JsonPropertyName("serverErrors")]
    public long ServerErrors { get; init; }

    [JsonPropertyName("averageResponseMs")]
    public double AverageResponseMs { get; init; }

    [JsonPropertyName("requestsByEndpoint")]
    public Dictionary<string, long> RequestsByEndpoint { get; init; } = [];
}
