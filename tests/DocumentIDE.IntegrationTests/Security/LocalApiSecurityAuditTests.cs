using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DocumentIDE.IntegrationTests.Security;

public class LocalApiSecurityAuditTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public LocalApiSecurityAuditTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Allowed_Origin_Receives_Correct_CORS_Header()
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        var allowedOrigin = "http://localhost:5173";
        request.Headers.Add("Origin", allowedOrigin);

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("Access-Control-Allow-Origin"));
        var headerValue = response.Headers.GetValues("Access-Control-Allow-Origin");
        Assert.Contains(allowedOrigin, headerValue);
        // Ensure no wildcard
        Assert.DoesNotContain("*", headerValue);
    }

    [Fact]
    public async Task Disallowed_Origin_Does_Not_Get_CORS_Header()
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        var badOrigin = "http://evil.com";
        request.Headers.Add("Origin", badOrigin);

        var response = await client.SendAsync(request);
        // Expect forbidden due to origin validation
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Preflight_Options_Returns_NoContent()
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Options, "/");
        request.Headers.Add("Origin", "http://localhost:5173");
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "X-DocumentIDE-Session-Token");

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
