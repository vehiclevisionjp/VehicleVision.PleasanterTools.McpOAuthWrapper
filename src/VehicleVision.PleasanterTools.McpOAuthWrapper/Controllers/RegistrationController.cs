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
        var id = Guid.NewGuid().ToString("N");
        await applications.CreateAsync(BridgeSetup.DescribeClient(id, request.ClientName, redirects, options.Resource), cancellationToken);
        return StatusCode(201, new { client_id = id, client_name = request.ClientName, redirect_uris = redirects,
            token_endpoint_auth_method = "none", grant_types = new[] { "authorization_code", "refresh_token" }, response_types = new[] { "code" } });
    }
}
