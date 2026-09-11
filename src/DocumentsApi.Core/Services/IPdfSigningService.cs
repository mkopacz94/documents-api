using DocumentsApi.Core.Domain;

namespace DocumentsApi.Core.Services;

public interface IPdfSigningService
{
    /// <summary>
    /// Draws the blank three-row signature table (category labels only, no
    /// signer/date yet) onto a copy of the uploaded PDF's last page. Called
    /// once, at upload time.
    /// </summary>
    byte[] RenderSignatureTable(byte[] originalPdf, IReadOnlyList<SignatureRowInfo> rows);

    /// <summary>
    /// Fills in the signer/date cells for one category's row on a copy of
    /// the PDF the caller currently holds (i.e. whatever this API returned
    /// from the previous upload/sign call). Only that row's two blank cells
    /// are drawn - borders and other rows are left untouched, since the API
    /// never stores the file itself and has nothing else to redraw from.
    /// </summary>
    byte[] FillSignatureRow(byte[] currentPdf, SignatureCategory category, string signedBy, DateTime signedAtUtc);
}
