namespace Nexus.OrchestratorApi.Documents;

public interface IDocumentIngestionRepository
{
    Task InsertAsync(DocumentIngestionRecord document, CancellationToken cancellationToken = default);
}
