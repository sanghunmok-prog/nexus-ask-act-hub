using System.Text.Json;
using Nexus.Contracts;
using Nexus.Mcp.Toolbelt.Tools;

namespace Nexus.Mcp.Toolbelt.Tests;

public sealed class DbSchemaSummaryToolTests
{
    private static readonly string[] OrdersSelectColumns =
    [
        "OrderId",
        "CreatedAtUtc",
        "Status",
        "ExpectedShipDateUtc",
        "ActualShipDateUtc",
        "Carrier",
        "DelayReason"
    ];

    [Fact]
    public async Task Allowlist_file_can_be_loaded_by_toolbelt()
    {
        var summary = await Tool().GetSchemaSummaryAsync();

        Assert.NotNull(summary);
        Assert.NotNull(summary.Tables);
    }

    [Fact]
    public async Task Schema_summary_contains_exactly_one_table()
    {
        var summary = await Tool().GetSchemaSummaryAsync();

        Assert.Single(summary.Tables);
    }

    [Fact]
    public async Task Schema_summary_contains_orders_table()
    {
        var summary = await Tool().GetSchemaSummaryAsync();

        Assert.Equal("Orders", summary.Tables.Single().Name);
    }

    [Fact]
    public async Task Orders_columns_match_allowlisted_select_columns_exactly_and_in_order()
    {
        var summary = await Tool().GetSchemaSummaryAsync();

        Assert.Equal(OrdersSelectColumns, summary.Tables.Single().Columns);
    }

    [Fact]
    public async Task Output_shape_is_deterministic()
    {
        var first = await Tool().GetSchemaSummaryAsync();
        var second = await Tool().GetSchemaSummaryAsync();

        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
    }

    [Fact]
    public async Task No_extra_non_allowlisted_columns_are_returned()
    {
        var summary = await Tool().GetSchemaSummaryAsync();
        var columns = summary.Tables.Single().Columns;

        Assert.DoesNotContain("InternalCost", columns);
        Assert.DoesNotContain("CustomerEmail", columns);
        Assert.DoesNotContain("ParamsJson", columns);
    }

    [Fact]
    public void Response_dtos_from_contracts_can_be_created_and_used()
    {
        var response = new DbSchemaSummaryResponse
        {
            Tables =
            [
                new DbSchemaTableSummary
                {
                    Name = "Orders",
                    Columns = OrdersSelectColumns
                }
            ]
        };

        Assert.Equal("Orders", response.Tables.Single().Name);
        Assert.Equal(OrdersSelectColumns, response.Tables.Single().Columns);
    }

    private static DbSchemaSummaryTool Tool() =>
        new(Path.Combine(AppContext.BaseDirectory, "Security", "allowlist.json"));
}
