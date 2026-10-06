using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace VehicleVision.PleasanterTools.McpOAuthWrapper.Bridge;

public sealed class LoginGuard(IDbContextFactory<OAuthState> factory)
{
    private readonly SemaphoreSlim _gate = new(1);

    // 失敗回数を OAuth 用 DB に保持し、再起動や IP の変更でも制限を維持する。
    public async Task<bool> TryAttemptAsync(int tenantId, string loginId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            var id = Key(tenantId, loginId);
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

    public async Task ResetAsync(int tenantId, string loginId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            await db.LoginAttempts.Where(x => x.Id == Key(tenantId, loginId)).ExecuteDeleteAsync(cancellationToken);
        }
        finally { _gate.Release(); }
    }

    private static string Key(int tenantId, string loginId) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes($"{tenantId}:{loginId.ToUpperInvariant()}")));
}
