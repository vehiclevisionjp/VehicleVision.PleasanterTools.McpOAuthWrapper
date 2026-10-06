using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VehicleVision.PleasanterTools.McpOAuthWrapper.Bridge;

namespace VehicleVision.PleasanterTools.McpOAuthWrapper.Tests;

public sealed class BridgeFlowTests
{
    [Fact]
    public async Task SSEは上流の完了を待たず最初のイベントを届ける()
    {
        using var factory = new BridgeFactory();
        using var browser = factory.Browser();
        var tokens = await AuthorizeAsync(browser);
        var pipe = new System.IO.Pipelines.Pipe();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        factory.Upstream.ContentOverride = new StreamContent(pipe.Reader.AsStream());
        factory.Upstream.ContentOverride.Headers.ContentType = new("text/event-stream");
        var producer = Task.Run(async () =>
        {
            await pipe.Writer.WriteAsync(Encoding.UTF8.GetBytes("data: first\n\n"));
            await release.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await pipe.Writer.WriteAsync(Encoding.UTF8.GetBytes("data: second\n\n"));
            await pipe.Writer.CompleteAsync();
        });
        try
        {
            using var request = McpRequest(tokens.AccessToken);
            using var response = await browser.SendAsync(request, HttpCompletionOption.ResponseHeadersRead)
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
            using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());
            Assert.Equal("data: first", await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.False(producer.IsCompleted);
            release.TrySetResult();
            Assert.Contains("data: second", await reader.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally { release.TrySetResult(); await producer; }
    }

    [Fact]
    public async Task OAuth認可から最新APIキーでMCPを中継しキーを保存しない()
    {
        using var factory = new BridgeFactory();
        using var browser = factory.Browser();
        var tokens = await AuthorizeAsync(browser);
        Assert.DoesNotContain("original-key", tokens.AccessToken);
        using var request = McpRequest(tokens.AccessToken);
        request.Headers.Add("X-API-Key", "attacker-key");
        request.Headers.Add("Cookie", "should-not-forward=1");
        var response = await browser.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("original-key", factory.Upstream.Keys.Last());
        Assert.False(factory.Upstream.ReceivedAuthorization);
        Assert.False(factory.Upstream.ReceivedCookie);
        Assert.DoesNotContain("original-key", await response.Content.ReadAsStringAsync());
        factory.ChangeUser("ApiKey", "rotated-key");
        using var next = McpRequest(tokens.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await browser.SendAsync(next)).StatusCode);
        Assert.Equal("rotated-key", factory.Upstream.Keys.Last());
        using var db = new SqliteConnection($"Data Source={Path.Combine(factory.Root, "state", "oauth.db")}");
        await db.OpenAsync();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT Payload FROM OpenIddictTokens";
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (!reader.IsDBNull(0))
            {
                Assert.DoesNotContain("original-key", reader.GetString(0));
                Assert.DoesNotContain("rotated-key", reader.GetString(0));
            }
        }
    }

    [Theory]
    [InlineData("ApiKey", "")]
    [InlineData("Disabled", "1")]
    [InlineData("Lockout", "1")]
    [InlineData("Password", "changed")]
    [InlineData("PasswordExpirationTime", "2000-01-01 00:00:00")]
    [InlineData("LoginExpirationLimit", "2000-01-01 00:00:00")]
    [InlineData("EnableSecretKey", "0")]
    public async Task キー削除と利用者無効化とパスワード変更は次の通信に反映される(string column, string value)
    {
        using var factory = new BridgeFactory();
        using var browser = factory.Browser();
        var tokens = await AuthorizeAsync(browser);
        factory.ChangeUser(column, value);
        using var request = McpRequest(tokens.AccessToken);
        var response = await browser.SendAsync(request);
        Assert.True(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
        Assert.Empty(factory.Upstream.Keys);
    }

    [Fact]
    public async Task 空のAPIキーは本体での発行を案内し認可コードを出さない()
    {
        using var factory = new BridgeFactory();
        factory.ChangeUser("ApiKey", "");
        using var browser = factory.Browser();
        var url = AuthorizationUrl();
        var page = await PageAsync(browser, url);
        var response = await PostAsync(browser, url, page, new() { ["decision"] = "login", ["loginId"] = "alice", ["password"] = "correct-password" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("API キー", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        Assert.Null(response.Headers.Location);
    }

    [Theory]
    [InlineData("wrong-password")]
    [InlineData("' OR 1=1 --")]
    public async Task 誤ったパスワードとSQLインジェクションは認証できない(string password)
    {
        using var factory = new BridgeFactory();
        using var browser = factory.Browser();
        var url = AuthorizationUrl();
        var page = await browser.GetStringAsync(url);
        var response = await PostAsync(browser, url, page, new() { ["decision"] = "login", ["loginId"] = "alice", ["password"] = password });
        var html = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("name=\"ticket\"", html);
        Assert.Contains("ログインできません", WebUtility.HtmlDecode(html));
    }

    [Fact]
    public async Task 認可フォームのCSRFと認可チケットの再利用を拒否する()
    {
        using var factory = new BridgeFactory();
        using var browser = factory.Browser();
        var url = AuthorizationUrl();
        var noCsrf = await browser.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["decision"] = "login", ["loginId"] = "alice", ["password"] = "correct-password" }));
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        var login = await browser.GetStringAsync(url);
        var consentResponse = await PostAsync(browser, url, login, new() { ["decision"] = "login", ["loginId"] = "alice", ["password"] = "correct-password" });
        var consent = await consentResponse.Content.ReadAsStringAsync();
        var form = new Dictionary<string, string> { ["decision"] = "approve", ["ticket"] = Hidden(consent, "ticket") };
        var approval = await PostAsync(browser, url, consent, form);
        var approved = await browser.GetAsync(approval.Headers.Location);
        Assert.Contains("code=", approved.Headers.Location!.Query);
        var replay = await PostAsync(browser, url, consent, form);
        var replayed = await browser.GetAsync(replay.Headers.Location);
        Assert.DoesNotContain("code=", replayed.Headers.Location?.Query ?? "");
    }

    [Fact]
    public async Task PKCEの誤りと認可コードの再利用を拒否する()
    {
        using var factory = new BridgeFactory();
        using var browser = factory.Browser();
        var code = await GetCodeAsync(browser);
        var bad = await ExchangeAsync(browser, code, new string('b', 64));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var newCode = await GetCodeAsync(browser);
        Assert.Equal(HttpStatusCode.OK, (await ExchangeAsync(browser, newCode, Verifier)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ExchangeAsync(browser, newCode, Verifier)).StatusCode);
    }

    [Fact]
    public async Task Discoveryと未認証401と許可していないリダイレクトを確認する()
    {
        using var factory = new BridgeFactory();
        using var browser = factory.Browser();
        var response = await browser.PostAsync("/mcp", new StringContent("{}"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("oauth-protected-resource", response.Headers.WwwAuthenticate.ToString());
        var metadata = await browser.GetFromJsonAsync<JsonElement>("/.well-known/oauth-protected-resource");
        Assert.Equal("http://localhost/mcp", metadata.GetProperty("resource").GetString());
        var discovery = await browser.GetFromJsonAsync<JsonElement>("/.well-known/openid-configuration");
        Assert.Contains("connect/token", discovery.GetProperty("token_endpoint").GetString());
        var invalid = await browser.GetAsync(AuthorizationUrl().Replace(Uri.EscapeDataString(Callback), Uri.EscapeDataString("https://evil.example/callback")));
        Assert.Null(invalid.Headers.Location);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task 別OriginとURL内のトークンとログインIDへのSQL注入を拒否する()
    {
        using var factory = new BridgeFactory();
        using var browser = factory.Browser();
        var tokens = await AuthorizeAsync(browser);
        using var origin = McpRequest(tokens.AccessToken);
        origin.Headers.Add("Origin", "https://evil.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.SendAsync(origin)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.GetAsync("/mcp?access_token=" + tokens.AccessToken)).StatusCode);
        var url = AuthorizationUrl();
        var page = await PageAsync(browser, url);
        var injected = await PostAsync(browser, url, page, new() { ["decision"] = "login",
            ["loginId"] = "alice' OR 1=1 --", ["password"] = "correct-password" });
        Assert.DoesNotContain("name=\"ticket\"", await injected.Content.ReadAsStringAsync());
        Assert.Empty(factory.Upstream.Keys);
    }

    [Fact]
    public async Task 共通キーを選んでもトークンはログインした本人に結び付く()
    {
        using var factory = new BridgeFactory(shared: true);
        using var browser = factory.Browser();
        var tokens = await AuthorizeAsync(browser, shared: true);
        using var request = McpRequest(tokens.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await browser.SendAsync(request)).StatusCode);
        Assert.Equal("shared-key", factory.Upstream.Keys.Last());
        factory.ChangeUser("Disabled", "1");
        using var disabled = McpRequest(tokens.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.SendAsync(disabled)).StatusCode);
    }

    [Fact]
    public async Task セッションIDは他の利用者のトークンで再利用できない()
    {
        using var factory = new BridgeFactory();
        using var browser = factory.Browser();
        var alice = await AuthorizeAsync(browser);
        using var first = McpRequest(alice.AccessToken);
        var response = await browser.SendAsync(first);
        var session = response.Headers.GetValues("Mcp-Session-Id").Single();
        Assert.NotEqual("upstream-session", session);
        using var sameUser = McpRequest(alice.AccessToken);
        sameUser.Headers.Add("Mcp-Session-Id", session);
        Assert.Equal(HttpStatusCode.OK, (await browser.SendAsync(sameUser)).StatusCode);
        Assert.Equal("upstream-session", factory.Upstream.LastSession);
        var bob = await AuthorizeAsync(browser, loginId: "bob");
        using var other = McpRequest(bob.AccessToken);
        other.Headers.Add("Mcp-Session-Id", session);
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.SendAsync(other)).StatusCode);
    }

    [Fact]
    public async Task リフレッシュとトークン失効が動作する()
    {
        using var factory = new BridgeFactory();
        using var browser = factory.Browser();
        var tokens = await AuthorizeAsync(browser);
        var refresh = await browser.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["grant_type"] = "refresh_token", ["client_id"] = "test-client", ["refresh_token"] = tokens.RefreshToken }));
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var revocation = await browser.PostAsync("/connect/revoke", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["client_id"] = "test-client", ["token"] = tokens.AccessToken, ["token_type_hint"] = "access_token" }));
        Assert.Equal(HttpStatusCode.OK, revocation.StatusCode);
        using var request = McpRequest(tokens.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task アカウントの失敗回数制限はIPによらず動作する()
    {
        using var factory = new BridgeFactory();
        using var browser = factory.Browser();
        var url = AuthorizationUrl();
        var login = await browser.GetStringAsync(url);
        for (var i = 0; i < 5; i++)
            await PostAsync(browser, url, login, new() { ["decision"] = "login", ["loginId"] = "alice", ["password"] = "wrong" });
        var correct = await PostAsync(browser, url, login, new() { ["decision"] = "login", ["loginId"] = "alice", ["password"] = "correct-password" });
        Assert.DoesNotContain("name=\"ticket\"", await correct.Content.ReadAsStringAsync());
    }

    private const string Callback = "https://client.example/callback";
    private static readonly string Verifier = new('a', 64);
    private static string AuthorizationUrl() => "/connect/authorize?client_id=test-client&response_type=code&scope=mcp%20offline_access"
        + $"&redirect_uri={Uri.EscapeDataString(Callback)}&state=test-state&resource=http%3A%2F%2Flocalhost%2Fmcp&code_challenge_method=S256&code_challenge="
        + Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(Verifier)));
    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static string Hidden(string html, string name) => WebUtility.HtmlDecode(
        Regex.Match(html, $"name=\"{name}\"[^>]*value=\"([^\"]*)\"").Groups[1].Value);
    private static async Task<string> PageAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        return body;
    }
    private static Task<HttpResponseMessage> PostAsync(HttpClient browser, string url, string html, Dictionary<string, string> form)
    {
        form["__RequestVerificationToken"] = Hidden(html, "__RequestVerificationToken");
        form["requestTicket"] = Hidden(html, "requestTicket");
        var action = WebUtility.HtmlDecode(Regex.Match(html, "<form[^>]*action=\"([^\"]+)\"").Groups[1].Value);
        return browser.PostAsync(action.Length > 0 ? action : url, new FormUrlEncodedContent(form));
    }
    private static async Task<string> GetCodeAsync(HttpClient browser, bool shared = false, string loginId = "alice")
    {
        var url = AuthorizationUrl();
        var page = await PageAsync(browser, url);
        var loggedIn = await PostAsync(browser, url, page, new() { ["decision"] = "login", ["loginId"] = loginId,
            ["password"] = "correct-password", ["keyMode"] = shared ? "shared" : "personal" });
        var consent = await loggedIn.Content.ReadAsStringAsync();
        var approval = await PostAsync(browser, url, consent, new() { ["decision"] = "approve", ["ticket"] = Hidden(consent, "ticket") });
        Assert.Equal(HttpStatusCode.Redirect, approval.StatusCode);
        approval = await browser.GetAsync(approval.Headers.Location);
        Assert.True(approval.StatusCode == HttpStatusCode.Redirect, await approval.Content.ReadAsStringAsync());
        var values = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(approval.Headers.Location!.Query);
        Assert.Equal("test-state", values["state"]);
        return values["code"].ToString();
    }
    private static Task<HttpResponseMessage> ExchangeAsync(HttpClient client, string code, string verifier) =>
        client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["grant_type"] = "authorization_code", ["client_id"] = "test-client", ["code"] = code,
            ["redirect_uri"] = Callback, ["code_verifier"] = verifier, ["resource"] = "http://localhost/mcp" }));
    private static async Task<Tokens> AuthorizeAsync(HttpClient browser, bool shared = false, string loginId = "alice")
    {
        var code = await GetCodeAsync(browser, shared, loginId);
        var response = await ExchangeAsync(browser, code, Verifier);
        var payload = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, payload);
        var json = JsonDocument.Parse(payload).RootElement;
        return new Tokens(json.GetProperty("access_token").GetString()!, json.GetProperty("refresh_token").GetString()!);
    }
    private static HttpRequestMessage McpRequest(string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = new StringContent("{\"jsonrpc\":\"2.0\",\"id\":1}", Encoding.UTF8, "application/json") };
        request.Headers.Authorization = new("Bearer", token);
        request.Headers.Accept.ParseAdd("application/json, text/event-stream");
        return request;
    }
    private sealed record Tokens(string AccessToken, string RefreshToken);

    private sealed class BridgeFactory : WebApplicationFactory<Program>
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "mcp-bridge-test-" + Guid.NewGuid().ToString("N"));
        public UpstreamHandler Upstream { get; } = new();
        private readonly bool _shared;
        public BridgeFactory(bool shared = false)
        {
            _shared = shared;
            Directory.CreateDirectory(Path.Combine(Root, "App_Data", "Parameters"));
            var parameters = Path.Combine(Root, "App_Data", "Parameters");
            File.WriteAllText(Path.Combine(parameters, "Rds.json"), "{\"Dbms\":\"PostgreSQL\",\"Provider\":\"Local\",\"UserConnectionString\":\"test-only\",\"SqlCommandTimeOut\":30}");
            File.WriteAllText(Path.Combine(parameters, "Authentication.json"), "{\"Provider\":\"Local\"}");
            File.WriteAllText(Path.Combine(parameters, "Security.json"), "{\"SecondaryAuthentication\":{\"Mode\":\"None\"}}");
            using var connection = UsersConnection();
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE Users (TenantId INTEGER, UserId INTEGER PRIMARY KEY, LoginId TEXT, Password TEXT, ApiKey TEXT,
                    Disabled INTEGER, Lockout INTEGER, PasswordExpirationTime TEXT NULL, LoginExpirationLimit TEXT NULL,
                    LoginExpirationPeriod INTEGER, LastLoginTime TEXT NULL, EnableSecretKey INTEGER);
                INSERT INTO Users VALUES (1, 1, 'alice', @hash, 'original-key', 0, 0, NULL, NULL, 0, NULL, 1);
                INSERT INTO Users VALUES (1, 2, 'shared', @hash, 'shared-key', 0, 0, NULL, NULL, 0, NULL, 1);
                INSERT INTO Users VALUES (1, 3, 'bob', @hash, 'bob-key', 0, 0, NULL, NULL, 0, NULL, 1);
                """;
            command.Parameters.AddWithValue("@hash", Convert.ToHexStringLower(SHA512.HashData(Encoding.UTF8.GetBytes("correct-password"))));
            command.ExecuteNonQuery();
        }
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development").UseContentRoot(Root);
            builder.UseSetting("Logging:LogLevel:Default", "Error");
            builder.UseSetting("Bridge:Enabled", "true");
            builder.UseSetting("Bridge:Issuer", "http://localhost/");
            builder.UseSetting("Bridge:PleasanterUrl", "http://localhost/pleasanter/");
            builder.UseSetting("Bridge:AllowDevelopmentHttp", "true");
            builder.UseSetting("Bridge:StateDirectory", Path.Combine(Root, "state"));
            builder.UseSetting("Bridge:SharedApiKeyUserId", _shared ? "2" : "");
            builder.UseSetting("Bridge:Clients:0:ClientId", "test-client");
            builder.UseSetting("Bridge:Clients:0:DisplayName", "Test Client");
            builder.UseSetting("Bridge:Clients:0:RedirectUris:0", Callback);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bridge:Enabled"] = "true", ["Bridge:Issuer"] = "http://localhost/", ["Bridge:PleasanterUrl"] = "http://localhost/pleasanter/",
                ["Bridge:AllowDevelopmentHttp"] = "true", ["Bridge:StateDirectory"] = Path.Combine(Root, "state"),
                ["Bridge:SharedApiKeyUserId"] = _shared ? "2" : null,
                ["Bridge:Clients:0:ClientId"] = "test-client", ["Bridge:Clients:0:DisplayName"] = "Test Client",
                ["Bridge:Clients:0:RedirectUris:0"] = Callback
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IPleasanterConnectionFactory>();
                services.AddSingleton<IPleasanterConnectionFactory>(new TestConnectionFactory(this));
                services.RemoveAll<HttpMessageInvoker>();
                services.AddSingleton(new HttpMessageInvoker(Upstream));
            });
        }
        public HttpClient Browser() => CreateClient(new WebApplicationFactoryClientOptions
            { BaseAddress = new Uri("http://localhost"), AllowAutoRedirect = false, HandleCookies = true });
        public SqliteConnection UsersConnection() => new($"Data Source={Path.Combine(Root, "users.db")}");
        public void ChangeUser(string column, string value)
        {
            using var connection = UsersConnection(); connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"UPDATE Users SET [{column}] = @value WHERE UserId = 1";
            command.Parameters.AddWithValue("@value", value); command.ExecuteNonQuery();
        }
    }
    private sealed class TestConnectionFactory(BridgeFactory factory) : IPleasanterConnectionFactory
    {
        public System.Data.Common.DbConnection Create() => factory.UsersConnection();
    }
    private sealed class UpstreamHandler : HttpMessageHandler
    {
        public HttpContent? ContentOverride { get; set; }
        public List<string> Keys { get; } = [];
        public bool ReceivedAuthorization { get; private set; }
        public bool ReceivedCookie { get; private set; }
        public string? LastSession { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("/pleasanter/mcp", request.RequestUri!.AbsolutePath);
            Keys.Add(request.Headers.GetValues("X-API-Key").Single());
            ReceivedAuthorization |= request.Headers.Contains("Authorization");
            ReceivedCookie |= request.Headers.Contains("Cookie");
            LastSession = request.Headers.TryGetValues("Mcp-Session-Id", out var ids) ? ids.Single() : null;
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = ContentOverride ?? new StringContent("{\"jsonrpc\":\"2.0\",\"result\":{}}", Encoding.UTF8, "application/json") };
            response.Headers.Add("Mcp-Session-Id", "upstream-session");
            return Task.FromResult(response);
        }
    }
}
