namespace DocumentsApi.Api.Options;

/// <summary>
/// Bound from the "DocumentStorage" configuration section.
/// </summary>
public class DocumentStorageOptions
{
    public const string SectionName = "DocumentStorage";

    public long MaxFileSizeBytes { get; set; } = 25 * 1024 * 1024;

    /// <summary>
    /// Where original uploaded PDFs are kept. Relative paths are resolved
    /// against the content root. Category-based routing (per the "second
    /// stage" requirement) is not implemented yet - everything lands here.
    /// </summary>
    public string BasePath { get; set; } = "App_Data/documents";
}
