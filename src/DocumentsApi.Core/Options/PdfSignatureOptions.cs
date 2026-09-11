namespace DocumentsApi.Core.Options;

/// <summary>
/// Configures how the signature table is drawn onto a document's last page.
/// Bound from the "PdfSignature" configuration section.
/// </summary>
public class PdfSignatureOptions
{
    public const string SectionName = "PdfSignature";

    public double FontSize { get; set; } = 9;

    public double RowHeight { get; set; } = 18;

    public double MarginLeft { get; set; } = 40;

    public double MarginRight { get; set; } = 40;

    public double MarginBottom { get; set; } = 30;
}
