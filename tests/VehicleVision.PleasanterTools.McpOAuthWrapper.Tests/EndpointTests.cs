using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace VehicleVision.PleasanterTools.McpOAuthWrapper.Tests;

public sealed class EndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public EndpointTests(WebApplicationFactory<Program> factory) => _factory = factory;

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
