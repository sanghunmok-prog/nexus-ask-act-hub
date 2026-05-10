namespace Nexus.OrchestratorApi.Approvals;

public sealed class ApprovalIntentFactory
{
    public const string GitHubCreateIssueToolName = "github.create_issue";
    public const string DefaultDemoRepo = "sanghunmok-prog/nexus-ask-act-hub";
    public const string RiskSummary = "Creates a GitHub issue. No action will run until approved.";

    private readonly IConfiguration configuration;
    private readonly IHostEnvironment environment;

    public ApprovalIntentFactory(IConfiguration configuration, IHostEnvironment environment)
    {
        this.configuration = configuration;
        this.environment = environment;
    }

    public bool IsActionIntent(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return false;
        }

        var normalized = prompt.ToLowerInvariant();
        return normalized.Contains("create", StringComparison.Ordinal) &&
               (normalized.Contains("issue", StringComparison.Ordinal) ||
                normalized.Contains("ticket", StringComparison.Ordinal));
    }

    public PendingGithubIssueArgs CreateGitHubIssueArgs() =>
        new()
        {
            Repo = ResolveDemoRepo(),
            Title = "Delayed shipments review",
            Body = "Review delayed shipment findings from NEXUS. Approval is required before this issue is created.",
            Labels = ["nexus-demo"]
        };

    private string ResolveDemoRepo()
    {
        var configured = configuration["NEXUS_DEMO_GITHUB_REPO"] ?? configuration["GitHub:DemoRepo"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        return environment.IsDevelopment() ? DefaultDemoRepo : DefaultDemoRepo;
    }
}
