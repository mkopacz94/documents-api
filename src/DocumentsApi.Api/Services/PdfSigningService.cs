using DocumentsApi.Api.Domain;
using DocumentsApi.Api.Options;
using DocumentsApi.Api.Pdf;
using Microsoft.Extensions.Options;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

namespace DocumentsApi.Api.Services;

public class PdfSigningService : IPdfSigningService
{
    private readonly PdfSignatureOptions _options;

    public PdfSigningService(IOptions<PdfSignatureOptions> options)
    {
        _options = options.Value;
    }

    public byte[] RenderSignatureTable(byte[] originalPdf, IReadOnlyList<SignatureRowInfo> rows)
    {
        using var inputStream = new MemoryStream(originalPdf);
        using var document = PdfReader.Open(inputStream, PdfDocumentOpenMode.Modify);

        if (document.PageCount == 0)
        {
            throw new InvalidOperationException("The PDF document has no pages to sign.");
        }

        var page = document.Pages[document.PageCount - 1];

        using (var gfx = XGraphics.FromPdfPage(page))
        {
            DrawTable(gfx, page, rows);
        }

        using var outputStream = new MemoryStream();
        document.Save(outputStream);
        return outputStream.ToArray();
    }

    private void DrawTable(XGraphics gfx, PdfPage page, IReadOnlyList<SignatureRowInfo> rows)
    {
        var headerFont = new XFont(EmbeddedFontResolver.FamilyName, _options.FontSize, XFontStyle.Bold);
        var cellFont = new XFont(EmbeddedFontResolver.FamilyName, _options.FontSize, XFontStyle.Regular);

        var tableWidth = page.Width.Point - _options.MarginLeft - _options.MarginRight;
        var columns = new (string Title, double Width)[]
        {
            ("Rodzaj podpisu", tableWidth * 0.3),
            ("Podpisano przez", tableWidth * 0.4),
            ("Data", tableWidth * 0.3),
        };

        var totalRows = rows.Count + 1; // header + one row per signature category
        var tableHeight = _options.RowHeight * totalRows;
        var top = page.Height.Point - _options.MarginBottom - tableHeight;
        var left = _options.MarginLeft;

        var y = top;
        DrawRow(gfx, columns, left, y, _options.RowHeight, headerFont,
            columns.Select(c => c.Title).ToArray());

        foreach (var row in rows)
        {
            y += _options.RowHeight;
            var signedAtText = row.SignedAtUtc is { } signedAt
                ? $"{signedAt:yyyy-MM-dd HH:mm} UTC"
                : string.Empty;
            DrawRow(gfx, columns, left, y, _options.RowHeight, cellFont,
                new[] { row.Category.DisplayName(), row.SignedBy ?? string.Empty, signedAtText });
        }
    }

    private static void DrawRow(
        XGraphics gfx,
        (string Title, double Width)[] columns,
        double left,
        double y,
        double rowHeight,
        XFont font,
        IReadOnlyList<string> cellValues)
    {
        var x = left;
        for (var i = 0; i < columns.Length; i++)
        {
            var width = columns[i].Width;
            var rect = new XRect(x, y, width, rowHeight);
            gfx.DrawRectangle(XPens.Black, rect);
            gfx.DrawString(cellValues[i], font, XBrushes.Black,
                new XRect(x + 4, y, width - 8, rowHeight), XStringFormats.CenterLeft);
            x += width;
        }
    }
}
