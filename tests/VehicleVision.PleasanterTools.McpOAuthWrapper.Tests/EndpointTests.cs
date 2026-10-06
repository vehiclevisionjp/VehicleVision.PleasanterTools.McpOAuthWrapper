using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;

namespace VehicleVision.PleasanterTools.McpOAuthWrapper.Tests;

public sealed class EndpointTests : IClassFixture<DisabledBridgeFactory>
{
    private readonly WebApplicationFactory<Program> _factory;

    public EndpointTests(DisabledBridgeFactory factory) => _factory = factory;

    [Fact]
    public async Task ヘルスチェックは認証設定なしで起動確認できる()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("DELETE")]
    public async Task 未実装のMCPはAPIキーを受けても成功を返さない(string method)
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), "/mcp");
        const string apiKey = "test-only-secret";
        request.Headers.Add("X-API-Key", apiKey);
        request.Headers.Authorization = new("Bearer", apiKey);
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(body);
        Assert.Equal(501, problem.RootElement.GetProperty("status").GetInt32());
        Assert.DoesNotContain(apiKey, body);
    }
}

// 実機検証用のローカル設定があっても、未設定時の挙動を独立して確認する。
public sealed class DisabledBridgeFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var root = Path.Combine(Path.GetTempPath(), "mcp-disabled-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        builder.UseContentRoot(root);
    }
}
