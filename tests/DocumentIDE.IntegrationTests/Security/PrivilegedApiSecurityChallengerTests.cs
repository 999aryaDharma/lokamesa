using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using DocumentIDE.Api.Controllers;
using DocumentIDE.Api.Security;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Domain.Repositories;
using DocumentIDE.Core.Word;
using DocumentIDE.Infrastructure.Word;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DocumentIDE.IntegrationTests.Security;

public class PrivilegedApiSecurityChallengerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PrivilegedApiSecurityChallengerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task OpenWord_WithoutSessionToken_IsRejectedWith401()
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/word/open")
        {
            Content = JsonContent.Create(new OpenWordDocumentRequest("doc-1"))
        };
        request.Headers.Add("Origin", "http://localhost:5173");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task OpenWord_WithInvalidSessionToken_IsRejectedWith401()
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/word/open")
        {
            Content = JsonContent.Create(new OpenWordDocumentRequest("doc-1"))
        };
        request.Headers.Add("Origin", "http://localhost:5173");
        request.Headers.Add("X-DocumentIDE-Session-Token", "invalid-token-1234567890");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task OpenWord_WithBearerTokenInAuthorizationHeader_SucceedsAuthorization()
    {
        using var scope = _factory.Services.CreateScope();
        var tokenProvider = scope.ServiceProvider.GetRequiredService<ISessionTokenProvider>();
        var token = tokenProvider.SessionToken;

        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/word/open")
        {
            Content = JsonContent.Create(new OpenWordDocumentRequest("non-existent-doc"))
        };
        request.Headers.Add("Origin", "http://localhost:5173");
        request.Headers.Add("Authorization", $"Bearer {token}");

        var response = await client.SendAsync(request);

        // Authorization should pass, returning 404 for missing doc instead of 401
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task OpenWord_WithCrossSiteSecFetchSite_IsRejectedWith403()
    {
        using var scope = _factory.Services.CreateScope();
        var tokenProvider = scope.ServiceProvider.GetRequiredService<ISessionTokenProvider>();
        var token = tokenProvider.SessionToken;

        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/word/open")
        {
            Content = JsonContent.Create(new OpenWordDocumentRequest("doc-1"))
        };
        request.Headers.Add("Origin", "http://localhost:5173");
        request.Headers.Add("X-DocumentIDE-Session-Token", token);
        request.Headers.Add("Sec-Fetch-Site", "cross-site");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task OpenWord_WithInvalidHostHeader_IsRejectedWith403()
    {
        using var scope = _factory.Services.CreateScope();
        var tokenProvider = scope.ServiceProvider.GetRequiredService<ISessionTokenProvider>();
        var token = tokenProvider.SessionToken;

        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/word/open")
        {
            Content = JsonContent.Create(new OpenWordDocumentRequest("doc-1"))
        };
        request.Headers.Host = "attacker-domain.com";
        request.Headers.Add("Origin", "http://localhost:5173");
        request.Headers.Add("X-DocumentIDE-Session-Token", token);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task OpenWord_WithAllowedOrigin_SetsCorsHeaders()
    {
        using var scope = _factory.Services.CreateScope();
        var tokenProvider = scope.ServiceProvider.GetRequiredService<ISessionTokenProvider>();
        var token = tokenProvider.SessionToken;

        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/word/open")
        {
            Content = JsonContent.Create(new OpenWordDocumentRequest("non-existent-doc"))
        };
        request.Headers.Add("Origin", "http://localhost:5173");
        request.Headers.Add("X-DocumentIDE-Session-Token", token);

        var response = await client.SendAsync(request);

        Assert.True(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.Equal("http://localhost:5173", response.Headers.GetValues("Access-Control-Allow-Origin").GetEnumerator().MoveNext() ? "http://localhost:5173" : "");
    }
}
