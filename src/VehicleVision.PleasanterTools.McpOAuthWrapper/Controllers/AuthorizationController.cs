using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using VehicleVision.PleasanterTools.McpOAuthWrapper.Bridge;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace VehicleVision.PleasanterTools.McpOAuthWrapper.Controllers;

public sealed record ConsentView(string ClientName, string RedirectUri, string ActionUrl,
    string PleasanterUrl, bool AllowSharedKey, string RequestTicket, string? Error = null,
    string? Ticket = null, string? LoginId = null, string? KeyDescription = null);

[EnableRateLimiting("oauth")]
[RequestSizeLimit(16384)]
public sealed class AuthorizationController(BridgeOptions options, IPleasanterUserStore users,
    IOpenIddictApplicationManager applications, LoginGuard guard, OAuthState db, IDataProtectionProvider protection)
    : Controller
{
    private ITimeLimitedDataProtector Protector<T>() => protection.CreateProtector("OAuth.Consent.v1", typeof(T).Name).ToTimeLimitedDataProtector();
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
            var consumed = await db.PendingConsents.Where(x => x.Id == identity.Nonce && x.ExpiresAt > DateTime.UtcNow)
                .ExecuteDeleteAsync(cancellationToken);
            if (consumed != 1) return Reject(Errors.AccessDenied);
            var user = await users.FindByIdAsync(identity.TenantId, identity.UserId, cancellationToken);
            if (user is null || !user.CanSignIn(options.DatabaseNow) || McpProxy.Stamp(user.PasswordHash) != identity.PasswordStamp
                || !await HasKeyAsync(user, identity.KeyOwnerId, cancellationToken)) return Reject(Errors.AccessDenied);
            var claims = new ClaimsIdentity(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            claims.AddClaim(new Claim(Claims.Subject, $"{user.TenantId}:{user.UserId}"));
            claims.AddClaim(new Claim("tenant_id", user.TenantId.ToString()).SetDestinations(Destinations.AccessToken));
            claims.AddClaim(new Claim("user_id", user.UserId.ToString()).SetDestinations(Destinations.AccessToken));
            claims.AddClaim(new Claim("key_owner_id", identity.KeyOwnerId.ToString()).SetDestinations(Destinations.AccessToken));
            claims.AddClaim(new Claim("password_stamp", identity.PasswordStamp).SetDestinations(Destinations.AccessToken));
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
        if (decision != "login" || string.IsNullOrWhiteSpace(loginId) || loginId.Length > 256
            || string.IsNullOrEmpty(password) || password.Length > 1024
            || !await guard.TryAttemptAsync(options.TenantId, loginId, cancellationToken))
            return View("Consent", ViewFor(info, "ログインできません。入力内容を確認し、時間を置いて再試行してください。"));
        var authenticated = await users.FindByLoginAsync(options.TenantId, loginId, cancellationToken);
        var passwordMatches = authenticated?.VerifyPassword(password)
            ?? new PleasanterUser(0, 0, "", new string('0', 128), "", true, true, null, null, 0, null, false).VerifyPassword(password);
        if (!passwordMatches || authenticated is null || !authenticated.CanSignIn(options.DatabaseNow))
            return View("Consent", ViewFor(info, "ログインできません。入力内容を確認し、時間を置いて再試行してください。"));
        await guard.ResetAsync(options.TenantId, loginId, cancellationToken);
        var ownerId = keyMode == "shared" ? options.SharedApiKeyUserId ?? 0 : authenticated.UserId;
        if (!await HasKeyAsync(authenticated, ownerId, cancellationToken))
            return View("Consent", ViewFor(info,
                "選択したアカウントの API キーがありません。Pleasanter 本体で先に発行し、もう一度ログインしてください。"));
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await db.PendingConsents.Where(x => x.ExpiresAt < DateTime.UtcNow).ExecuteDeleteAsync(cancellationToken);
        db.Add(new PendingConsent { Id = nonce, ExpiresAt = DateTime.UtcNow.AddMinutes(5) });
        await db.SaveChangesAsync(cancellationToken);
        var approved = new ApprovedIdentity(authenticated.TenantId, authenticated.UserId, ownerId,
            McpProxy.Stamp(authenticated.PasswordHash), Hash(new Uri("http://localhost" + info.AuthorizationUrl).Query), nonce);
        return View("Consent", ViewFor(info) with
        {
            ActionUrl = "/account/consent", Ticket = Protect(approved), LoginId = authenticated.LoginId,
            KeyDescription = ownerId == authenticated.UserId ? "自分の Pleasanter アカウント" : "管理者が指定した共通 Pleasanter アカウント"
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
            await db.PendingConsents.Where(x => x.Id == identity.Nonce).ExecuteDeleteAsync(cancellationToken);
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
        if (user is null || !user.CanSignIn(options.DatabaseNow) || McpProxy.Stamp(user.PasswordHash) != principal.FindFirstValue("password_stamp")
            || !await HasKeyAsync(user, ownerId, cancellationToken)) return Reject(Errors.InvalidGrant);
        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private bool ValidRequest(OpenIddictRequest request) => request.CodeChallengeMethod == CodeChallengeMethods.Sha256
        && request.GetScopes().Contains("mcp") && request.GetScopes().All(s => s is "mcp" or Scopes.OfflineAccess)
        && request.GetResources().All(resource => resource == options.Resource);
    private async Task<bool> HasKeyAsync(PleasanterUser user, int ownerId, CancellationToken cancellationToken)
    {
        if (ownerId != user.UserId && ownerId != options.SharedApiKeyUserId) return false;
        var owner = ownerId == user.UserId ? user : await users.FindByIdAsync(user.TenantId, ownerId, cancellationToken);
        return owner is not null && !owner.Disabled && !owner.Lockout && !string.IsNullOrWhiteSpace(owner.ApiKey);
    }
    private ConsentView ViewFor(AuthorizationInfo info, string? error = null) => new(info.ClientName,
        info.RedirectUri, "/account/login", options.PleasanterUrl, options.SharedApiKeyUserId.HasValue, Protect(info), error);
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
    private sealed record ApprovedIdentity(int TenantId, int UserId, int KeyOwnerId, string PasswordStamp, string RequestHash, string Nonce);
    private sealed record AuthorizationInfo(string AuthorizationUrl, string RedirectUri, string ClientName);
}
