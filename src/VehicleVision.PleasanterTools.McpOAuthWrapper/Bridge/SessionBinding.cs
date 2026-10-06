using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace VehicleVision.PleasanterTools.McpOAuthWrapper.Bridge;

public sealed class SessionBinding(IDataProtectionProvider provider)
{
    private readonly ITimeLimitedDataProtector _protector = provider.CreateProtector("MCP.Session.v1").ToTimeLimitedDataProtector();
    public string Wrap(string session, string binding) => _protector.Protect(
        JsonSerializer.Serialize(new BoundSession(session, binding)), TimeSpan.FromDays(1));

    public string? Unwrap(string value, string binding)
    {
        if (value.Length > 8192) return null;
        try
        {
            var decoded = JsonSerializer.Deserialize<BoundSession>(_protector.Unprotect(value));
            return decoded?.Binding == binding ? decoded.Session : null;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException) { return null; }
    }

    private sealed record BoundSession(string Session, string Binding);
}
