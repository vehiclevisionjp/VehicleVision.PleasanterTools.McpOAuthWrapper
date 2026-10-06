using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OpenIddict.Abstractions;
using VehicleVision.PleasanterTools.McpOAuthWrapper.Bridge;

namespace VehicleVision.PleasanterTools.McpOAuthWrapper.Controllers;

public sealed record RegistrationRequest(
    [property: JsonPropertyName("client_name")] string? ClientName,
    [property: JsonPropertyName("redirect_uris")] string[]? RedirectUris,
    [property: JsonPropertyName("token_endpoint_auth_method")] string? AuthenticationMethod);

[ApiController]
[EnableRateLimiting("oauth")]
[RequestSizeLimit(16384)]
public sealed class RegistrationController(BridgeOptions options, IOpenIddictApplicationManager applications) : ControllerBase
{
    [HttpPost("/connect/register")]
    public async Task<IActionResult> Register(RegistrationRequest request, CancellationToken cancellationToken)
    {
        if (!options.AllowDynamicClientRegistration) return NotFound();
        if (request.RedirectUris is not { Length: > 0 and <= 5 } redirects
            || redirects.Distinct(StringComparer.Ordinal).Count() != redirects.Length
            || redirects.Any(uri => !options.AllowedRedirectUris.Contains(uri, StringComparer.Ordinal))
            || request.ClientName is not { Length: > 0 and <= 100 }
            || request.AuthenticationMethod is not (null or "none"))
            return BadRequest(new { error = "invalid_client_metadata" });
        // 同じ名前・redirect URI の登録は同じ client_id にして、繰り返しの登録でストアを肥大化させない。
        var id = "dcr-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            request.ClientName + "\n" + string.Join("\n", redirects.Order(StringComparer.Ordinal))))).ToLowerInvariant()[..32];
        if (await applications.FindByClientIdAsync(id, cancellationToken) is null)
        {
            if (await applications.CountAsync(cancellationToken) >= options.Clients.Count + options.MaxDynamicClients)
                return StatusCode(503, new { error = "temporarily_unavailable" });
            try { await applications.CreateAsync(BridgeSetup.DescribeClient(id, request.ClientName, redirects, options.Resource), cancellationToken); }
            // 同時登録で先に作られた場合は、そのクライアントを返す。
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                if (await applications.FindByClientIdAsync(id, cancellationToken) is null) throw;
            }
        }
        return StatusCode(201, new { client_id = id, client_name = request.ClientName, redirect_uris = redirects,
            token_endpoint_auth_method = "none", grant_types = new[] { "authorization_code", "refresh_token" }, response_types = new[] { "code" } });
    }
}
