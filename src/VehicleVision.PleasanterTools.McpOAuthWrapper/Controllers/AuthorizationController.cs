using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using VehicleVision.PleasanterTools.McpOAuthWrapper.Bridge;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace VehicleVision.PleasanterTools.McpOAuthWrapper.Controllers;

public sealed record ConsentView(string ClientName, string RedirectUri, string ActionUrl,
    string PleasanterUrl, bool AllowSharedKey, string RequestTicket, string? Error = null,
    string? Ticket = null, string? LoginId = null, string? KeyDescription = null, string ApiKeyLoginId = "apikey");

[EnableRateLimiting("oauth")]
[RequestSizeLimit(16384)]
public sealed class AuthorizationController(BridgeOptions options, IPleasanterUserStore users,
    IOpenIddictApplicationManager applications, LoginGuard guard, IBridgeState state, IDataProtectionProvider protection,
    Microsoft.Extensions.Localization.IStringLocalizer<UiText> text)
    : Controller
{
    private ITimeLimitedDataProtector Protector<T>() => protection.CreateProtector("OAuth.Consent.v2", typeof(T).Name).ToTimeLimitedDataProtector();
    private string ApprovalCookie => Request.IsHttps ? "__Host-McpBridge.Approval" : "McpBridge.Approval";

    [HttpGet("/connect/authorize")]
    public async Task<IActionResult> Authorize(CancellationToken cancellationToken)
    {
        var request = HttpContext.GetOpenIddictServerRequest()!;
        if (!ValidRequest(request)) return Reject(Errors.InvalidRequest);
        if (Request.Cookies.TryGetValue(ApprovalCookie, out var approval))
        {
            Response.Cookies.Delete(ApprovalCookie, CookieOptions());
            var identity = Unprotect<ApprovedIdentity>(approval);
            if (identity is null || identity.RequestHash != Hash(Request.QueryString.Value ?? "") || identity.TenantId != options.TenantId)
                return Reject(Errors.AccessDenied);
            var consumed = await state.ConsumeConsentAsync(identity.Nonce, cancellationToken);
            if (!consumed) return Reject(Errors.AccessDenied);
            var user = await users.FindByIdAsync(identity.TenantId, identity.UserId, cancellationToken);
            if (user is null || !user.CanUseApi(options.DatabaseNow) || McpProxy.Stamp(user.ApiKey) != identity.ApiKeyStamp
                || !await HasKeyAsync(user, identity.KeyOwnerId, cancellationToken, identity.KeyOwnerStamp)) return Reject(Errors.AccessDenied);
            var claims = new ClaimsIdentity(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            claims.AddClaim(new Claim(Claims.Subject, $"{user.TenantId}:{user.UserId}"));
            claims.AddClaim(new Claim("tenant_id", user.TenantId.ToString()).SetDestinations(Destinations.AccessToken));
            claims.AddClaim(new Claim("user_id", user.UserId.ToString()).SetDestinations(Destinations.AccessToken));
            claims.AddClaim(new Claim("key_owner_id", identity.KeyOwnerId.ToString()).SetDestinations(Destinations.AccessToken));
            claims.AddClaim(new Claim("api_key_stamp", identity.ApiKeyStamp).SetDestinations(Destinations.AccessToken));
            claims.AddClaim(new Claim("key_owner_stamp", identity.KeyOwnerStamp).SetDestinations(Destinations.AccessToken));
            var principal = new ClaimsPrincipal(claims);
            principal.SetScopes(request.GetScopes());
            principal.SetResources(options.Resource);
            return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }
        var app = await applications.FindByClientIdAsync(request.ClientId!, cancellationToken);
        var name = app is null ? request.ClientId! : await applications.GetDisplayNameAsync(app, cancellationToken) ?? request.ClientId!;
        var info = new AuthorizationInfo(Request.Path + Request.QueryString, request.RedirectUri!, name);
        return View("Consent", ViewFor(info));
    }

    // パスワードは OAuth エンドポイントへ送らず、プロトコルのログに混入させない。
    [HttpPost("/account/login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login([FromForm] string? loginId, [FromForm] string? password,
        [FromForm] string? keyMode, [FromForm] string? requestTicket, [FromForm] string? decision,
        CancellationToken cancellationToken)
    {
        var info = Unprotect<AuthorizationInfo>(requestTicket);
        if (info is null) return BadRequest();
        if (decision == "deny") return Denied(info);
        var attemptId = "api-key-ip:" + (HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        if (decision != "login" || !string.Equals(loginId, options.ApiKeyLoginId, StringComparison.Ordinal)
            || string.IsNullOrEmpty(password) || password.Length > 1024
            || !await guard.TryAttemptAsync(options.TenantId, attemptId, cancellationToken))
            return View("Consent", ViewFor(info, text["LoginFailed"].Value));
        var authenticated = await users.FindByApiKeyAsync(options.TenantId, password, cancellationToken);
        if (authenticated is null || !authenticated.VerifyApiKey(password) || !authenticated.CanUseApi(options.DatabaseNow))
            return View("Consent", ViewFor(info, text["LoginFailed"].Value));
        await guard.ReleaseAsync(options.TenantId, attemptId, cancellationToken);
        var ownerId = keyMode == "shared" ? options.SharedApiKeyUserId ?? 0 : authenticated.UserId;
        var owner = ownerId == authenticated.UserId ? authenticated : ownerId == options.SharedApiKeyUserId
            ? await users.FindByIdAsync(authenticated.TenantId, ownerId, cancellationToken) : null;
        if (owner is null || !owner.CanUseApi(options.DatabaseNow) || string.IsNullOrWhiteSpace(owner.ApiKey))
            return View("Consent", ViewFor(info,
                text["MissingKey"].Value));
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await state.CreateConsentAsync(nonce, cancellationToken);
        var approved = new ApprovedIdentity(authenticated.TenantId, authenticated.UserId, ownerId,
            McpProxy.Stamp(authenticated.ApiKey), McpProxy.Stamp(owner.ApiKey), Hash(new Uri("http://localhost" + info.AuthorizationUrl).Query), nonce);
        return View("Consent", ViewFor(info) with
        {
            ActionUrl = CultureUrl("/account/consent"), Ticket = Protect(approved), LoginId = authenticated.LoginId,
            KeyDescription = ownerId == authenticated.UserId ? text["PersonalPermission"].Value : text["SharedPermission"].Value
        });
    }

    [HttpPost("/account/consent")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Consent([FromForm] string? ticket, [FromForm] string? requestTicket, [FromForm] string? decision,
        CancellationToken cancellationToken)
    {
        var info = Unprotect<AuthorizationInfo>(requestTicket);
        var identity = Unprotect<ApprovedIdentity>(ticket);
        if (info is null || identity is null) return BadRequest();
        if (identity.RequestHash != Hash(new Uri("http://localhost" + info.AuthorizationUrl).Query)) return BadRequest();
        if (decision != "approve")
        {
            await state.ConsumeConsentAsync(identity.Nonce, cancellationToken);
            return Denied(info);
        }
        Response.Cookies.Append(ApprovalCookie, ticket!, CookieOptions());
        return LocalRedirect(info.AuthorizationUrl);
    }

    [HttpPost("/connect/token")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Exchange(CancellationToken cancellationToken)
    {
        var request = HttpContext.GetOpenIddictServerRequest()!;
        if (!request.IsAuthorizationCodeGrantType() && !request.IsRefreshTokenGrantType()) return Reject(Errors.UnsupportedGrantType);
        if (request.GetResources().Any(resource => resource != options.Resource)) return Reject(Errors.InvalidTarget);
        var result = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        if (result.Principal is not { } principal || !McpProxy.TryIdentity(principal, out var tenant, out var userId, out var ownerId)
            || tenant != options.TenantId) return Reject(Errors.InvalidGrant);
        var user = await users.FindByIdAsync(tenant, userId, cancellationToken);
        if (user is null || !user.CanUseApi(options.DatabaseNow) || McpProxy.Stamp(user.ApiKey) != principal.FindFirstValue("api_key_stamp")
            || !await HasKeyAsync(user, ownerId, cancellationToken, principal.FindFirstValue("key_owner_stamp") ?? "")) return Reject(Errors.InvalidGrant);
        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private bool ValidRequest(OpenIddictRequest request) => request.CodeChallengeMethod == CodeChallengeMethods.Sha256
        && request.GetScopes().Contains("mcp") && request.GetScopes().All(s => s is "mcp" or Scopes.OfflineAccess)
        && request.GetResources().All(resource => resource == options.Resource);
    private async Task<bool> HasKeyAsync(PleasanterUser user, int ownerId, CancellationToken cancellationToken, string? expectedStamp = null)
    {
        if (ownerId != user.UserId && ownerId != options.SharedApiKeyUserId) return false;
        var owner = ownerId == user.UserId ? user : await users.FindByIdAsync(user.TenantId, ownerId, cancellationToken);
        return owner is not null && owner.CanUseApi(options.DatabaseNow) && !string.IsNullOrWhiteSpace(owner.ApiKey)
            && (expectedStamp is null || McpProxy.Stamp(owner.ApiKey) == expectedStamp);
    }
    private ConsentView ViewFor(AuthorizationInfo info, string? error = null)
    {
        // ブラウザーはフォームのリダイレクト先にも form-action を適用する。
        // OpenIddict が登録と完全一致を検証済みの戻り先だけを許可する。
        var callback = new Uri(info.RedirectUri).GetLeftPart(UriPartial.Authority);
        Response.Headers["Content-Security-Policy"] = $"default-src 'none'; style-src 'self'; script-src 'self'; form-action 'self' {callback}; frame-ancestors 'none'; base-uri 'none'";
        return new(info.ClientName, info.RedirectUri, CultureUrl("/account/login"), options.PleasanterUrl,
            options.SharedApiKeyUserId.HasValue, Protect(info), error, ApiKeyLoginId: options.ApiKeyLoginId);
    }
    private static string CultureUrl(string path) => path + "?culture=" + Uri.EscapeDataString(System.Globalization.CultureInfo.CurrentUICulture.Name);
    private string Protect<T>(T data) => Protector<T>().Protect(JsonSerializer.Serialize(data), TimeSpan.FromMinutes(5));
    private T? Unprotect<T>(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 16384) return default;
        try { return JsonSerializer.Deserialize<T>(Protector<T>().Unprotect(value)); }
        catch (Exception ex) when (ex is CryptographicException or JsonException or ArgumentException) { return default; }
    }
    private CookieOptions CookieOptions() => new() { HttpOnly = true, Secure = Request.IsHttps, SameSite = SameSiteMode.Strict,
        Path = "/", MaxAge = TimeSpan.FromMinutes(2), IsEssential = true };
    private IActionResult Denied(AuthorizationInfo info)
    {
        var original = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri("http://localhost" + info.AuthorizationUrl).Query);
        return Redirect(Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(info.RedirectUri,
            new Dictionary<string, string?> { ["error"] = Errors.AccessDenied, ["state"] = original["state"].ToString() }));
    }
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private ForbidResult Reject(string error) => Forbid(new AuthenticationProperties(new Dictionary<string, string?>
    { [OpenIddictServerAspNetCoreConstants.Properties.Error] = error }), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    private sealed record ApprovedIdentity(int TenantId, int UserId, int KeyOwnerId, string ApiKeyStamp, string KeyOwnerStamp, string RequestHash, string Nonce);
    private sealed record AuthorizationInfo(string AuthorizationUrl, string RedirectUri, string ClientName);
}
