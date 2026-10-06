using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using OpenIddict.Abstractions;
using Yarp.ReverseProxy.Forwarder;

namespace VehicleVision.PleasanterTools.McpOAuthWrapper.Bridge;

public sealed class McpProxy(IPleasanterUserStore users, BridgeOptions options, IHttpForwarder forwarder,
    HttpMessageInvoker client, SessionBinding sessions, Microsoft.Extensions.Localization.IStringLocalizer<UiText> text)
{
    public async Task ForwardAsync(HttpContext context)
    {
        var cancellationToken = context.RequestAborted;
        if (!context.User.HasScope("mcp"))
        {
            context.Response.StatusCode = 403;
            context.Response.Headers.WWWAuthenticate = "Bearer error=\"insufficient_scope\", scope=\"mcp\"";
            return;
        }
        if (!TryIdentity(context.User, out var tenantId, out var userId, out var ownerId) || tenantId != options.TenantId)
        { context.Response.StatusCode = 401; return; }
        var user = await users.FindByIdAsync(tenantId, userId, cancellationToken);
        if (user is null || !user.CanUseApi(options.DatabaseNow)
            || Stamp(user.ApiKey) != context.User.FindFirstValue("api_key_stamp"))
        { context.Response.StatusCode = 401; return; }
        if (ownerId != userId && ownerId != options.SharedApiKeyUserId)
        { context.Response.StatusCode = 403; return; }
        // 毎回 DB の最新値を読む。OAuth DB、トークン、セッションに API キーを格納しない。
        var owner = ownerId == userId ? user : await users.FindByIdAsync(tenantId, ownerId, cancellationToken);
        if (owner is null || !owner.CanUseApi(options.DatabaseNow) || string.IsNullOrWhiteSpace(owner.ApiKey)
            || Stamp(owner.ApiKey) != context.User.FindFirstValue("key_owner_stamp"))
        {
            await Results.Problem(statusCode: 403, title: text["Unavailable"].Value,
                detail: text["UnavailableHelp"].Value).ExecuteAsync(context);
            return;
        }
        // MCP セッションを利用者・API キー所有者・OAuth クライアントに結び付ける。
        var binding = $"{tenantId}:{userId}:{ownerId}:{context.User.GetPresenters().SingleOrDefault()}";
        string? upstreamSession = null;
        if (context.Request.Headers.TryGetValue("Mcp-Session-Id", out var session))
        {
            upstreamSession = session.Count == 1 ? sessions.Unwrap(session.ToString(), binding) : null;
            if (upstreamSession is null) { context.Response.StatusCode = 403; return; }
        }
        var error = await forwarder.SendAsync(context, options.PleasanterUrl, client,
            new ForwarderRequestConfig { ActivityTimeout = TimeSpan.FromMinutes(10) },
            new KeyTransformer(owner.ApiKey, upstreamSession, binding, sessions));
        if (error != ForwarderError.None && !context.Response.HasStarted)
            context.Response.StatusCode = StatusCodes.Status502BadGateway;
    }

    public static string Stamp(string hash) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hash)));

    public static bool TryIdentity(ClaimsPrincipal principal, out int tenant, out int user, out int owner)
    {
        tenant = user = owner = 0;
        return int.TryParse(principal.FindFirstValue("tenant_id"), out tenant) && tenant > 0
            && int.TryParse(principal.FindFirstValue("user_id"), out user) && user > 0
            && int.TryParse(principal.FindFirstValue("key_owner_id"), out owner) && owner > 0;
    }

    private sealed class KeyTransformer(string key, string? session, string binding, SessionBinding sessions) : HttpTransformer
    {
        public override async ValueTask TransformRequestAsync(HttpContext context, HttpRequestMessage request,
            string destinationPrefix, CancellationToken cancellationToken)
        {
            await base.TransformRequestAsync(context, request, destinationPrefix, cancellationToken);
            foreach (var header in new[] { "Authorization", "Proxy-Authorization", "X-API-Key", "Cookie", "Mcp-Session-Id", "Forwarded",
                         "X-Forwarded-For", "X-Forwarded-Host", "X-Forwarded-Proto" }) request.Headers.Remove(header);
            request.Headers.Host = null;
            request.Headers.TryAddWithoutValidation("X-API-Key", key);
            if (session is not null) request.Headers.TryAddWithoutValidation("Mcp-Session-Id", session);
        }

        public override async ValueTask<bool> TransformResponseAsync(HttpContext context, HttpResponseMessage? response,
            CancellationToken cancellationToken)
        {
            var result = await base.TransformResponseAsync(context, response, cancellationToken);
            context.Response.Headers.Remove("Set-Cookie");
            context.Response.Headers.Remove("WWW-Authenticate");
            if (response?.Headers.TryGetValues("Mcp-Session-Id", out var ids) == true)
                context.Response.Headers["Mcp-Session-Id"] = sessions.Wrap(ids.Single(), binding);
            return result;
        }
    }
}
