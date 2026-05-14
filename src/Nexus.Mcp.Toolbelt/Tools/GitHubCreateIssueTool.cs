using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Nexus.Mcp.Toolbelt.Tools;

public sealed class GitHubCreateIssueTool
{
    private readonly IGitHubIssueClient client;
    private readonly IConfiguration configuration;

    public GitHubCreateIssueTool(IGitHubIssueClient client, IConfiguration configuration)
    {
        this.client = client;
        this.configuration = configuration;
    }

    public async Task<GitHubCreateIssueToolResult> CreateIssueAsync(
        GitHubCreateIssueRequest request,
        CancellationToken cancellationToken = default)
    {
        var token = configuration["NEXUS_GITHUB_TOKEN"];
        if (string.IsNullOrWhiteSpace(token))
        {
            return GitHubCreateIssueToolResult.NotConfigured();
        }

        if (!IsValidRepo(request.Repo))
        {
            return GitHubCreateIssueToolResult.InvalidRepo();
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return GitHubCreateIssueToolResult.TitleRequired();
        }

        if (!AllowedRepos().Contains(request.Repo.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            return GitHubCreateIssueToolResult.RepoNotAllowed();
        }

        GitHubIssueClientResult clientResult;
        try
        {
            clientResult = await client.CreateIssueAsync(
                token.Trim(),
                request with
                {
                    Repo = request.Repo.Trim(),
                    Title = request.Title.Trim(),
                    Body = request.Body?.Trim(),
                    Labels = request.Labels
                        .Where(label => !string.IsNullOrWhiteSpace(label))
                        .Select(label => label.Trim())
                        .ToArray()
                },
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return GitHubCreateIssueToolResult.TemporaryFailure();
        }

        return clientResult.Succeeded
            ? GitHubCreateIssueToolResult.Success(clientResult.Response!)
            : GitHubCreateIssueToolResult.GitHubFailed(clientResult.StatusCode);
    }

    private IReadOnlySet<string> AllowedRepos() =>
        (configuration["NEXUS_GITHUB_ALLOWED_REPOS"] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static bool IsValidRepo(string? repo)
    {
        if (string.IsNullOrWhiteSpace(repo))
        {
            return false;
        }

        var parts = repo.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 2 &&
               parts.All(part => part.Length > 0 &&
                                 part.All(character => char.IsLetterOrDigit(character) ||
                                                       character is '-' or '_' or '.'));
    }
}

public interface IGitHubIssueClient
{
    Task<GitHubIssueClientResult> CreateIssueAsync(
        string token,
        GitHubCreateIssueRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class HttpGitHubIssueClient : IGitHubIssueClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient httpClient;

    public HttpGitHubIssueClient(HttpClient httpClient)
    {
        this.httpClient = httpClient;
        this.httpClient.BaseAddress ??= new Uri("https://api.github.com", UriKind.Absolute);
        this.httpClient.Timeout = TimeSpan.FromSeconds(20);
    }

    public async Task<GitHubIssueClientResult> CreateIssueAsync(
        string token,
        GitHubCreateIssueRequest request,
        CancellationToken cancellationToken = default)
    {
        var repoParts = request.Repo.Split('/', 2);
        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            $"/repos/{Uri.EscapeDataString(repoParts[0])}/{Uri.EscapeDataString(repoParts[1])}/issues");
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        message.Headers.UserAgent.ParseAdd("NEXUS-Toolbelt");
        message.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        message.Content = JsonContent.Create(
            new
            {
                title = request.Title,
                body = request.Body ?? string.Empty,
                labels = request.Labels
            },
            options: JsonOptions);

        using var response = await httpClient.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return GitHubIssueClientResult.Failure(response.StatusCode);
        }

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var body = await JsonSerializer.DeserializeAsync<JsonElement>(stream, JsonOptions, cancellationToken);
            return GitHubIssueClientResult.Success(new GitHubCreateIssueResponse
            {
                Number = body.TryGetProperty("number", out var number) && number.TryGetInt32(out var value)
                    ? value
                    : 0,
                HtmlUrl = body.TryGetProperty("html_url", out var htmlUrl) && htmlUrl.ValueKind == JsonValueKind.String
                    ? htmlUrl.GetString() ?? string.Empty
                    : string.Empty,
                Title = body.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String
                    ? title.GetString() ?? string.Empty
                    : request.Title
            });
        }
        catch (JsonException)
        {
            return GitHubIssueClientResult.Failure(HttpStatusCode.BadGateway);
        }
    }
}

public sealed record GitHubCreateIssueRequest
{
    public string Repo { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string? Body { get; init; }

    public IReadOnlyList<string> Labels { get; init; } = [];
}

public sealed record GitHubCreateIssueResponse
{
    public int Number { get; init; }

    public string HtmlUrl { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;
}

public sealed record GitHubIssueToolErrorResponse
{
    public string Code { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public IReadOnlyList<string> Errors { get; init; } = [];
}

public sealed record GitHubCreateIssueToolResult(
    bool Succeeded,
    int StatusCode,
    GitHubCreateIssueResponse? Response,
    GitHubIssueToolErrorResponse? Error)
{
    public static GitHubCreateIssueToolResult Success(GitHubCreateIssueResponse response) =>
        new(true, StatusCodes.Status200OK, response, null);

    public static GitHubCreateIssueToolResult NotConfigured() =>
        Failure(
            StatusCodes.Status400BadRequest,
            "GITHUB_NOT_CONFIGURED",
            "GitHub issue creation is not configured.");

    public static GitHubCreateIssueToolResult RepoNotAllowed() =>
        Failure(
            StatusCodes.Status403Forbidden,
            "GITHUB_REPO_NOT_ALLOWED",
            "GitHub repo is not allowed for issue creation.");

    public static GitHubCreateIssueToolResult InvalidRepo() =>
        Failure(
            StatusCodes.Status400BadRequest,
            "GITHUB_REPO_INVALID",
            "GitHub repo must use owner/repo format.");

    public static GitHubCreateIssueToolResult TitleRequired() =>
        Failure(
            StatusCodes.Status400BadRequest,
            "GITHUB_TITLE_REQUIRED",
            "GitHub issue title is required.");

    public static GitHubCreateIssueToolResult GitHubFailed(HttpStatusCode statusCode) =>
        statusCode switch
        {
            HttpStatusCode.Unauthorized => Failure(
                StatusCodes.Status401Unauthorized,
                "GITHUB_AUTH_FAILED",
                "GitHub authentication or configuration check failed."),
            HttpStatusCode.Forbidden => Failure(
                StatusCodes.Status403Forbidden,
                "GITHUB_PERMISSION_FAILED",
                "GitHub permission or configuration check failed."),
            HttpStatusCode.NotFound => Failure(
                StatusCodes.Status404NotFound,
                "GITHUB_REPO_NOT_ACCESSIBLE",
                "GitHub repo was not found or is not accessible."),
            HttpStatusCode.Gone => Failure(
                StatusCodes.Status409Conflict,
                "GITHUB_ISSUES_DISABLED",
                "GitHub issues are disabled for this repo."),
            HttpStatusCode.UnprocessableEntity => Failure(
                StatusCodes.Status400BadRequest,
                "GITHUB_VALIDATION_FAILED",
                "GitHub rejected the issue fields."),
            HttpStatusCode.ServiceUnavailable or HttpStatusCode.TooManyRequests => Failure(
                StatusCodes.Status503ServiceUnavailable,
                "GITHUB_TEMPORARY_FAILURE",
                "GitHub issue creation is temporarily unavailable."),
            _ => Failure(
                StatusCodes.Status502BadGateway,
                "GITHUB_CREATE_ISSUE_FAILED",
                "GitHub issue creation failed.")
        };

    public static GitHubCreateIssueToolResult TemporaryFailure() =>
        Failure(
            StatusCodes.Status503ServiceUnavailable,
            "GITHUB_TEMPORARY_FAILURE",
            "GitHub issue creation is temporarily unavailable.");

    private static GitHubCreateIssueToolResult Failure(int statusCode, string code, string message) =>
        new(
            false,
            statusCode,
            null,
            new GitHubIssueToolErrorResponse
            {
                Code = code,
                Message = message
            });
}

public sealed record GitHubIssueClientResult(
    bool Succeeded,
    HttpStatusCode StatusCode,
    GitHubCreateIssueResponse? Response)
{
    public static GitHubIssueClientResult Success(GitHubCreateIssueResponse response) =>
        new(true, HttpStatusCode.OK, response);

    public static GitHubIssueClientResult Failure(HttpStatusCode statusCode) =>
        new(false, statusCode, null);
}
