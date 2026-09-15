using DocumentsApi.Core.Domain;

namespace DocumentsApi.Core.Pdf;

public interface IPdfSigningService
{
    /// <summary>
    /// Appends a new blank page to a copy of the uploaded PDF and draws the
    /// blank three-row signature table (category labels only, no signer/date
    /// yet) at the bottom of it - the table never shares a page with the
    /// document's own content. Called once, at upload time.
    /// </summary>
    byte[] RenderSignatureTable(byte[] originalPdf, IReadOnlyList<SignatureRowInfo> rows);

    /// <summary>
    /// Fills in the signer/date cells for one category's row, on the
    /// signature page appended by <see cref="RenderSignatureTable"/>, on a
    /// copy of the PDF the caller currently holds (i.e. whatever this API
    /// returned from the previous upload/sign call). Only that row's two
    /// blank cells are drawn - borders and other rows are left untouched,
    /// since the API never stores the file itself and has nothing else to
    /// redraw from.
    /// </summary>
    byte[] FillSignatureRow(byte[] currentPdf, SignatureCategory category, string signedBy, DateTime signedAtUtc);
}
