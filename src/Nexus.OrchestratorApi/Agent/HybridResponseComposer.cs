using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Nexus.OrchestratorApi.Agent;

public sealed class HybridResponseComposer
{
    private const int MaxTableRows = 10;
    private const int MaxPolicyExcerptLength = 600;

    public HybridResponseOutput Compose(HybridResponseInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var sqlRows = ReadRows(input.DbQueryReadonlyResult).ToArray();
        var documentResults = ReadDocumentResults(input.DocsSearchResult).ToArray();
        var chunk = ReadChunk(input.DocsGetChunkResult);
        var topDocument = chunk ?? documentResults.FirstOrDefault();
        IReadOnlyList<CitationSummary> citations = topDocument is null
            ? []
            : [topDocument.ToCitationSummary()];

        var message = BuildMessage(sqlRows, documentResults, chunk, topDocument, input.DocsGetChunkUnavailable);

        return new HybridResponseOutput
        {
            Message = message,
            Citations = citations,
            Summary = new HybridResponseSummary
            {
                SqlRowCount = ReadRowCount(input.DbQueryReadonlyResult, sqlRows.Length),
                DocumentResultCount = ReadDocumentResultCount(input.DocsSearchResult, documentResults.Length),
                CitationCount = citations.Count
            }
        };
    }

    private static string BuildMessage(
        IReadOnlyList<OrderRow> sqlRows,
        IReadOnlyList<DocumentReference> documentResults,
        DocumentReference? chunk,
        DocumentReference? topDocument,
        bool docsGetChunkUnavailable)
    {
        var builder = new StringBuilder();

        builder.AppendLine("## Delayed orders");
        if (sqlRows.Count == 0)
        {
            builder.AppendLine("No delayed orders were returned by the current demo query.");
        }
        else
        {
            builder.AppendLine($"{sqlRows.Count.ToString(CultureInfo.InvariantCulture)} delayed orders were returned by the current demo query.");
            builder.AppendLine();
            builder.AppendLine("| OrderId | Status | Carrier | Expected ship date | Actual ship date | Delay reason |");
            builder.AppendLine("| --- | --- | --- | --- | --- | --- |");

            foreach (var row in sqlRows.Take(MaxTableRows))
            {
                builder
                    .Append("| ")
                    .Append(MarkdownCell(row.OrderId))
                    .Append(" | ")
                    .Append(MarkdownCell(row.Status))
                    .Append(" | ")
                    .Append(MarkdownCell(row.Carrier))
                    .Append(" | ")
                    .Append(MarkdownCell(row.ExpectedShipDateUtc))
                    .Append(" | ")
                    .Append(MarkdownCell(row.ActualShipDateUtc))
                    .Append(" | ")
                    .Append(MarkdownCell(row.DelayReason))
                    .AppendLine(" |");
            }

            if (sqlRows.Count > MaxTableRows)
            {
                builder.AppendLine();
                builder.AppendLine($"Showing the first {MaxTableRows.ToString(CultureInfo.InvariantCulture)} rows.");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Relevant policy");

        if (topDocument is null || string.IsNullOrWhiteSpace(topDocument.Excerpt))
        {
            builder.AppendLine("No relevant policy document was found.");
        }
        else
        {
            builder.AppendLine(CapExcerpt(topDocument.Excerpt));

            if (chunk is null && docsGetChunkUnavailable)
            {
                builder.AppendLine();
                builder.AppendLine("Note: full citation text was unavailable, so this excerpt uses the document search snippet.");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Citations");

        if (topDocument is null)
        {
            builder.AppendLine("No citations available.");
        }
        else
        {
            builder
                .Append("[1] ")
                .Append(PlainText(topDocument.Title))
                .Append(" — ")
                .Append(PlainText(topDocument.SourceName))
                .Append(" — chunk ")
                .Append(topDocument.ChunkIndex?.ToString(CultureInfo.InvariantCulture) ?? "—")
                .AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private static IEnumerable<OrderRow> ReadRows(JsonElement? dbQueryResult)
    {
        if (dbQueryResult is null ||
            !TryGetProperty(dbQueryResult.Value, "rows", out var rows) ||
            rows.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            yield return new OrderRow(
                ReadCell(row, "OrderId"),
                ReadCell(row, "Status"),
                ReadCell(row, "Carrier"),
                ReadCell(row, "ExpectedShipDateUtc"),
                ReadCell(row, "ActualShipDateUtc"),
                ReadCell(row, "DelayReason"));
        }
    }

    private static IEnumerable<DocumentReference> ReadDocumentResults(JsonElement? docsSearchResult)
    {
        if (docsSearchResult is null ||
            !TryGetProperty(docsSearchResult.Value, "results", out var results) ||
            results.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var result in results.EnumerateArray())
        {
            if (result.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            yield return new DocumentReference(
                ReadString(result, "citationId"),
                ReadString(result, "title"),
                ReadString(result, "sourceName"),
                ReadNullableInt(result, "chunkIndex"),
                ReadString(result, "snippet"));
        }
    }

    private static DocumentReference? ReadChunk(JsonElement? docsGetChunkResult)
    {
        if (docsGetChunkResult is null || docsGetChunkResult.Value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var chunk = docsGetChunkResult.Value;
        var chunkText = ReadString(chunk, "chunkText");
        if (string.IsNullOrWhiteSpace(chunkText))
        {
            return null;
        }

        return new DocumentReference(
            ReadString(chunk, "citationId"),
            ReadString(chunk, "title"),
            ReadString(chunk, "sourceName"),
            ReadNullableInt(chunk, "chunkIndex"),
            chunkText);
    }

    private static int ReadRowCount(JsonElement? dbQueryResult, int fallback)
    {
        if (dbQueryResult is not null &&
            TryGetProperty(dbQueryResult.Value, "rowCount", out var rowCount) &&
            rowCount.TryGetInt32(out var count))
        {
            return count;
        }

        return fallback;
    }

    private static int ReadDocumentResultCount(JsonElement? docsSearchResult, int fallback)
    {
        if (docsSearchResult is not null &&
            TryGetProperty(docsSearchResult.Value, "resultCount", out var resultCount) &&
            resultCount.TryGetInt32(out var count))
        {
            return count;
        }

        return fallback;
    }

    private static string ReadCell(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var property) ||
            property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return string.Empty;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString() ?? string.Empty,
            JsonValueKind.Number => property.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => property.GetRawText()
        };
    }

    private static string ReadString(JsonElement element, string propertyName) =>
        TryGetProperty(element, propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;

    private static int? ReadNullableInt(JsonElement element, string propertyName) =>
        TryGetProperty(element, propertyName, out var property) && property.TryGetInt32(out var value)
            ? value
            : null;

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement property)
    {
        if (element.TryGetProperty(propertyName, out property))
        {
            return true;
        }

        if (propertyName.Length == 0)
        {
            return false;
        }

        var camelCaseName = char.ToLowerInvariant(propertyName[0]) + propertyName[1..];
        return element.TryGetProperty(camelCaseName, out property);
    }

    private static string MarkdownCell(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "—";
        }

        return value.Replace("|", "\\|", StringComparison.Ordinal).ReplaceLineEndings(" ");
    }

    private static string PlainText(string value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value.ReplaceLineEndings(" ").Trim();

    private static string CapExcerpt(string value)
    {
        var excerpt = value.ReplaceLineEndings(" ").Trim();
        if (excerpt.Length <= MaxPolicyExcerptLength)
        {
            return excerpt;
        }

        return excerpt[..MaxPolicyExcerptLength].TrimEnd() + "...";
    }

    private sealed record OrderRow(
        string OrderId,
        string Status,
        string Carrier,
        string ExpectedShipDateUtc,
        string ActualShipDateUtc,
        string DelayReason);

    private sealed record DocumentReference(
        string CitationId,
        string Title,
        string SourceName,
        int? ChunkIndex,
        string Excerpt)
    {
        public CitationSummary ToCitationSummary() =>
            new()
            {
                CitationId = CitationId,
                Title = Title,
                SourceName = SourceName,
                ChunkIndex = ChunkIndex
            };
    }
}

public sealed record HybridResponseInput
{
    public JsonElement? DocsSearchResult { get; init; }

    public JsonElement? DocsGetChunkResult { get; init; }

    public JsonElement? DbSchemaSummaryResult { get; init; }

    public JsonElement? DbQueryReadonlyResult { get; init; }

    public bool DocsGetChunkUnavailable { get; init; }
}

public sealed record HybridResponseOutput
{
    public required string Message { get; init; }

    public IReadOnlyList<CitationSummary> Citations { get; init; } = [];

    public required HybridResponseSummary Summary { get; init; }
}

public sealed record CitationSummary
{
    public string CitationId { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string SourceName { get; init; } = string.Empty;

    public int? ChunkIndex { get; init; }
}

public sealed record HybridResponseSummary
{
    public int SqlRowCount { get; init; }

    public int DocumentResultCount { get; init; }

    public int CitationCount { get; init; }
}
