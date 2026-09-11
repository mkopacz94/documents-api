using DocumentsApi.Api.Validation;
using Xunit;

namespace DocumentsApi.Api.Tests.Validation;

public class DocumentFileNameValidatorTests
{
    [Theory]
    [InlineData("729#VIPD2#v1.00.16", "729", "VIPD2", "v1.00.16")] // two dots
    [InlineData("13#VIPRS#236A.01.P8Q", "13", "VIPRS", "236A.01.P8Q")] // two dots, mixed alphanumeric
    [InlineData("729#VIPD2#v1.00", "729", "VIPD2", "v1.00")] // one dot
    public void TryParse_AcceptsWellFormedNames(string fileName, string repo, string project, string version)
    {
        var success = DocumentFileNameValidator.TryParse(fileName, out var parts);

        Assert.True(success);
        Assert.Equal(fileName, parts.FullName);
        Assert.Equal(repo, parts.RepositoryId);
        Assert.Equal(project, parts.ProjectName);
        Assert.Equal(version, parts.Version);
    }

    [Theory]
    [InlineData("729#VIPD2#v1")] // zero dots in version
    [InlineData("729#VIPD2#v1.00.16.20")] // three dots in version
    [InlineData("729#VIPD2#v1..00")] // doubled dot (empty chunk)
    [InlineData("729#VIPD2#v1.00.")] // trailing dot (empty final chunk)
    [InlineData("729#VIPD2#.v1.00")] // leading dot (empty first chunk)
    [InlineData("729#VIPD2")] // missing version segment entirely
    [InlineData("729#VIPD2#v1.00#extra")] // extra segment
    [InlineData("#VIPD2#v1.00")] // empty repository segment
    [InlineData("729##v1.00")] // empty project segment
    [InlineData("")] // empty string
    public void TryParse_RejectsMalformedNames(string fileName)
    {
        var success = DocumentFileNameValidator.TryParse(fileName, out var parts);

        Assert.False(success);
        Assert.Equal(default, parts);
    }
}
