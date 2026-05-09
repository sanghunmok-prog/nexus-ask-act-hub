using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Nexus.OrchestratorApi.Agent;

public interface IToolbeltClient
{
    Task<ToolbeltToolResult> CallAsync(ToolPlanStep step, CancellationToken cancellationToken = default);
}

public sealed record ToolbeltToolResult(string ToolName, JsonElement RawJson);

public sealed class ToolbeltClientException : Exception
{
    public ToolbeltClientException(string toolName, HttpStatusCode? statusCode, string message)
        : base(message)
    {
        ToolName = toolName;
        StatusCode = statusCode;
    }

    public string ToolName { get; }

    public HttpStatusCode? StatusCode { get; }
}

public sealed class ToolbeltConfigurationException : Exception
{
    public ToolbeltConfigurationException(string message)
        : base(message)
    {
    }
}

public sealed class HttpToolbeltClient : IToolbeltClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient httpClient;

    public HttpToolbeltClient(HttpClient httpClient, IConfiguration configuration, IHostEnvironment environment)
    {
        this.httpClient = httpClient;

        var baseUrl = ResolveBaseUrl(configuration, environment);
        if (baseUrl is not null)
        {
            this.httpClient.BaseAddress = baseUrl;
        }

        this.httpClient.Timeout = TimeSpan.FromSeconds(20);
    }

    public async Task<ToolbeltToolResult> CallAsync(ToolPlanStep step, CancellationToken cancellationToken = default)
    {
        if (httpClient.BaseAddress is null)
        {
            throw new ToolbeltConfigurationException("Toolbelt base URL is not configured.");
        }

        using var request = CreateRequest(step);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new ToolbeltClientException(
                step.ToolName,
                response.StatusCode,
                "Toolbelt returned an unsuccessful status code.");
        }

        if (string.IsNullOrWhiteSpace(responseText))
        {
            return new ToolbeltToolResult(step.ToolName, JsonSerializer.SerializeToElement(new { }, JsonOptions));
        }

        try
        {
            return new ToolbeltToolResult(step.ToolName, JsonSerializer.Deserialize<JsonElement>(responseText, JsonOptions));
        }
        catch (JsonException)
        {
            throw new ToolbeltClientException(step.ToolName, response.StatusCode, "Toolbelt returned invalid JSON.");
        }
    }

    private static HttpRequestMessage CreateRequest(ToolPlanStep step)
    {
        var request = new HttpRequestMessage(new HttpMethod(step.Method), step.Endpoint);

        if (string.Equals(step.Method, HttpMethods.Post, StringComparison.OrdinalIgnoreCase))
        {
            request.Content = JsonContent.Create(step.Args, options: JsonOptions);
        }

        return request;
    }

    private static Uri? ResolveBaseUrl(IConfiguration configuration, IHostEnvironment environment)
    {
        var configured = configuration["NEXUS_TOOLBELT_BASE_URL"] ?? configuration["Toolbelt:BaseUrl"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return new Uri(configured, UriKind.Absolute);
        }

        return environment.IsDevelopment()
            ? new Uri("http://localhost:5062", UriKind.Absolute)
            : null;
    }
}
