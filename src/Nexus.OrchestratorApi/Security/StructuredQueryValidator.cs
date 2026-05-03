using Nexus.Contracts;

namespace Nexus.OrchestratorApi.Security;

public sealed class StructuredQueryValidator
{
    private static readonly HashSet<string> AllowedOperators = new(StringComparer.OrdinalIgnoreCase)
    {
        "eq",
        "neq",
        "gte",
        "lte",
        "between",
        "contains"
    };

    private readonly QueryAllowlist allowlist;

    public StructuredQueryValidator(QueryAllowlist allowlist)
    {
        this.allowlist = allowlist;
    }

    public StructuredQueryValidationResult Validate(StructuredQuery query)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(query.Table))
        {
            errors.Add("Table is required.");
            return StructuredQueryValidationResult.Failure(errors);
        }

        if (!allowlist.Tables.TryGetValue(query.Table, out var tableAllowlist))
        {
            errors.Add($"Table '{query.Table}' is not allowlisted.");
            return StructuredQueryValidationResult.Failure(errors);
        }

        ValidateSelect(query, tableAllowlist, errors);
        ValidateFilters(query, tableAllowlist, errors);
        ValidateOrderBy(query, tableAllowlist, errors);

        var effectiveLimit = ValidateLimit(query, errors);

        return errors.Count == 0
            ? StructuredQueryValidationResult.Success(effectiveLimit!.Value)
            : StructuredQueryValidationResult.Failure(errors);
    }

    private static void ValidateSelect(
        StructuredQuery query,
        QueryAllowlistTable tableAllowlist,
        List<string> errors)
    {
        if (query.Select.Count == 0)
        {
            errors.Add("Select must not be empty.");
            return;
        }

        foreach (var column in query.Select)
        {
            if (!ContainsColumn(tableAllowlist.Select, column))
            {
                errors.Add($"Select column '{column}' is not allowlisted.");
            }
        }
    }

    private static void ValidateFilters(
        StructuredQuery query,
        QueryAllowlistTable tableAllowlist,
        List<string> errors)
    {
        foreach (var filter in query.Filters)
        {
            if (!ContainsColumn(tableAllowlist.Filter, filter.Column))
            {
                errors.Add($"Filter column '{filter.Column}' is not allowlisted.");
            }

            if (string.IsNullOrWhiteSpace(filter.Op) || !AllowedOperators.Contains(filter.Op))
            {
                errors.Add($"Filter operator '{filter.Op}' is not supported.");
                continue;
            }

            if (!string.Equals(filter.Op, "between", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(filter.Value2))
            {
                errors.Add("Value2 is only supported for between filters.");
            }

            if (string.IsNullOrWhiteSpace(filter.Value))
            {
                errors.Add($"Filter '{filter.Column}' requires value.");
            }

            if (string.Equals(filter.Op, "between", StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(filter.Value2))
            {
                errors.Add("Between filters require value and value2.");
            }

            if (string.Equals(filter.Op, "contains", StringComparison.OrdinalIgnoreCase) &&
                !IsStringColumn(tableAllowlist, filter.Column))
            {
                errors.Add($"Contains is only supported for string columns. Column '{filter.Column}' is not string.");
            }
        }
    }

    private static void ValidateOrderBy(
        StructuredQuery query,
        QueryAllowlistTable tableAllowlist,
        List<string> errors)
    {
        foreach (var orderBy in query.OrderBy)
        {
            if (!ContainsColumn(tableAllowlist.OrderBy, orderBy.Column))
            {
                errors.Add($"OrderBy column '{orderBy.Column}' is not allowlisted.");
            }

            if (!string.Equals(orderBy.Dir, "asc", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(orderBy.Dir, "desc", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("OrderBy dir must be asc or desc.");
            }
        }
    }

    private int? ValidateLimit(StructuredQuery query, List<string> errors)
    {
        if (query.Limit is null)
        {
            errors.Add("Limit is required.");
            return null;
        }

        if (query.Limit <= 0)
        {
            errors.Add("Limit must be greater than zero.");
            return null;
        }

        return Math.Min(query.Limit.Value, allowlist.MaxLimit);
    }

    private static bool ContainsColumn(IReadOnlyList<string> columns, string? column) =>
        !string.IsNullOrWhiteSpace(column) &&
        columns.Contains(column, StringComparer.OrdinalIgnoreCase);

    private static bool IsStringColumn(QueryAllowlistTable tableAllowlist, string? column) =>
        !string.IsNullOrWhiteSpace(column) &&
        tableAllowlist.ColumnTypes.TryGetValue(column, out var columnType) &&
        string.Equals(columnType, "string", StringComparison.OrdinalIgnoreCase);
}
