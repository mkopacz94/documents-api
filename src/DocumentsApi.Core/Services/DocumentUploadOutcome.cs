namespace DocumentsApi.Core.Services;

/// <summary>
/// Result of <see cref="IDocumentProcessingService.PrepareForSigning"/>. Only
/// has one failure mode (the PDF couldn't be rendered), so unlike
/// <see cref="SigningOutcome"/> there's no reason to enumerate - the caller
/// always maps a failure to the same 400 FILE_PROCESSING_FAILED response.
/// </summary>
public sealed record DocumentUploadOutcome
{
    public bool IsValid { get; private init; }

    public string? ErrorMessage { get; private init; }

    public byte[]? RenderedBytes { get; private init; }

    public static DocumentUploadOutcome Success(byte[] renderedBytes) =>
        new() { IsValid = true, RenderedBytes = renderedBytes };

    public static DocumentUploadOutcome Failure(string errorMessage) =>
        new() { IsValid = false, ErrorMessage = errorMessage };
}
