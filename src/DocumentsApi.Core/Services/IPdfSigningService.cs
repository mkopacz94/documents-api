namespace DocumentsApi.Core.Services;

public interface IPdfSigningService
{
    /// <summary>
    /// Renders the full signature table (Opracował/Sprawdził/Zatwierdził rows,
    /// blank where not yet signed) onto a copy of the original PDF's last
    /// page and returns the resulting document bytes. Always operates on the
    /// original, unmodified PDF so re-rendering after each new signature
    /// never accumulates stale table artwork.
    /// </summary>
    byte[] RenderSignatureTable(byte[] originalPdf, IReadOnlyList<SignatureRowInfo> rows);
}
