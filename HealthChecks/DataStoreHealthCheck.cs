using Microsoft.Extensions.Diagnostics.HealthChecks;
using DotnetBackend.Data;

namespace DotnetBackend.HealthChecks;

public class DataStoreHealthCheck : IHealthCheck
{
    private readonly IDataStore _store;

    public DataStoreHealthCheck(IDataStore store)
    {
        _store = store;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var users = _store.GetUsers();
            var tasks = _store.GetTasks(null, null);

            var data = new Dictionary<string, object>
            {
                ["usersCount"] = users.Count,
                ["tasksCount"] = tasks.Count,
                ["storage"] = _store.StorageInfo
            };

            return Task.FromResult(HealthCheckResult.Healthy("DataStore is accessible", data));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("DataStore check failed", ex));
        }
    }
}
