using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Nexus.Mcp.Toolbelt.Tools;

namespace Nexus.Mcp.Toolbelt.Tests;

public sealed class GitHubCreateIssueToolTests
{
    [Fact]
    public async Task Missing_token_returns_sanitized_config_error()
    {
        var client = new FakeGitHubIssueClient();
        var result = await CreateTool(client, allowedRepos: "owner/repo").CreateIssueAsync(ValidRequest());

        Assert.False(result.Succeeded);
        Assert.Equal("GITHUB_NOT_CONFIGURED", result.Error?.Code);
        Assert.Equal(0, client.CallCount);
        Assert.DoesNotContain("token", result.Error?.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Repo_not_in_allowlist_returns_sanitized_error_without_calling_github()
    {
        var client = new FakeGitHubIssueClient();
        var result = await CreateTool(client, token: "test-token", allowedRepos: "other/repo").CreateIssueAsync(ValidRequest());

        Assert.False(result.Succeeded);
        Assert.Equal("GITHUB_REPO_NOT_ALLOWED", result.Error?.Code);
        Assert.Equal(0, client.CallCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("owner")]
    [InlineData("owner/repo/extra")]
    [InlineData("owner repo/name")]
    public async Task Invalid_repo_format_is_rejected(string repo)
    {
        var client = new FakeGitHubIssueClient();
        var result = await CreateTool(client, token: "test-token", allowedRepos: repo).CreateIssueAsync(
            ValidRequest() with { Repo = repo });

        Assert.False(result.Succeeded);
        Assert.Equal("GITHUB_REPO_INVALID", result.Error?.Code);
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public async Task Valid_request_calls_github_client_and_returns_issue()
    {
        var client = new FakeGitHubIssueClient();
        var result = await CreateTool(client, token: "test-token", allowedRepos: "owner/repo").CreateIssueAsync(ValidRequest());

        Assert.True(result.Succeeded);
        Assert.Equal(1, client.CallCount);
        Assert.Equal("test-token", client.Token);
        Assert.Equal("owner/repo", client.Request?.Repo);
        Assert.Equal(123, result.Response?.Number);
        Assert.Equal("https://github.com/owner/repo/issues/123", result.Response?.HtmlUrl);
        Assert.Equal("Delayed shipments review", result.Response?.Title);
    }

    [Fact]
    public async Task Http_client_sends_create_issue_request_and_parses_response()
    {
        var handler = new FakeHttpMessageHandler();
        var client = new HttpGitHubIssueClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.github.test")
        });

        var result = await client.CreateIssueAsync("test-token", ValidRequest());

        Assert.True(result.Succeeded);
        Assert.Equal(HttpMethod.Post, handler.Request?.Method);
        Assert.Equal("https://api.github.test/repos/owner/repo/issues", handler.Request?.RequestUri?.ToString());
        Assert.Equal("Bearer", handler.Request?.Headers.Authorization?.Scheme);
        Assert.Equal("test-token", handler.Request?.Headers.Authorization?.Parameter);
        Assert.NotNull(handler.Request);
        Assert.Contains(handler.Request.Headers.Accept, value => value.MediaType == "application/vnd.github+json");
        Assert.Contains("\"title\":\"Delayed shipments review\"", handler.RequestBody);
        Assert.Equal(123, result.Response?.Number);
        Assert.Equal("https://github.com/owner/repo/issues/123", result.Response?.HtmlUrl);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "GITHUB_AUTH_FAILED", StatusCodes.Status401Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, "GITHUB_PERMISSION_FAILED", StatusCodes.Status403Forbidden)]
    [InlineData(HttpStatusCode.NotFound, "GITHUB_REPO_NOT_ACCESSIBLE", StatusCodes.Status404NotFound)]
    [InlineData(HttpStatusCode.Gone, "GITHUB_ISSUES_DISABLED", StatusCodes.Status409Conflict)]
    [InlineData(HttpStatusCode.UnprocessableEntity, "GITHUB_VALIDATION_FAILED", StatusCodes.Status400BadRequest)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "GITHUB_TEMPORARY_FAILURE", StatusCodes.Status503ServiceUnavailable)]
    public async Task GitHub_errors_are_mapped_to_sanitized_errors(
        HttpStatusCode statusCode,
        string expectedCode,
        int expectedHttpStatus)
    {
        var client = new FakeGitHubIssueClient(statusCode);
        var result = await CreateTool(client, token: "test-token", allowedRepos: "owner/repo").CreateIssueAsync(ValidRequest());
        var responseJson = JsonSerializer.Serialize(result.Error);

        Assert.False(result.Succeeded);
        Assert.Equal(expectedHttpStatus, result.StatusCode);
        Assert.Equal(expectedCode, result.Error?.Code);
        Assert.DoesNotContain("test-token", responseJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("raw github body", responseJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stack", responseJson, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "GITHUB_AUTH_FAILED")]
    [InlineData(HttpStatusCode.NotFound, "GITHUB_REPO_NOT_ACCESSIBLE")]
    public async Task Http_github_errors_do_not_surface_raw_response_body(HttpStatusCode statusCode, string expectedCode)
    {
        var handler = new FakeHttpMessageHandler(statusCode, "raw github body with test-token and stack trace");
        var httpClient = new HttpGitHubIssueClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.github.test")
        });
        var clientResult = await httpClient.CreateIssueAsync("test-token", ValidRequest());
        var client = new FakeGitHubIssueClient(clientResult.StatusCode);
        var result = await CreateTool(client, token: "test-token", allowedRepos: "owner/repo").CreateIssueAsync(ValidRequest());
        var responseJson = JsonSerializer.Serialize(result.Error);

        Assert.False(clientResult.Succeeded);
        Assert.False(result.Succeeded);
        Assert.Equal(expectedCode, result.Error?.Code);
        Assert.DoesNotContain("test-token", responseJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("raw github body", responseJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stack", responseJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Network_failure_returns_sanitized_temporary_failure()
    {
        var client = new ThrowingGitHubIssueClient();
        var result = await CreateTool(client, token: "test-token", allowedRepos: "owner/repo").CreateIssueAsync(ValidRequest());
        var responseJson = JsonSerializer.Serialize(result.Error);

        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
        Assert.Equal("GITHUB_TEMPORARY_FAILURE", result.Error?.Code);
        Assert.DoesNotContain("test-token", responseJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("raw github body", responseJson, StringComparison.OrdinalIgnoreCase);
    }

    private static GitHubCreateIssueTool CreateTool(
        IGitHubIssueClient client,
        string? token = null,
        string? allowedRepos = null)
    {
        var values = new Dictionary<string, string?>();
        if (token is not null)
        {
            values["NEXUS_GITHUB_TOKEN"] = token;
        }

        if (allowedRepos is not null)
        {
            values["NEXUS_GITHUB_ALLOWED_REPOS"] = allowedRepos;
        }

        return new GitHubCreateIssueTool(client, new ConfigurationBuilder().AddInMemoryCollection(values).Build());
    }

    private static GitHubCreateIssueRequest ValidRequest() =>
        new()
        {
            Repo = "owner/repo",
            Title = "Delayed shipments review",
            Body = "Review delayed shipment findings from NEXUS.",
            Labels = ["nexus-demo"]
        };

    private sealed class FakeGitHubIssueClient : IGitHubIssueClient
    {
        private readonly HttpStatusCode? statusCode;

        public FakeGitHubIssueClient(HttpStatusCode? statusCode = null)
        {
            this.statusCode = statusCode;
        }

        public int CallCount { get; private set; }

        public string? Token { get; private set; }

        public GitHubCreateIssueRequest? Request { get; private set; }

        public Task<GitHubIssueClientResult> CreateIssueAsync(
            string token,
            GitHubCreateIssueRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            Token = token;
            Request = request;

            if (statusCode is not null)
            {
                return Task.FromResult(GitHubIssueClientResult.Failure(statusCode.Value));
            }

            return Task.FromResult(GitHubIssueClientResult.Success(new GitHubCreateIssueResponse
            {
                Number = 123,
                HtmlUrl = "https://github.com/owner/repo/issues/123",
                Title = "Delayed shipments review"
            }));
        }
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode statusCode;
        private readonly string? responseBody;

        public FakeHttpMessageHandler(
            HttpStatusCode statusCode = HttpStatusCode.Created,
            string? responseBody = null)
        {
            this.statusCode = statusCode;
            this.responseBody = responseBody;
        }

        public HttpRequestMessage? Request { get; private set; }

        public string RequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            RequestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            if (statusCode != HttpStatusCode.Created)
            {
                return new HttpResponseMessage(statusCode)
                {
                    Content = new StringContent(responseBody ?? string.Empty)
                };
            }

            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    number = 123,
                    html_url = "https://github.com/owner/repo/issues/123",
                    title = "Delayed shipments review"
                }))
            };
        }
    }

    private sealed class ThrowingGitHubIssueClient : IGitHubIssueClient
    {
        public Task<GitHubIssueClientResult> CreateIssueAsync(
            string token,
            GitHubCreateIssueRequest request,
            CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("network failure with test-token and raw github body");
    }
}
