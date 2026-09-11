using System.Text.RegularExpressions;

namespace DocumentsApi.Api.Validation;

/// <summary>
/// The parsed "&lt;RepositoryId&gt;#&lt;ProjectName&gt;#&lt;Version&gt;" segments of a
/// validated file name.
/// </summary>
public readonly record struct DocumentFileNameParts(
    string FullName,
    string RepositoryId,
    string ProjectName,
    string Version);

/// <summary>
/// Parses and validates the "&lt;RepositoryId&gt;#&lt;ProjectName&gt;#&lt;Version&gt;" file
/// name convention (e.g. "729#VIPD2#v1.00.16"). Pulled out of the controller
/// as a pure string-in/struct-out function - no ASP.NET Core dependencies -
/// so the parsing rules can be unit tested directly.
/// </summary>
public static class DocumentFileNameValidator
{
    // Version must contain exactly one or two dots (e.g. "v1.00.16" or
    // "236A.01"), with a non-empty, dot-free chunk on each side of every dot -
    // no leading/trailing/doubled dots, and no third dot.
    private static readonly Regex Pattern =
        new(@"^(?<repo>[^#]+)#(?<project>[^#]+)#(?<version>[^#.]+(?:\.[^#.]+){1,2})$", RegexOptions.Compiled);

    public static bool TryParse(string fileName, out DocumentFileNameParts parts)
    {
        var match = Pattern.Match(fileName);
        if (!match.Success)
        {
            parts = default;
            return false;
        }

        parts = new DocumentFileNameParts(
            fileName,
            match.Groups["repo"].Value,
            match.Groups["project"].Value,
            match.Groups["version"].Value);
        return true;
    }
}
