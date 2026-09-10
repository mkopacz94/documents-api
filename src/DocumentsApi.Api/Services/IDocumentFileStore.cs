namespace DocumentsApi.Api.Services;

/// <summary>
/// Persists a document's bytes. Two copies are kept per document:
/// - "original": the untouched upload, used as the render input at each
///   signing stage (re-rendering the full table from this every time avoids
///   ever drawing a duplicate table on top of a previous one).
/// - "current": the exact bytes produced by the most recent successful
///   render, persisted verbatim. This must be served as-is (never
///   re-rendered on read) because PdfSharpCore's Save() is not
///   byte-deterministic across calls (it embeds a fresh trailer /ID and
///   ModDate each time) - re-deriving it on demand would produce bytes that
///   no longer match the hash recorded in the database at sign time.
/// </summary>
public interface IDocumentFileStore
{
    Task SaveOriginalAsync(int documentId, byte[] content, CancellationToken cancellationToken = default);

    Task<byte[]> ReadOriginalAsync(int documentId, CancellationToken cancellationToken = default);

    Task SaveCurrentAsync(int documentId, byte[] content, CancellationToken cancellationToken = default);

    Task<byte[]> ReadCurrentAsync(int documentId, CancellationToken cancellationToken = default);
}
