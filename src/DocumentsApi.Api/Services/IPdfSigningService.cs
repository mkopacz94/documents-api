namespace DocumentsApi.Api.Services;

public interface IPdfSigningService
{
    /// <summary>
    /// Stamps the signer's username and the signing date/time onto the PDF
    /// and returns the resulting document bytes.
    /// </summary>
    byte[] Sign(byte[] sourcePdf, string signedBy, DateTime signedAtUtc);
}
