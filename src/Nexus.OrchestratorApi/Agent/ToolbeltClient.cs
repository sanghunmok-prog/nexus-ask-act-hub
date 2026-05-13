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
    public ToolbeltClientException(
        string toolName,
        HttpStatusCode? statusCode,
        string message,
        string? errorCode = null,
        string? errorMessage = null,
        IReadOnlyList<string>? errorDetails = null)
        : base(message)
    {
        ToolName = toolName;
        StatusCode = statusCode;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        ErrorDetails = errorDetails ?? [];
    }

    public string ToolName { get; }

    public HttpStatusCode? StatusCode { get; }

    public string? ErrorCode { get; }

    public string? ErrorMessage { get; }

    public IReadOnlyList<string> ErrorDetails { get; }
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
            var error = TryReadToolError(responseText);
            throw new ToolbeltClientException(
                step.ToolName,
                response.StatusCode,
                "Toolbelt returned an unsuccessful status code.",
                error.Code,
                error.Message,
                error.Errors);
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

    private static ToolbeltError TryReadToolError(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return new ToolbeltError(null, null, []);
        }

        try
        {
            using var document = JsonDocument.Parse(responseText);
            var root = document.RootElement;
            var code = TryReadString(root, "code");
            var message = TryReadString(root, "message");
            var errors = root.TryGetProperty("errors", out var errorsProperty) &&
                errorsProperty.ValueKind == JsonValueKind.Array
                    ? errorsProperty.EnumerateArray()
                        .Where(error => error.ValueKind == JsonValueKind.String)
                        .Select(error => error.GetString())
                        .Where(error => !string.IsNullOrWhiteSpace(error))
                        .Select(error => error!)
                        .ToArray()
                    : [];

            return new ToolbeltError(code, message, errors);
        }
        catch (JsonException)
        {
            return new ToolbeltError(null, null, []);
        }
    }

    private static string? TryReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private sealed record ToolbeltError(string? Code, string? Message, IReadOnlyList<string> Errors);
}
