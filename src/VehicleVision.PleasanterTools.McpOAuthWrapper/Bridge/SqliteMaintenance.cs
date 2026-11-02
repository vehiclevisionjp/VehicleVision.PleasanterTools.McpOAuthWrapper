using OpenIddict.Abstractions;

namespace VehicleVision.PleasanterTools.McpOAuthWrapper.Bridge;

// SQLite モードで、期限切れ・失効済みのトークンと認可を定期的に削除する。
// Redis モードの KvsMaintenance と同じ周期・基準（作成から 1 日より古いもの）にそろえる。
public sealed class SqliteMaintenance(IServiceScopeFactory scopes, ILogger<SqliteMaintenance> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan Retention = TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await PruneAsync(scopes, DateTimeOffset.UtcNow - Retention, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // 整理に失敗しても認可・MCP 通信は止めない。次の周期で再試行する。
                logger.LogWarning(ex, "SQLite の OAuth 状態を整理できませんでした。次回再試行します。");
            }
        }
    }

    // トークンを先に消し、そのあとで有効なトークンを持たない認可を消す（OpenIddict の標準の順序）。
    public static async Task PruneAsync(IServiceScopeFactory scopes, DateTimeOffset threshold, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var tokens = scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();
        var authorizations = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
        await tokens.PruneAsync(threshold, cancellationToken);
        await authorizations.PruneAsync(threshold, cancellationToken);
    }
}
