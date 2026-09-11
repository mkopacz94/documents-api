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
}
