using PdfSharpCore.Fonts;

namespace DocumentsApi.Api.Pdf;

/// <summary>
/// Resolves fonts from embedded TTF resources instead of relying on fonts being
/// installed on the host, so PDF signing behaves the same in any deployment
/// environment (containers included).
/// </summary>
public sealed class EmbeddedFontResolver : IFontResolver
{
    public const string FamilyName = "Liberation Sans";

    private const string RegularFaceName = "LiberationSans#Regular";
    private const string BoldFaceName = "LiberationSans#Bold";

    public string DefaultFontName => RegularFaceName;

    public byte[] GetFont(string faceName)
    {
        var resourceName = faceName switch
        {
            RegularFaceName => "DocumentsApi.Api.Resources.Fonts.LiberationSans-Regular.ttf",
            BoldFaceName => "DocumentsApi.Api.Resources.Fonts.LiberationSans-Bold.ttf",
            _ => throw new ArgumentException($"Unknown font face: {faceName}", nameof(faceName)),
        };

        var assembly = typeof(EmbeddedFontResolver).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded font resource '{resourceName}' was not found.");
        using var memoryStream = new MemoryStream();
        stream.CopyTo(memoryStream);
        return memoryStream.ToArray();
    }

    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        var faceName = isBold ? BoldFaceName : RegularFaceName;
        return new FontResolverInfo(faceName);
    }
}
