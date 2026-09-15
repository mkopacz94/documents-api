using DocumentsApi.Core.Domain;
using DocumentsApi.Core.Options;
using Microsoft.Extensions.Options;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

namespace DocumentsApi.Core.Pdf;

public class PdfSigningService : IPdfSigningService
{
    private readonly PdfSignatureOptions _options;

    public PdfSigningService(IOptions<PdfSignatureOptions> options)
    {
        _options = options.Value;
    }

    public byte[] RenderSignatureTable(byte[] originalPdf, IReadOnlyList<SignatureRowInfo> rows)
    {
        return WithNewBlankPage(originalPdf, (gfx, page) =>
        {
            var layout = GetLayout(page, rows.Count);
            var headerFont = new XFont(EmbeddedFontResolver.FamilyName, _options.FontSize, XFontStyle.Bold);
            var cellFont = new XFont(EmbeddedFontResolver.FamilyName, _options.FontSize, XFontStyle.Regular);

            DrawRow(gfx, layout, 0, headerFont, layout.Columns.Select(c => c.Title).ToArray());

            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var signedAtText = row.SignedAtUtc is { } signedAt ? $"{signedAt:yyyy-MM-dd HH:mm} UTC" : string.Empty;
                DrawRow(gfx, layout, i + 1, cellFont, new[] { row.Category.DisplayName(), row.SignedBy ?? string.Empty, signedAtText });
            }
        });
    }

    public byte[] FillSignatureRow(byte[] currentPdf, SignatureCategory category, string signedBy, DateTime signedAtUtc)
    {
        return WithLastPage(currentPdf, (gfx, page) =>
        {
            // The signature page was appended once, at upload time (see
            // WithNewBlankPage), so it's already the document's last page and
            // stays that way - no page is ever added here. Table geometry is
            // fixed too (every category's blank row and every cell border are
            // already burned into currentPdf), so this only fills in the two
            // cells for one row - it never redraws borders or other rows.
            var layout = GetLayout(page, SignatureCategoryExtensions.Sequence.Count);
            var font = new XFont(EmbeddedFontResolver.FamilyName, _options.FontSize, XFontStyle.Regular);
            var rowIndex = 1 + SignatureCategoryExtensions.Sequence.ToList().IndexOf(category);
            var signedAtText = $"{signedAtUtc:yyyy-MM-dd HH:mm} UTC";

            DrawCellText(gfx, layout, rowIndex, columnIndex: 1, font, signedBy);
            DrawCellText(gfx, layout, rowIndex, columnIndex: 2, font, signedAtText);
        });
    }

    private static byte[] WithLastPage(byte[] pdfBytes, Action<XGraphics, PdfPage> draw)
    {
        using var inputStream = new MemoryStream(pdfBytes);
        using var document = PdfReader.Open(inputStream, PdfDocumentOpenMode.Modify);

        if (document.PageCount == 0)
        {
            throw new InvalidOperationException("The PDF document has no pages to sign.");
        }

        var page = document.Pages[document.PageCount - 1];
        using (var gfx = XGraphics.FromPdfPage(page))
        {
            draw(gfx, page);
        }

        using var outputStream = new MemoryStream();
        document.Save(outputStream);
        return outputStream.ToArray();
    }

    /// <summary>
    /// Appends a new blank page (matching the size of the document's current
    /// last page) and draws on that, so the signature table never overlaps
    /// the uploaded content - it always lives on its own page at the end.
    /// </summary>
    private static byte[] WithNewBlankPage(byte[] pdfBytes, Action<XGraphics, PdfPage> draw)
    {
        using var inputStream = new MemoryStream(pdfBytes);
        using var document = PdfReader.Open(inputStream, PdfDocumentOpenMode.Modify);

        if (document.PageCount == 0)
        {
            throw new InvalidOperationException("The PDF document has no pages to sign.");
        }

        var lastContentPage = document.Pages[document.PageCount - 1];
        var page = document.AddPage();
        page.Width = lastContentPage.Width;
        page.Height = lastContentPage.Height;

        using (var gfx = XGraphics.FromPdfPage(page))
        {
            draw(gfx, page);
        }

        using var outputStream = new MemoryStream();
        document.Save(outputStream);
        return outputStream.ToArray();
    }

    private (double Left, double Top, (string Title, double Width)[] Columns) GetLayout(PdfPage page, int signatureRowCount)
    {
        var tableWidth = page.Width.Point - _options.MarginLeft - _options.MarginRight;
        var columns = new (string Title, double Width)[]
        {
            ("Rodzaj podpisu", tableWidth * 0.3),
            ("Podpisano przez", tableWidth * 0.4),
            ("Data", tableWidth * 0.3),
        };

        var totalRows = signatureRowCount + 1; // header + one row per signature category
        var tableHeight = _options.RowHeight * totalRows;
        var top = page.Height.Point - _options.MarginBottom - tableHeight;

        return (_options.MarginLeft, top, columns);
    }

    private void DrawRow(
        XGraphics gfx,
        (double Left, double Top, (string Title, double Width)[] Columns) layout,
        int rowIndex,
        XFont font,
        IReadOnlyList<string> cellValues)
    {
        var y = layout.Top + _options.RowHeight * rowIndex;
        var x = layout.Left;
        for (var i = 0; i < layout.Columns.Length; i++)
        {
            var width = layout.Columns[i].Width;
            gfx.DrawRectangle(XPens.Black, new XRect(x, y, width, _options.RowHeight));
            gfx.DrawString(cellValues[i], font, XBrushes.Black,
                new XRect(x + 4, y, width - 8, _options.RowHeight), XStringFormats.CenterLeft);
            x += width;
        }
    }

    private void DrawCellText(
        XGraphics gfx,
        (double Left, double Top, (string Title, double Width)[] Columns) layout,
        int rowIndex,
        int columnIndex,
        XFont font,
        string text)
    {
        var y = layout.Top + _options.RowHeight * rowIndex;
        var x = layout.Left + layout.Columns.Take(columnIndex).Sum(c => c.Width);
        var width = layout.Columns[columnIndex].Width;
        gfx.DrawString(text, font, XBrushes.Black,
            new XRect(x + 4, y, width - 8, _options.RowHeight), XStringFormats.CenterLeft);
    }
}
