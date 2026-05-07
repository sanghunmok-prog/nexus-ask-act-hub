using Nexus.Contracts;

namespace Nexus.QuerySafety;

public sealed class StructuredQueryCompiler
{
    private readonly QueryAllowlist allowlist;
    private readonly StructuredQueryValidator validator;

    public StructuredQueryCompiler(QueryAllowlist allowlist)
    {
        this.allowlist = allowlist;
        validator = new StructuredQueryValidator(allowlist);
    }

    public CompiledSqlQuery Compile(StructuredQuery query)
    {
        var validation = validator.Validate(query);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(
                $"StructuredQuery is invalid: {string.Join(" ", validation.Errors)}");
        }

        var tableAllowlist = allowlist.Tables[query.Table!];
        var parameters = new Dictionary<string, object>(StringComparer.Ordinal);
        var parameterIndex = 0;

        var selectSql = string.Join(", ", query.Select.Select(column => QuoteColumn(tableAllowlist.Select, column)));
        var sql = $"SELECT TOP (@p_limit) {selectSql} FROM dbo.{query.Table}";

        if (query.Filters.Count > 0)
        {
            var predicates = query.Filters.Select(filter =>
                CompileFilter(filter, tableAllowlist, parameters, ref parameterIndex));

            sql += $" WHERE {string.Join(" AND ", predicates)}";
        }

        if (query.OrderBy.Count > 0)
        {
            var orderBySql = query.OrderBy.Select(orderBy =>
                $"{QuoteColumn(tableAllowlist.OrderBy, orderBy.Column!)} {orderBy.Dir!.ToUpperInvariant()}");

            sql += $" ORDER BY {string.Join(", ", orderBySql)}";
        }

        parameters["@p_limit"] = validation.EffectiveLimit!.Value;

        return new CompiledSqlQuery(sql, parameters);
    }

    private static string CompileFilter(
        StructuredQueryFilter filter,
        QueryAllowlistTable tableAllowlist,
        Dictionary<string, object> parameters,
        ref int parameterIndex)
    {
        var columnSql = QuoteColumn(tableAllowlist.Filter, filter.Column!);
        var op = filter.Op!;

        if (string.Equals(op, "between", StringComparison.OrdinalIgnoreCase))
        {
            var startParameter = NextParameterName(ref parameterIndex);
            var endParameter = NextParameterName(ref parameterIndex);
            parameters[startParameter] = filter.Value!;
            parameters[endParameter] = filter.Value2!;
            return $"{columnSql} BETWEEN {startParameter} AND {endParameter}";
        }

        var parameterName = NextParameterName(ref parameterIndex);
        parameters[parameterName] = string.Equals(op, "contains", StringComparison.OrdinalIgnoreCase)
            ? $"%{filter.Value}%"
            : filter.Value!;

        var sqlOperator = op.ToLowerInvariant() switch
        {
            "eq" => "=",
            "neq" => "<>",
            "gte" => ">=",
            "lte" => "<=",
            "contains" => "LIKE",
            _ => throw new InvalidOperationException($"Unsupported operator '{op}'.")
        };

        return $"{columnSql} {sqlOperator} {parameterName}";
    }

    private static string QuoteColumn(IReadOnlyList<string> allowlistedColumns, string column)
    {
        var canonicalColumn = allowlistedColumns.First(allowlistedColumn =>
            string.Equals(allowlistedColumn, column, StringComparison.OrdinalIgnoreCase));

        return $"[{canonicalColumn}]";
    }

    private static string NextParameterName(ref int parameterIndex)
    {
        var parameterName = $"@p{parameterIndex}";
        parameterIndex++;
        return parameterName;
    }
}
