using Nexus.Contracts;
using Nexus.OrchestratorApi.Security;

namespace Nexus.OrchestratorApi.Tests;

public sealed class StructuredQueryTests
{
    [Fact]
    public async Task Allowlist_json_loads_orders_rules()
    {
        var allowlist = await QueryAllowlist.LoadAsync(AllowlistPath());

        Assert.True(allowlist.SingleTableOnly);
        Assert.Equal(200, allowlist.MaxLimit);
        Assert.True(allowlist.Tables.ContainsKey("Orders"));
        Assert.Contains("OrderId", allowlist.Tables["Orders"].Select);
        Assert.Contains("ExpectedShipDateUtc", allowlist.Tables["Orders"].Filter);
        Assert.Contains("CreatedAtUtc", allowlist.Tables["Orders"].OrderBy);
        Assert.Equal("string", allowlist.Tables["Orders"].ColumnTypes["Status"]);
    }

    [Fact]
    public void Valid_orders_query_passes_validation()
    {
        var result = Validator().Validate(DelayedOrdersQuery());

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Equal(50, result.EffectiveLimit);
    }

    [Fact]
    public void Unknown_table_fails()
    {
        var query = DelayedOrdersQuery() with { Table = "Customers" };

        var result = Validator().Validate(query);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("not allowlisted", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Empty_select_fails()
    {
        var query = DelayedOrdersQuery() with { Select = [] };

        var result = Validator().Validate(query);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("Select must not be empty", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Disallowed_select_column_fails()
    {
        var query = DelayedOrdersQuery() with { Select = ["OrderId", "InternalCost"] };

        var result = Validator().Validate(query);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("Select column", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Disallowed_filter_column_fails()
    {
        var query = DelayedOrdersQuery() with
        {
            Filters =
            [
                new StructuredQueryFilter
                {
                    Column = "DelayReason",
                    Op = "eq",
                    Value = "Weather"
                }
            ]
        };

        var result = Validator().Validate(query);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("Filter column", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Disallowed_order_by_column_fails()
    {
        var query = DelayedOrdersQuery() with
        {
            OrderBy =
            [
                new StructuredQueryOrderBy
                {
                    Column = "Status",
                    Dir = "asc"
                }
            ]
        };

        var result = Validator().Validate(query);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("OrderBy column", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Disallowed_operator_fails()
    {
        var query = DelayedOrdersQuery() with
        {
            Filters =
            [
                new StructuredQueryFilter
                {
                    Column = "Status",
                    Op = "startsWith",
                    Value = "Del"
                }
            ]
        };

        var result = Validator().Validate(query);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("operator", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Between_without_value2_fails()
    {
        var query = DelayedOrdersQuery() with
        {
            Filters =
            [
                new StructuredQueryFilter
                {
                    Column = "ExpectedShipDateUtc",
                    Op = "between",
                    Value = "2026-01-01T00:00:00Z"
                }
            ]
        };

        var result = Validator().Validate(query);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("value2", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Contains_on_non_string_column_fails()
    {
        var query = DelayedOrdersQuery() with
        {
            Filters =
            [
                new StructuredQueryFilter
                {
                    Column = "ExpectedShipDateUtc",
                    Op = "contains",
                    Value = "2026"
                }
            ]
        };

        var result = Validator().Validate(query);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("string columns", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    public void Invalid_limit_fails(int? limit)
    {
        var query = DelayedOrdersQuery() with { Limit = limit };

        var result = Validator().Validate(query);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("Limit", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Compiler_generates_deterministic_sql_for_valid_delayed_orders_query()
    {
        var compiled = Compiler().Compile(DelayedOrdersQuery());

        Assert.Equal(
            "SELECT TOP (@p_limit) [OrderId], [Status], [ExpectedShipDateUtc], [ActualShipDateUtc], [Carrier] FROM dbo.Orders WHERE [Status] = @p0 AND [ExpectedShipDateUtc] BETWEEN @p1 AND @p2 ORDER BY [ExpectedShipDateUtc] DESC",
            compiled.SqlText);
        Assert.Equal("Delayed", compiled.Parameters["@p0"]);
        Assert.Equal("2026-01-01T00:00:00Z", compiled.Parameters["@p1"]);
        Assert.Equal("2026-01-31T23:59:59Z", compiled.Parameters["@p2"]);
        Assert.Equal(50, compiled.Parameters["@p_limit"]);
    }

    [Fact]
    public void Compiler_clamps_limit_to_max_limit()
    {
        var query = DelayedOrdersQuery() with { Limit = 500 };

        var compiled = Compiler().Compile(query);

        Assert.Equal(200, compiled.Parameters["@p_limit"]);
    }

    private static StructuredQuery DelayedOrdersQuery() =>
        new()
        {
            Table = "Orders",
            Select =
            [
                "OrderId",
                "Status",
                "ExpectedShipDateUtc",
                "ActualShipDateUtc",
                "Carrier"
            ],
            Filters =
            [
                new StructuredQueryFilter
                {
                    Column = "Status",
                    Op = "eq",
                    Value = "Delayed"
                },
                new StructuredQueryFilter
                {
                    Column = "ExpectedShipDateUtc",
                    Op = "between",
                    Value = "2026-01-01T00:00:00Z",
                    Value2 = "2026-01-31T23:59:59Z"
                }
            ],
            OrderBy =
            [
                new StructuredQueryOrderBy
                {
                    Column = "ExpectedShipDateUtc",
                    Dir = "desc"
                }
            ],
            Limit = 50
        };

    private static StructuredQueryValidator Validator() => new(Allowlist());

    private static StructuredQueryCompiler Compiler() => new(Allowlist());

    private static QueryAllowlist Allowlist() =>
        new()
        {
            Tables = new Dictionary<string, QueryAllowlistTable>(StringComparer.OrdinalIgnoreCase)
            {
                ["Orders"] = new QueryAllowlistTable
                {
                    Select =
                    [
                        "OrderId",
                        "CreatedAtUtc",
                        "Status",
                        "ExpectedShipDateUtc",
                        "ActualShipDateUtc",
                        "Carrier",
                        "DelayReason"
                    ],
                    Filter =
                    [
                        "Status",
                        "ExpectedShipDateUtc",
                        "Carrier"
                    ],
                    OrderBy =
                    [
                        "ExpectedShipDateUtc",
                        "CreatedAtUtc"
                    ],
                    ColumnTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["OrderId"] = "int",
                        ["CreatedAtUtc"] = "datetime",
                        ["Status"] = "string",
                        ["ExpectedShipDateUtc"] = "datetime",
                        ["ActualShipDateUtc"] = "datetime",
                        ["Carrier"] = "string",
                        ["DelayReason"] = "string"
                    }
                }
            },
            MaxLimit = 200,
            SingleTableOnly = true
        };

    private static string AllowlistPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var path = Path.Combine(
                directory.FullName,
                "src",
                "Nexus.OrchestratorApi",
                "Security",
                "allowlist.json");

            if (File.Exists(path))
            {
                return path;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not find Security/allowlist.json.");
    }
}
