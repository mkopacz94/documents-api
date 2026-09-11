using PdfSharpCore.Fonts;

namespace DocumentsApi.Core.Pdf;

/// <summary>
/// Resolves fonts from embedded TTF resources instead of relying on fonts being
/// installed on the host, so PDF signing behaves the same in any deployment
/// environment (containers included).
/// </summary>
public sealed class EmbeddedFontResolver : IFontResolver
{
    public const string FamilyName = "Lato";

    private const string RegularFaceName = "Lato#Regular";
    private const string BoldFaceName = "Lato#Bold";

    public string DefaultFontName => RegularFaceName;

    public byte[] GetFont(string faceName)
    {
        var resourceName = faceName switch
        {
            RegularFaceName => "DocumentsApi.Core.Resources.Fonts.Lato-Regular.ttf",
            BoldFaceName => "DocumentsApi.Core.Resources.Fonts.Lato-Bold.ttf",
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
