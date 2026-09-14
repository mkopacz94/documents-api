using DocumentsApi.Core.Data.Entities;
using DocumentsApi.Core.Domain;
using Xunit;

namespace DocumentsApi.Api.Tests.Domain;

public class SignatureCategoryExtensionsTests
{
    [Fact]
    public void GetNextExpected_ReturnsFirstCategoryWhenNoneSigned()
    {
        var next = SignatureCategoryExtensions.GetNextExpected([]);

        Assert.Equal(SignatureCategory.Opracowal, next);
    }

    [Fact]
    public void GetNextExpected_SkipsAlreadySignedCategories()
    {
        var signed = new[] { Signature(SignatureCategory.Opracowal) };

        var next = SignatureCategoryExtensions.GetNextExpected(signed);

        Assert.Equal(SignatureCategory.Sprawdzil, next);
    }

    [Fact]
    public void IsFullySigned_FalseUntilEveryCategorySigned()
    {
        var signed = new[] { Signature(SignatureCategory.Opracowal), Signature(SignatureCategory.Sprawdzil) };

        Assert.False(SignatureCategoryExtensions.IsFullySigned(signed));
    }

    [Fact]
    public void IsFullySigned_TrueWhenEveryCategorySigned()
    {
        var signed = SignatureCategoryExtensions.Sequence.Select(Signature).ToList();

        Assert.True(SignatureCategoryExtensions.IsFullySigned(signed));
    }

    private static DocumentSignature Signature(SignatureCategory category) => new() { Category = category };
}
