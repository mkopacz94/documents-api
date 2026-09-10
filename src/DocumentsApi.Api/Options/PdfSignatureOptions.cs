namespace DocumentsApi.Api.Options;

/// <summary>
/// Configures where and how the signature stamp is drawn onto a PDF.
/// Bound from the "PdfSignature" configuration section.
/// </summary>
public class PdfSignatureOptions
{
    public const string SectionName = "PdfSignature";

    /// <summary>
    /// 1-based page number to stamp. A value less than or equal to 0 means "last page".
    /// </summary>
    public int PageNumber { get; set; } = 0;

    public double FontSize { get; set; } = 10;

    public double MarginRight { get; set; } = 40;

    public double MarginBottom { get; set; } = 30;
}
