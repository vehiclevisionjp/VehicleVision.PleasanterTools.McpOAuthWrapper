using Microsoft.EntityFrameworkCore;

namespace VehicleVision.PleasanterTools.McpOAuthWrapper.Bridge;

// Pleasanter DB と分離し、API キーもパスワードも保存しない。
public sealed class OAuthState(DbContextOptions<OAuthState> options) : DbContext(options)
{
    public DbSet<LoginAttempt> LoginAttempts => Set<LoginAttempt>();
    public DbSet<PendingConsent> PendingConsents => Set<PendingConsent>();
}

public sealed class PendingConsent
{
    public string Id { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
}

public sealed class LoginAttempt
{
    public string Id { get; set; } = "";
    public int Count { get; set; }
    public DateTime WindowStart { get; set; }
}
