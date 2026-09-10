using DocumentsApi.Api.Options;
using Microsoft.Extensions.Options;

namespace DocumentsApi.Api.Services;

/// <summary>
/// Stores original documents as plain files on local disk. Category-based
/// folder routing (the "second stage" requirement) is not implemented here -
/// every document lands under the same base path, keyed by its database id.
/// </summary>
public class LocalDiskDocumentFileStore : IDocumentFileStore
{
    private readonly string _basePath;

    public LocalDiskDocumentFileStore(IOptions<DocumentStorageOptions> options, IWebHostEnvironment environment)
    {
        _basePath = Path.IsPathRooted(options.Value.BasePath)
            ? options.Value.BasePath
            : Path.Combine(environment.ContentRootPath, options.Value.BasePath);
        Directory.CreateDirectory(_basePath);
    }

    public Task SaveOriginalAsync(int documentId, byte[] content, CancellationToken cancellationToken = default)
        => File.WriteAllBytesAsync(GetPath(documentId, "original"), content, cancellationToken);

    public Task<byte[]> ReadOriginalAsync(int documentId, CancellationToken cancellationToken = default)
        => File.ReadAllBytesAsync(GetPath(documentId, "original"), cancellationToken);

    public Task SaveCurrentAsync(int documentId, byte[] content, CancellationToken cancellationToken = default)
        => File.WriteAllBytesAsync(GetPath(documentId, "current"), content, cancellationToken);

    public Task<byte[]> ReadCurrentAsync(int documentId, CancellationToken cancellationToken = default)
        => File.ReadAllBytesAsync(GetPath(documentId, "current"), cancellationToken);

    private string GetPath(int documentId, string variant) => Path.Combine(_basePath, $"{documentId}.{variant}.pdf");
}
