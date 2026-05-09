using System.Text.Json;
using Nexus.OrchestratorApi.Agent;

namespace Nexus.OrchestratorApi.Tests;

public sealed class HybridResponseComposerTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Compose_creates_delayed_orders_table_policy_excerpt_citations_and_summary()
    {
        var composer = new HybridResponseComposer();

        var output = composer.Compose(new HybridResponseInput
        {
            DocsSearchResult = Json(new
            {
                resultCount = 1,
                results = new[]
                {
                    new
                    {
                        citationId = "doc-1:0",
                        chunkIndex = 0,
                        title = "Shipping Delay Policy",
                        sourceName = "nexus-shipping-policy.md",
                        snippet = "Search snippet should not win when chunk text is available."
                    }
                }
            }),
            DocsGetChunkResult = Json(new
            {
                citationId = "doc-1:0",
                chunkIndex = 0,
                title = "Shipping Delay Policy",
                sourceName = "nexus-shipping-policy.md",
                chunkText = "Escalate delayed shipments when the carrier delay reason indicates a policy threshold has been reached."
            }),
            DbQueryReadonlyResult = Json(new
            {
                rowCount = 2,
                rows = new object[]
                {
                    new
                    {
                        OrderId = 11,
                        Status = "Delayed",
                        Carrier = "USPS",
                        ExpectedShipDateUtc = "2026-02-01T00:00:00Z",
                        ActualShipDateUtc = (string?)null,
                        DelayReason = "Carrier exception"
                    },
                    new
                    {
                        OrderId = 12,
                        Status = "Delayed",
                        Carrier = "UPS",
                        ExpectedShipDateUtc = "2026-02-02T00:00:00Z",
                        ActualShipDateUtc = "2026-02-04T00:00:00Z",
                        DelayReason = "Weather"
                    }
                }
            })
        });

        Assert.Contains("## Delayed orders", output.Message);
        Assert.Contains("2 delayed orders were returned by the current demo query.", output.Message);
        Assert.Contains("| OrderId | Status | Carrier | Expected ship date | Actual ship date | Delay reason |", output.Message);
        Assert.Contains("| 11 | Delayed | USPS | 2026-02-01T00:00:00Z | — | Carrier exception |", output.Message);
        Assert.Contains("## Relevant policy", output.Message);
        Assert.Contains("Escalate delayed shipments", output.Message);
        Assert.Contains("[1] Shipping Delay Policy — nexus-shipping-policy.md — chunk 0", output.Message);
        Assert.DoesNotContain("Hybrid answer composition will be added in PR-12", output.Message);
        Assert.DoesNotContain("last 30 days", output.Message, StringComparison.OrdinalIgnoreCase);

        var citation = Assert.Single(output.Citations);
        Assert.Equal("doc-1:0", citation.CitationId);
        Assert.Equal("Shipping Delay Policy", citation.Title);
        Assert.Equal("nexus-shipping-policy.md", citation.SourceName);
        Assert.Equal(0, citation.ChunkIndex);
        Assert.Equal(2, output.Summary.SqlRowCount);
        Assert.Equal(1, output.Summary.DocumentResultCount);
        Assert.Equal(1, output.Summary.CitationCount);
    }

    [Fact]
    public void Compose_handles_no_sql_rows()
    {
        var output = new HybridResponseComposer().Compose(new HybridResponseInput
        {
            DocsSearchResult = Json(new
            {
                resultCount = 1,
                results = new[]
                {
                    new
                    {
                        citationId = "doc-1:0",
                        chunkIndex = 0,
                        title = "Shipping Delay Policy",
                        sourceName = "nexus-shipping-policy.md",
                        snippet = "Policy snippet."
                    }
                }
            }),
            DbQueryReadonlyResult = Json(new
            {
                rowCount = 0,
                rows = Array.Empty<object>()
            })
        });

        Assert.Contains("No delayed orders were returned by the current demo query.", output.Message);
        Assert.Equal(0, output.Summary.SqlRowCount);
        Assert.Equal(1, output.Summary.CitationCount);
    }

    [Fact]
    public void Compose_handles_no_document_result()
    {
        var output = new HybridResponseComposer().Compose(new HybridResponseInput
        {
            DocsSearchResult = Json(new
            {
                resultCount = 0,
                results = Array.Empty<object>()
            }),
            DbQueryReadonlyResult = Json(new
            {
                rowCount = 1,
                rows = new[]
                {
                    new
                    {
                        OrderId = 11,
                        Status = "Delayed"
                    }
                }
            })
        });

        Assert.Contains("No relevant policy document was found.", output.Message);
        Assert.Contains("No citations available.", output.Message);
        Assert.Empty(output.Citations);
        Assert.Equal(0, output.Summary.DocumentResultCount);
        Assert.Equal(0, output.Summary.CitationCount);
    }

    [Fact]
    public void Compose_falls_back_to_docs_search_snippet_when_get_chunk_is_unavailable()
    {
        var output = new HybridResponseComposer().Compose(new HybridResponseInput
        {
            DocsGetChunkUnavailable = true,
            DocsSearchResult = Json(new
            {
                resultCount = 1,
                results = new[]
                {
                    new
                    {
                        citationId = "doc-1:0",
                        chunkIndex = 0,
                        title = "Shipping Delay Policy",
                        sourceName = "nexus-shipping-policy.md",
                        snippet = "Use this search snippet as the fallback policy excerpt."
                    }
                }
            }),
            DbQueryReadonlyResult = Json(new
            {
                rowCount = 1,
                rows = new[]
                {
                    new
                    {
                        OrderId = 11,
                        Status = "Delayed",
                        ActualShipDateUtc = (string?)null,
                        DelayReason = (string?)null
                    }
                }
            })
        });

        Assert.Contains("Use this search snippet as the fallback policy excerpt.", output.Message);
        Assert.Contains("full citation text was unavailable", output.Message);
        Assert.Contains("| 11 | Delayed | — | — | — | — |", output.Message);
    }

    private static JsonElement Json(object value) =>
        JsonSerializer.SerializeToElement(value, JsonOptions);
}
