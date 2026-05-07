using System.Text;
using UglyToad.PdfPig;

namespace Nexus.OrchestratorApi.Documents;

public sealed class DocumentTextExtractor
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt",
        ".md",
        ".pdf"
    };

    public bool IsSupportedExtension(string fileName) =>
        SupportedExtensions.Contains(Path.GetExtension(fileName));

    public async Task<string> ExtractAsync(
        string fileName,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(fileName);

        if (string.Equals(extension, ".txt", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase))
        {
            using var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            return await reader.ReadToEndAsync(cancellationToken);
        }

        if (string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return ExtractPdfText(content);
        }

        throw new NotSupportedException("Document type is not supported.");
    }

    private static string ExtractPdfText(Stream content)
    {
        using var document = PdfDocument.Open(content);
        var builder = new StringBuilder();

        foreach (var page in document.GetPages())
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(page.Text);
        }

        return builder.ToString();
    }
}
