namespace Nexus.QuerySafety;

public sealed record CompiledSqlQuery(
    string SqlText,
    IReadOnlyDictionary<string, object> Parameters);
