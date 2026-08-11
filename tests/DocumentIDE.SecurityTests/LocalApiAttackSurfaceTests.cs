using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DocumentIDE.SecurityTests;

public class LocalApiAttackSurfaceTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public LocalApiAttackSurfaceTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Foreign_Origin_Is_Rejected()
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Origin", "http://malicious.com");
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Null_Origin_Is_Rejected()
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Origin", "null");
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DNS_Rebinding_Host_Mismatch_Is_Rejected()
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Host = "attacker.com";
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Missing_Or_Invalid_Session_Token_Is_Rejected()
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Origin", "http://localhost:5173");
        // No session token header
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Valid_Origin_Host_And_Token_Succeeds()
    {
        using var scope = _factory.Services.CreateScope();
        var tokenProvider = scope.ServiceProvider.GetRequiredService<DocumentIDE.Api.Security.ISessionTokenProvider>();
        var token = tokenProvider.SessionToken;

        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Origin", "http://localhost:5173");
        request.Headers.Add("X-DocumentIDE-Session-Token", token);

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task No_Wildcard_CORS_Header_On_Invalid_Origin()
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Origin", "http://malicious.com");
        var response = await client.SendAsync(request);
        // Ensure that the Access-Control-Allow-Origin header is not "*"
        if (response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values))
        {
            Assert.DoesNotContain(values, v => v == "*");
        }
        else
        {
            // Header not present is acceptable
            Assert.True(true);
        }
    }
}
