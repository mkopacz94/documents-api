namespace DocumentsApi.Core.Options;

/// <summary>
/// Bound from the "DocumentUpload" configuration section. This API doesn't
/// persist file bytes anywhere - the caller round-trips the PDF at each
/// stage - so there's no storage location to configure, only an upload
/// size limit.
/// </summary>
public class DocumentUploadOptions
{
    public const string SectionName = "DocumentUpload";

    public long MaxFileSizeBytes { get; set; } = 50 * 1024 * 1024;

    /// <summary>
    /// Maximum number of files accepted in one batch-sign request. Each file
    /// is still processed (and held in memory) one at a time, but this
    /// bounds how many can be queued into a single request.
    /// </summary>
    public int MaxBatchSize { get; set; } = 20;
}
