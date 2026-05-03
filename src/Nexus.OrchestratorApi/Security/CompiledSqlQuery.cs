namespace Nexus.OrchestratorApi.Security;

public sealed record CompiledSqlQuery(
    string SqlText,
    IReadOnlyDictionary<string, object> Parameters);
