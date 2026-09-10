using DocumentsApi.Api.Options;
using DocumentsApi.Api.Pdf;
using Microsoft.Extensions.Options;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf.IO;

namespace DocumentsApi.Api.Services;

public class PdfSigningService : IPdfSigningService
{
    private readonly PdfSignatureOptions _options;

    public PdfSigningService(IOptions<PdfSignatureOptions> options)
    {
        _options = options.Value;
    }

    public byte[] Sign(byte[] sourcePdf, string signedBy, DateTime signedAtUtc)
    {
        using var inputStream = new MemoryStream(sourcePdf);
        using var document = PdfReader.Open(inputStream, PdfDocumentOpenMode.Modify);

        if (document.PageCount == 0)
        {
            throw new InvalidOperationException("The PDF document has no pages to sign.");
        }

        var pageIndex = _options.PageNumber > 0
            ? Math.Min(_options.PageNumber, document.PageCount) - 1
            : document.PageCount - 1;

        var page = document.Pages[pageIndex];

        using (var gfx = XGraphics.FromPdfPage(page))
        {
            var font = new XFont(EmbeddedFontResolver.FamilyName, _options.FontSize, XFontStyle.Regular);
            var text = $"Signed by {signedBy} on {signedAtUtc:yyyy-MM-dd HH:mm} UTC";

            var textSize = gfx.MeasureString(text, font);
            var x = page.Width.Point - textSize.Width - _options.MarginRight;
            var y = page.Height.Point - _options.MarginBottom;

            gfx.DrawString(text, font, XBrushes.Black, new XPoint(Math.Max(x, 0), y));
        }

        using var outputStream = new MemoryStream();
        document.Save(outputStream);
        return outputStream.ToArray();
    }
}
