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

namespace DocumentIDE.IntegrationTests.Word;

public class WordDesktopLauncherTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public WordDesktopLauncherTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task OpenInWord_WithoutSessionToken_ReturnsUnauthorized()
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
    public async Task OpenInWord_WithInvalidOrigin_ReturnsForbidden()
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/word/open")
        {
            Content = JsonContent.Create(new OpenWordDocumentRequest("doc-1"))
        };
        request.Headers.Add("Origin", "http://malicious.com");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task OpenInWord_WhenDocumentNotFound_ReturnsNotFound()
    {
        using var scope = _factory.Services.CreateScope();
        var tokenProvider = scope.ServiceProvider.GetRequiredService<ISessionTokenProvider>();
        var token = tokenProvider.SessionToken;

        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/word/open")
        {
            Content = JsonContent.Create(new OpenWordDocumentRequest("non-existent-doc-id"))
        };
        request.Headers.Add("Origin", "http://localhost:5173");
        request.Headers.Add("X-DocumentIDE-Session-Token", token);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<WordLaunchResult>();
        Assert.NotNull(result);
        Assert.False(result!.Success);
        Assert.Equal(WordLaunchStatus.DocumentNotFound, result.Status);
    }

    [Fact]
    public async Task OpenInWord_ViaDocumentIdRoute_WhenDocumentNotFound_ReturnsNotFound()
    {
        using var scope = _factory.Services.CreateScope();
        var tokenProvider = scope.ServiceProvider.GetRequiredService<ISessionTokenProvider>();
        var token = tokenProvider.SessionToken;

        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/documents/non-existent-doc-id/open-in-word");
        request.Headers.Add("Origin", "http://localhost:5173");
        request.Headers.Add("X-DocumentIDE-Session-Token", token);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<WordLaunchResult>();
        Assert.NotNull(result);
        Assert.False(result!.Success);
        Assert.Equal(WordLaunchStatus.DocumentNotFound, result.Status);
    }

    [Fact]
    public async Task OpenInWord_WhenWordNotInstalled_ReturnsServiceUnavailable()
    {
        var customFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddScoped<IWordDesktopLauncher>(sp =>
                {
                    var repo = sp.GetRequiredService<IDocumentRepository>();
                    return new WordDesktopLauncher(
                        repo,
                        processLauncher: _ => true,
                        availabilityChecker: () => false
                    );
                });
            });
        });

        using var scope = customFactory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        string docId = Guid.NewGuid().ToString();
        var doc = new Document
        {
            Id = docId,
            CurrentPath = "C:\\test.docx",
            Filename = "test.docx",
            Extension = ".docx",
            DocumentType = "docx"
        };
        await repo.CreateAsync(doc);

        var tokenProvider = scope.ServiceProvider.GetRequiredService<ISessionTokenProvider>();
        var token = tokenProvider.SessionToken;

        var client = customFactory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/word/open")
        {
            Content = JsonContent.Create(new OpenWordDocumentRequest(docId))
        };
        request.Headers.Add("Origin", "http://localhost:5173");
        request.Headers.Add("X-DocumentIDE-Session-Token", token);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<WordLaunchResult>();
        Assert.NotNull(result);
        Assert.False(result!.Success);
        Assert.Equal(WordLaunchStatus.WordNotInstalled, result.Status);
        Assert.Equal("Microsoft Word is required for this operation.", result.ErrorMessage);
    }
}
