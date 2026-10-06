using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace VehicleVision.PleasanterTools.McpOAuthWrapper.Bridge;

public sealed class SqliteBridgeState(IDbContextFactory<OAuthState> factory) : IBridgeState
{
    private readonly SemaphoreSlim _gate = new(1);

    // 失敗回数を OAuth 用 DB に保持し、再起動や IP の変更でも制限を維持する。
    public async Task<bool> TryAttemptAsync(string id, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);

            var now = DateTime.UtcNow;
            await db.LoginAttempts.Where(x => x.WindowStart < now.AddMinutes(-15)).ExecuteDeleteAsync(cancellationToken);
            var attempt = await db.LoginAttempts.FindAsync([id], cancellationToken);
            if (attempt is null)
            {
                attempt = new LoginAttempt { Id = id, WindowStart = now };
                db.Add(attempt);
            }
            if (attempt.WindowStart.AddMinutes(15) <= now)
            {
                attempt.WindowStart = now;
                attempt.Count = 0;
            }
            if (attempt.Count >= 5) return false;
            attempt.Count++;
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        finally { _gate.Release(); }
    }

    // 成功した試行の 1 回分だけを戻す。過去の失敗は残し、成功で制限を回避させない。
    public async Task ReleaseAsync(string id, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            var attempt = await db.LoginAttempts.FindAsync([id], cancellationToken);
            if (attempt is null) return;
            if (attempt.Count > 1) attempt.Count--;
            else db.Remove(attempt);
            await db.SaveChangesAsync(cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task CreateConsentAsync(string id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.PendingConsents.Where(x => x.ExpiresAt < DateTime.UtcNow).ExecuteDeleteAsync(ct);
        db.Add(new PendingConsent { Id = id, ExpiresAt = DateTime.UtcNow.AddMinutes(5) });
        await db.SaveChangesAsync(ct);
    }
    public async Task<bool> ConsumeConsentAsync(string id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.PendingConsents.Where(x => x.Id == id && x.ExpiresAt > DateTime.UtcNow).ExecuteDeleteAsync(ct) == 1;
    }
}

public interface IBridgeState
{
    Task<bool> TryAttemptAsync(string id, CancellationToken ct);
    Task ReleaseAsync(string id, CancellationToken ct);
    Task CreateConsentAsync(string id, CancellationToken ct);
    Task<bool> ConsumeConsentAsync(string id, CancellationToken ct);
}

public sealed class LoginGuard(IBridgeState state)
{
    public Task<bool> TryAttemptAsync(int tenantId, string loginId, CancellationToken cancellationToken, int? userId = null) => state.TryAttemptAsync(Key(tenantId, loginId, userId), cancellationToken);
    public Task ReleaseAsync(int tenantId, string loginId, CancellationToken cancellationToken, int? userId = null) => state.ReleaseAsync(Key(tenantId, loginId, userId), cancellationToken);
    private static string Key(int tenantId, string loginId, int? userId) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{tenantId}:" + (userId.HasValue ? $"user:{userId.Value}" : $"login:{loginId.ToUpperInvariant()}"))));
}
