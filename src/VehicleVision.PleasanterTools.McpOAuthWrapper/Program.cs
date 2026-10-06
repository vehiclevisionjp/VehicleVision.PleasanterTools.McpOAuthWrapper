using OpenIddict.Validation.AspNetCore;
using VehicleVision.PleasanterTools.McpOAuthWrapper;
using VehicleVision.PleasanterTools.McpOAuthWrapper.Bridge;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.Configure<Microsoft.AspNetCore.Builder.RequestLocalizationOptions>(options =>
{
    string[] languages = ["ja", "en", "zh", "de", "ko", "es", "vi"];
    options.SetDefaultCulture("ja").AddSupportedCultures(languages).AddSupportedUICultures(languages);
});
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
var general = new ConfigurationBuilder().SetBasePath(builder.Environment.ContentRootPath)
    .AddJsonFile("App_Data/Parameters/General.json", optional: true)
    .AddEnvironmentVariables("MCP_GENERAL_").Build();
var bridge = general.Get<BridgeOptions>() ?? new();
if (bridge.Enabled) builder.AddBridge(bridge);

var app = builder.Build();
app.UseRequestLocalization();

app.UseExceptionHandler();
app.MapHealthChecks("/health/live");

// 接続を設定するまでは、MCP の操作が成功したように見せない。
if (!bridge.Enabled)
{
    app.MapMethods("/mcp", ["GET", "POST", "DELETE"], (Microsoft.Extensions.Localization.IStringLocalizer<UiText> text) => Results.Problem(
    statusCode: StatusCodes.Status501NotImplemented,
    title: text["NotConfigured"].Value,
        detail: text["ConfigureHelp"].Value));
}
else
{
    await app.InitializeBridgeAsync(bridge);
    if (bridge.TrustedProxyAddresses.Count > 0) app.UseForwardedHeaders();
    app.Use(async (context, next) =>
    {
        // 外部から与えられた Host で OAuth の URL を構成しない。
        var issuer = new Uri(bridge.Issuer);
        if (context.Request.Path == "/mcp" && context.Request.Query.ContainsKey("access_token"))
        { context.Response.StatusCode = 400; return; }
        if (context.Request.Path == "/mcp" && context.Request.Headers.TryGetValue("Origin", out var origin)
            && (origin.Count != 1 || (origin.ToString() != issuer.GetLeftPart(UriPartial.Authority)
                && !bridge.AllowedOrigins.Contains(origin.ToString(), StringComparer.Ordinal))))
        { context.Response.StatusCode = 403; return; }
        if (context.Request.Path != "/health/live" &&
            (!string.Equals(context.Request.Host.Value, issuer.Authority, StringComparison.OrdinalIgnoreCase)
             || context.Request.Scheme != issuer.Scheme))
        { context.Response.StatusCode = 400; return; }
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; form-action 'self'; frame-ancestors 'none'; base-uri 'none'";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.OnStarting(() =>
        {
            if (context.Response.StatusCode == 401 && context.Request.Path == "/mcp")
                context.Response.Headers.WWWAuthenticate = $"Bearer resource_metadata=\"{new Uri(new Uri(bridge.Issuer), ".well-known/oauth-protected-resource")}\", scope=\"mcp\"";
            return Task.CompletedTask;
        });
        await next();
    });
    app.UseRouting();
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();
    app.MapGet("/.well-known/oauth-protected-resource", () => Results.Json(new
    {
        resource = bridge.Resource, authorization_servers = new[] { bridge.Issuer },
        scopes_supported = new[] { "mcp" }, bearer_methods_supported = new[] { "header" }
    }));
    app.MapGet("/.well-known/oauth-protected-resource/mcp", () => Results.Redirect("/.well-known/oauth-protected-resource"));
    app.MapMethods("/mcp", ["GET", "POST", "DELETE"], async (HttpContext context, McpProxy proxy) =>
    {
        await proxy.ForwardAsync(context);
    }).RequireAuthorization(new Microsoft.AspNetCore.Authorization.AuthorizeAttribute
    { AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme });
}

app.Run();

// WebApplicationFactory から起動して HTTP 経由で検証するために公開する。
public partial class Program;
