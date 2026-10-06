using System.Security.Cryptography.X509Certificates;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace VehicleVision.PleasanterTools.McpOAuthWrapper.Bridge;

public static class BridgeSetup
{
    public static void AddBridge(this WebApplicationBuilder builder, BridgeOptions options)
    {
        var root = builder.Environment.ContentRootPath;
        var rdsConfig = new ConfigurationBuilder().SetBasePath(root)
            .AddJsonFile("App_Data/Parameters/Rds.json", optional: false)
            .AddEnvironmentVariables("MCP_RDS_").Build();
        var rds = rdsConfig.Get<RdsOptions>() ?? throw new InvalidOperationException("Rds.json が必要です。");
        var auth = new ConfigurationBuilder().SetBasePath(root)
            .AddJsonFile("App_Data/Parameters/Authentication.json", optional: false).Build();
        var security = new ConfigurationBuilder().SetBasePath(root)
            .AddJsonFile("App_Data/Parameters/Security.json", optional: false).Build();
        if (!string.IsNullOrEmpty(auth["Provider"]) && auth["Provider"] != "Local")
            throw new InvalidOperationException("初期版の DB ログインは Pleasanter のローカル認証に限定します。");
        if (auth.GetValue<bool>("PasskeyParameters:Enabled") || security["SecondaryAuthentication:Mode"] is not ("None" or "0"))
            throw new InvalidOperationException("二段階認証・パスキーの迂回を防ぐため、この構成では DB ログインを提供しません。");
        if (rds.Provider != "Local" || string.IsNullOrWhiteSpace(rds.UserConnectionString)
            || rds.UserConnectionString.Contains('#') || rds.SqlCommandTimeOut is < 1 or > 120)
            throw new InvalidOperationException("Rds.json に読み取り専用 UserConnectionString と 1～120 秒のタイムアウトを設定してください。");
        if (rds.Dbms.ToLowerInvariant() is not ("postgresql" or "sqlserver" or "mysql"))
            throw new InvalidOperationException("Dbms は PostgreSQL、SQLServer、MySQL のいずれかです。");
        if (options.TenantId <= 0 || options.SharedApiKeyUserId is <= 0)
            throw new InvalidOperationException("TenantId と共通キーの所有者 ID を確認してください。");
        _ = TimeZoneInfo.FindSystemTimeZoneById(options.DatabaseTimeZoneId);
        var allowHttp = options.AllowDevelopmentHttp && builder.Environment.IsDevelopment();
        if (options.AllowDevelopmentHttp && !builder.Environment.IsDevelopment())
            throw new InvalidOperationException("HTTP の許可は Development 環境専用です。");
        ValidateUrl(options.Issuer, allowHttp);
        if (new Uri(options.Issuer).AbsolutePath != "/")
            throw new InvalidOperationException("Issuer は専用ホストのルート URL を指定してください。");
        ValidateUrl(options.PleasanterUrl, allowHttp);
        foreach (var uri in options.AllowedRedirectUris.Concat(options.Clients.SelectMany(c => c.RedirectUris)))
            ValidateRedirect(uri, allowHttp);
        foreach (var origin in options.AllowedOrigins) ValidateUrl(origin.TrimEnd('/') + "/", allowHttp);
        builder.Services.Configure<ForwardedHeadersOptions>(forwarded =>
        {
            forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
            forwarded.ForwardLimit = 1;
            forwarded.KnownProxies.Clear();
            forwarded.KnownIPNetworks.Clear();
            foreach (var address in options.TrustedProxyAddresses)
                forwarded.KnownProxies.Add(System.Net.IPAddress.Parse(address));
            forwarded.AllowedHosts.Add(new Uri(options.Issuer).Host);
        });
        if (!options.Clients.Any() && !options.AllowDynamicClientRegistration)
            throw new InvalidOperationException("OAuth クライアントまたは許可済み redirect URI 付き動的登録を設定してください。");
        if (options.AllowDynamicClientRegistration && options.AllowedRedirectUris.Count == 0)
            throw new InvalidOperationException("動的登録には AllowedRedirectUris の明示的な許可一覧が必要です。");

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(rds);
        builder.Services.AddSingleton<IPleasanterConnectionFactory, PleasanterConnectionFactory>();
        builder.Services.AddScoped<IPleasanterUserStore, PleasanterUserStore>();
        var directory = Path.GetFullPath(options.StateDirectory, root);
        Directory.CreateDirectory(directory);
        builder.Services.AddDbContextFactory<OAuthState>(db =>
        {
            db.UseSqlite($"Data Source={Path.Combine(directory, "oauth.db")}");
            db.UseOpenIddict();
        });
        builder.Services.AddSingleton<LoginGuard>();
        builder.Services.AddAntiforgery(anti =>
        {
            anti.Cookie.Name = allowHttp ? "McpBridge.Antiforgery" : "__Host-McpBridge.Antiforgery";
            anti.Cookie.SecurePolicy = allowHttp ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            anti.Cookie.HttpOnly = true;
            anti.Cookie.SameSite = SameSiteMode.Strict;
        });
        var protection = builder.Services.AddDataProtection().SetApplicationName("Pleasanter.McpOAuthWrapper")
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(directory, "keys")));
        X509Certificate2? signing = null;
        X509Certificate2? encryption = null;
        if (!builder.Environment.IsDevelopment())
        {
            signing = LoadCertificate(options.SigningCertificatePath, options.CertificatePassword, root);
            encryption = LoadCertificate(options.EncryptionCertificatePath, options.CertificatePassword, root);
            protection.ProtectKeysWithCertificate(encryption);
        }
        builder.Services.AddOpenIddict()
            .AddCore(core => core.UseEntityFrameworkCore().UseDbContext<OAuthState>())
            .AddServer(server =>
            {
                server.SetIssuer(new Uri(options.Issuer));
                server.SetAuthorizationEndpointUris("connect/authorize");
                server.SetTokenEndpointUris("connect/token");
                server.SetRevocationEndpointUris("connect/revoke");
                server.AllowAuthorizationCodeFlow().AllowRefreshTokenFlow();
                server.RequireProofKeyForCodeExchange();
                server.RegisterScopes("mcp");
                server.RegisterResources(new Uri(options.Resource));
                server.Configure(configuration => configuration.CodeChallengeMethods.Remove(CodeChallengeMethods.Plain));
                if (options.AllowDynamicClientRegistration)
                    server.AddEventHandler<OpenIddict.Server.OpenIddictServerEvents.HandleConfigurationRequestContext>(handler =>
                        handler.UseInlineHandler(context =>
                        {
                            context.Metadata["registration_endpoint"] = new Uri(new Uri(options.Issuer), "connect/register").AbsoluteUri;
                            return ValueTask.CompletedTask;
                        }));
                server.SetAccessTokenLifetime(TimeSpan.FromMinutes(15));
                server.SetAuthorizationCodeLifetime(TimeSpan.FromMinutes(2));
                server.SetRefreshTokenLifetime(TimeSpan.FromDays(1));
                server.UseReferenceAccessTokens().UseReferenceRefreshTokens();
                if (signing is not null && encryption is not null)
                    server.AddSigningCertificate(signing).AddEncryptionCertificate(encryption);
                else
                    server.AddDevelopmentSigningCertificate().AddDevelopmentEncryptionCertificate();
                var asp = server.UseAspNetCore().EnableAuthorizationEndpointPassthrough().EnableTokenEndpointPassthrough();
                if (allowHttp) asp.DisableTransportSecurityRequirement();
            })
            .AddValidation(validation =>
            {
                validation.UseLocalServer();
                validation.AddAudiences(options.Resource);
                validation.EnableTokenEntryValidation();
                validation.UseAspNetCore();
            });
        builder.Services.AddAuthentication();
        builder.Services.AddAuthorization();
        builder.Services.AddControllersWithViews();
        builder.Services.AddHttpForwarder();
        builder.Services.AddSingleton<SessionBinding>();
        builder.Services.AddSingleton(new HttpMessageInvoker(new SocketsHttpHandler
        {
            AllowAutoRedirect = false, UseCookies = false, AutomaticDecompression = System.Net.DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(15)
        }));
        builder.Services.AddScoped<McpProxy>();
        builder.Services.AddRateLimiter(limits =>
        {
            limits.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limits.AddPolicy("oauth", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
                { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
    }

    public static async Task InitializeBridgeAsync(this WebApplication app, BridgeOptions options)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OAuthState>();
        await db.Database.EnsureCreatedAsync();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        foreach (var client in options.Clients)
        {
            var descriptor = DescribeClient(client.ClientId, client.DisplayName, client.RedirectUris, options.Resource);
            var existing = await manager.FindByClientIdAsync(client.ClientId);
            if (existing is null) await manager.CreateAsync(descriptor);
            else await manager.UpdateAsync(existing, descriptor);
        }
    }

    public static OpenIddictApplicationDescriptor DescribeClient(string id, string name, IEnumerable<string> redirects, string resource)
    {
        var result = new OpenIddictApplicationDescriptor
        { ClientId = id, DisplayName = name, ClientType = ClientTypes.Public, ConsentType = ConsentTypes.Explicit };
        foreach (var uri in redirects) result.RedirectUris.Add(new Uri(uri));
        result.Permissions.UnionWith([Permissions.Endpoints.Authorization, Permissions.Endpoints.Token,
            Permissions.Endpoints.Revocation, Permissions.GrantTypes.AuthorizationCode, Permissions.GrantTypes.RefreshToken,
            Permissions.ResponseTypes.Code, Permissions.Prefixes.Scope + "mcp", Permissions.Prefixes.Resource + resource]);
        result.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);
        return result;
    }

    private static X509Certificate2 LoadCertificate(string path, string password, string root)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("本番では署名・暗号化証明書を設定してください。");
        var certificate = X509CertificateLoader.LoadPkcs12FromFile(Path.GetFullPath(path, root), password);
        if (!certificate.HasPrivateKey || certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow
            || certificate.NotBefore.ToUniversalTime() > DateTime.UtcNow)
            throw new InvalidOperationException("証明書の秘密鍵と有効期限を確認してください。");
        return certificate;
    }

    private static void ValidateUrl(string value, bool allowHttp)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)
            || (uri.Scheme != "https" && !(allowHttp && uri.Scheme == "http" && uri.IsLoopback))
            || !value.EndsWith('/'))
            throw new InvalidOperationException("接続 URL は末尾 / の HTTPS URL が必要です。HTTP はローカル開発専用です。");
    }

    private static void ValidateRedirect(string value, bool allowHttp)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment)
            || (uri.Scheme != "https" && !(allowHttp && uri.Scheme == "http" && uri.IsLoopback)))
            throw new InvalidOperationException("redirect URI は HTTPS の完全一致で登録してください。");
    }
}
