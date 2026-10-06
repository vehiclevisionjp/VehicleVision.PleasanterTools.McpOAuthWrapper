namespace VehicleVision.PleasanterTools.McpOAuthWrapper.Bridge.Kvs;

public sealed class KvsMaintenance(IServiceScopeFactory scopes, ILogger<KvsMaintenance> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var backend = scope.ServiceProvider.GetRequiredService<KvsBackend>();
                var threshold = DateTimeOffset.UtcNow.AddDays(-1);
                await new KvsTokenStore(backend).PruneAsync(threshold, stoppingToken);
                await new KvsAuthorizationStore(backend).PruneAsync(threshold, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (StackExchange.Redis.RedisException) { logger.LogWarning("KVS の OAuth 状態を整理できませんでした。次回再試行します。"); }
        }
    }
}
