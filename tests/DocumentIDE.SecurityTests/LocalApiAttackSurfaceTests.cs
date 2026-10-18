using System.Net;
using System.Net.Http.Json;
using DocumentIDE.Api.Security;
using DocumentIDE.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DocumentIDE.SecurityTests;

public class CustomSecurityTestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"sec_audit_{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(AppDb));
            if (descriptor != null)
            {
                services.Remove(descriptor);
            }
            services.AddSingleton(new AppDb(_dbPath));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }
}

public class LocalApiAttackSurfaceTests : IClassFixture<CustomSecurityTestWebApplicationFactory>
{
    private readonly CustomSecurityTestWebApplicationFactory _factory;

    public LocalApiAttackSurfaceTests(CustomSecurityTestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // Step 1: Automate Hostile Origin Requests
    [Theory]
    [InlineData("http://malicious.com")]
    [InlineData("https://attacker.org")]
    [InlineData("null")]
    [InlineData("http://evil.localhost.com")]
    [InlineData("http://localhost.evil.com")]
    [InlineData("http://127.0.0.1.attacker.com")]
    [InlineData("http://[::1].attacker.com")]
    [InlineData("https://evilappassets.html2sav")]
    [InlineData("http://appassets.html2sav")]
    [InlineData("https://appassets.html2sav.attacker.com")]
    [InlineData("not-a-valid-uri")]
    public async Task Hostile_Origin_Requests_Are_Rejected_With_Forbidden(string hostileOrigin)
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.TryAddWithoutValidation("Origin", hostileOrigin);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Theory]
    [InlineData("http://malicious.com")]
    [InlineData("https://attacker.org")]
    public async Task CrossSite_FetchMetadata_Is_Rejected_With_Forbidden(string origin)
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Sec-Fetch-Site", "cross-site");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // Step 2: Automate Host / DNS-Rebinding Style Mismatch Tests
    [Theory]
    [InlineData("attacker.com")]
    [InlineData("192.168.1.100")]
    [InlineData("10.0.0.1")]
    [InlineData("localhost.attacker.com")]
    [InlineData("127.0.0.1.attacker.com")]
    [InlineData("[::1].attacker.com")]
    [InlineData("evil.com:5000")]
    [InlineData("0.0.0.0")]
    public async Task DNS_Rebinding_Host_Header_Mismatches_Are_Rejected(string hostileHost)
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Host = hostileHost;

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // Step 3: Test Missing / Wrong Startup Secret
    [Fact]
    public async Task Missing_Session_Token_Is_Rejected_With_Unauthorized()
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Origin", "http://localhost:5173");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("wrong_secret_123")]
    [InlineData("invalid-session-token")]
    [InlineData("a")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("   ")]
    public async Task Wrong_Session_Token_Is_Rejected_With_Unauthorized(string invalidToken)
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Origin", "http://localhost:5173");
        request.Headers.Add("X-DocumentIDE-Session-Token", invalidToken);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("Basic dXNlcjpwYXNz")]
    [InlineData("Token 12345")]
    [InlineData("Bearer ")]
    [InlineData("Bearer wrong_secret")]
    public async Task Malformed_Authorization_Headers_Are_Rejected(string authHeader)
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Origin", "http://localhost:5173");
        request.Headers.Add("Authorization", authHeader);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Valid_Session_Token_Via_CustomHeader_Succeeds()
    {
        var token = GetValidSessionToken();
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Origin", "http://localhost:5173");
        request.Headers.Add("X-DocumentIDE-Session-Token", token);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Valid_Session_Token_Via_BearerToken_Succeeds()
    {
        var token = GetValidSessionToken();
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Origin", "http://localhost:5173");
        request.Headers.Add("Authorization", $"Bearer {token}");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // Step 4: Test CORS Policy and State-Changing Method Rules
    [Fact]
    public async Task Preflight_OPTIONS_Request_From_Hostile_Origin_Is_Rejected()
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Options, "/");
        request.Headers.Add("Origin", "http://malicious.com");
        request.Headers.Add("Access-Control-Request-Method", "POST");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Preflight_OPTIONS_Request_From_Allowed_Origin_Returns_NoContent_With_CORS_Headers()
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Options, "/");
        var allowedOrigin = "http://localhost:5173";
        request.Headers.Add("Origin", allowedOrigin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "X-DocumentIDE-Session-Token, Content-Type");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.Contains(allowedOrigin, response.Headers.GetValues("Access-Control-Allow-Origin"));
        Assert.DoesNotContain("*", response.Headers.GetValues("Access-Control-Allow-Origin"));
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    public async Task State_Changing_Methods_With_Hostile_Origin_Are_Blocked_Before_Controller(string httpMethod)
    {
        var client = _factory.CreateClient();
        var method = new HttpMethod(httpMethod);
        var request = new HttpRequestMessage(method, "/api/workspaces/ws1/folder-sources");
        request.Headers.Add("Origin", "http://attacker.com");
        request.Content = JsonContent.Create(new { FolderPath = "C:\\Malicious" });

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task State_Changing_Methods_Without_Session_Token_Are_Blocked_Before_Controller(string httpMethod)
    {
        var client = _factory.CreateClient();
        var method = new HttpMethod(httpMethod);
        var request = new HttpRequestMessage(method, "/api/workspaces/ws1/folder-sources");
        request.Headers.Add("Origin", "http://localhost:5173");
        request.Content = JsonContent.Create(new { FolderPath = "C:\\Malicious" });

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("http://localhost:5173")]
    [InlineData("https://appassets.html2sav")]
    [InlineData("http://malicious.com")]
    public async Task No_Wildcard_CORS_Header_Ever_Returned(string origin)
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.TryAddWithoutValidation("Origin", origin);

        var response = await client.SendAsync(request);

        if (response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values))
        {
            Assert.DoesNotContain(values, v => v == "*");
        }
    }

    // Step 5: Browser-Based Attack Page Fixture / Hostile Client Simulation against Live Host
    [Fact]
    public async Task Browser_Attack_Page_Fixture_Simulation_Cross_Origin_Fetch_Blocked()
    {
        // Simulate a malicious web page running inside a user's web browser at http://attacker-site.com
        var hostileBrowserClient = _factory.CreateClient();

        // 1. Attack scenario A: Attacker page attempts cross-origin POST to attach arbitrary folder
        var postRequest = new HttpRequestMessage(HttpMethod.Post, "/api/workspaces/victim-ws/folder-sources");
        postRequest.Headers.Add("Origin", "http://attacker-site.com");
        postRequest.Headers.Add("Referer", "http://attacker-site.com/exploit.html");
        postRequest.Headers.Add("Sec-Fetch-Site", "cross-site");
        postRequest.Headers.Add("Sec-Fetch-Mode", "cors");
        postRequest.Headers.Add("Sec-Fetch-Dest", "empty");
        postRequest.Content = JsonContent.Create(new { FolderPath = "C:\\Windows\\System32" });

        var postResponse = await hostileBrowserClient.SendAsync(postRequest);
        Assert.Equal(HttpStatusCode.Forbidden, postResponse.StatusCode);
        Assert.False(postResponse.Headers.Contains("Access-Control-Allow-Origin"));

        // 2. Attack scenario B: Attacker page attempts cross-origin GET
        var getRequest = new HttpRequestMessage(HttpMethod.Get, "/api/workspaces/victim-ws/folder-sources");
        getRequest.Headers.Add("Origin", "http://attacker-site.com");
        getRequest.Headers.Add("Referer", "http://attacker-site.com/exploit.html");
        getRequest.Headers.Add("Sec-Fetch-Site", "cross-site");

        var getResponse = await hostileBrowserClient.SendAsync(getRequest);
        Assert.Equal(HttpStatusCode.Forbidden, getResponse.StatusCode);

        // 3. Attack scenario C: DNS Rebinding attack setting Host header to attacker domain
        var rebindingRequest = new HttpRequestMessage(HttpMethod.Get, "/");
        rebindingRequest.Headers.Host = "attacker-site.com";
        rebindingRequest.Headers.Add("Origin", "http://attacker-site.com");

        var rebindingResponse = await hostileBrowserClient.SendAsync(rebindingRequest);
        Assert.Equal(HttpStatusCode.Forbidden, rebindingResponse.StatusCode);
    }

    private string GetValidSessionToken()
    {
        using var scope = _factory.Services.CreateScope();
        var tokenProvider = scope.ServiceProvider.GetRequiredService<ISessionTokenProvider>();
        return tokenProvider.SessionToken;
    }
}
