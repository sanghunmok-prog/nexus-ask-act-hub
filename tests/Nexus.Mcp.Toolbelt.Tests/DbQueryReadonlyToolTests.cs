using Microsoft.AspNetCore.Http;
using Nexus.Contracts;
using Nexus.Mcp.Toolbelt.Tools;
using Nexus.QuerySafety;

namespace Nexus.Mcp.Toolbelt.Tests;

public sealed class DbQueryReadonlyToolTests
{
    [Fact]
    public async Task Valid_structured_query_is_compiled_and_passed_to_executor()
    {
        var executor = new FakeReadonlyQueryExecutor();
        var result = await Tool(executor).QueryAsync(DelayedOrdersQuery());

        Assert.True(result.Succeeded);
        Assert.NotNull(executor.LastQuery);
        Assert.Equal(
            "SELECT TOP (@p_limit) [OrderId], [Status], [ExpectedShipDateUtc], [ActualShipDateUtc], [Carrier] FROM dbo.Orders WHERE [Status] = @p0 ORDER BY [ExpectedShipDateUtc] DESC",
            executor.LastQuery.SqlText);
        Assert.Equal("Delayed", executor.LastQuery.Parameters["@p0"]);
        Assert.Equal(5, executor.LastQuery.Parameters["@p_limit"]);
        Assert.Equal(DelayedOrdersQuery().Select, executor.LastSelectedColumns);
    }

    [Fact]
    public async Task Invalid_structured_query_fails_before_executor_is_invoked()
    {
        var executor = new FakeReadonlyQueryExecutor();
        var query = DelayedOrdersQuery() with { Limit = 0 };

        var result = await Tool(executor).QueryAsync(query);

        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
        Assert.Null(executor.LastQuery);
        Assert.Equal("QUERY_VALIDATION_FAILED", result.Error?.Code);
    }

    [Fact]
    public async Task Disallowed_select_column_returns_validation_failed_error()
    {
        var executor = new FakeReadonlyQueryExecutor();
        var query = DelayedOrdersQuery() with { Select = ["OrderId", "InternalCost"] };

        var result = await Tool(executor).QueryAsync(query);

        Assert.False(result.Succeeded);
        Assert.Equal("QUERY_VALIDATION_FAILED", result.Error?.Code);
        Assert.Equal("StructuredQuery failed validation.", result.Error?.Message);
        Assert.Contains(
            "Select column 'InternalCost' is not allowlisted.",
            result.Error?.Errors ?? []);
        Assert.Null(executor.LastQuery);
    }

    [Fact]
    public async Task Response_row_count_equals_returned_rows_count()
    {
        var executor = new FakeReadonlyQueryExecutor
        {
            Rows =
            [
                Row(("OrderId", 11), ("Status", "Delayed")),
                Row(("OrderId", 12), ("Status", "Delayed"))
            ]
        };

        var result = await Tool(executor).QueryAsync(DelayedOrdersQuery());

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Response?.RowCount);
    }

    [Fact]
    public async Task Rows_are_returned_as_json_friendly_column_keyed_objects()
    {
        var expectedShipDateUtc = new DateTime(2026, 1, 20, 17, 0, 0);
        var executor = new FakeReadonlyQueryExecutor
        {
            Rows =
            [
                Row(
                    ("OrderId", 11),
                    ("Status", "Delayed"),
                    ("ExpectedShipDateUtc", expectedShipDateUtc),
                    ("ActualShipDateUtc", null),
                    ("Carrier", "USPS"))
            ]
        };

        var result = await Tool(executor).QueryAsync(DelayedOrdersQuery());

        var row = Assert.Single(result.Response?.Rows ?? []);
        Assert.Equal(11, row["OrderId"]);
        Assert.Equal("Delayed", row["Status"]);
        Assert.Equal(expectedShipDateUtc, row["ExpectedShipDateUtc"]);
        Assert.Null(row["ActualShipDateUtc"]);
        Assert.Equal("USPS", row["Carrier"]);
    }

    private static DbQueryReadonlyTool Tool(FakeReadonlyQueryExecutor executor) =>
        new(executor, Path.Combine(AppContext.BaseDirectory, "Security", "allowlist.json"));

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
            Limit = 5
        };

    private static IReadOnlyDictionary<string, object?> Row(params (string Column, object? Value)[] values) =>
        values.ToDictionary(value => value.Column, value => value.Value, StringComparer.Ordinal);

    private sealed class FakeReadonlyQueryExecutor : IReadonlyQueryExecutor
    {
        public CompiledSqlQuery? LastQuery { get; private set; }

        public IReadOnlyList<string>? LastSelectedColumns { get; private set; }

        public IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows { get; init; } = [];

        public Task<DbQueryReadonlyResponse> ExecuteAsync(
            CompiledSqlQuery query,
            IReadOnlyList<string> selectedColumns,
            CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            LastSelectedColumns = selectedColumns;

            return Task.FromResult(new DbQueryReadonlyResponse
            {
                RowCount = Rows.Count,
                Rows = Rows
            });
        }
    }
}
