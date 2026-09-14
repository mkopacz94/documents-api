using System.Security.Claims;
using System.Security.Cryptography;
using DocumentsApi.Core.Data.Entities;
using DocumentsApi.Core.Domain;
using DocumentsApi.Core.Options;
using DocumentsApi.Core.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace DocumentsApi.Api.Tests.Services;

public class SigningWorkflowServiceTests
{
    private const string FileName = "729#VIPD2#v1.00.16";

    private readonly SigningWorkflowService _service = new(Options.Create(new SignaturePermissionOptions()));

    [Fact]
    public void ValidateSigningRequest_AllowsFirstCategoryWithNoPriorSignatures()
    {
        var result = _service.ValidateSigningRequest(
            FileName,
            existingSignatures: [],
            SignatureCategory.Opracowal,
            UserWithRole("DocumentSigner.Opracowal"),
            submittedBytes: [1, 2, 3]);

        Assert.True(result.IsValid);
        Assert.Null(result.FailureReason);
    }

    [Fact]
    public void ValidateSigningRequest_RejectsAlreadySignedCategory()
    {
        var existing = new[] { Signature(SignatureCategory.Opracowal, hash: "irrelevant") };

        var result = _service.ValidateSigningRequest(
            FileName,
            existing,
            SignatureCategory.Opracowal,
            UserWithRole("DocumentSigner.Opracowal"),
            submittedBytes: [1, 2, 3]);

        Assert.False(result.IsValid);
        Assert.Equal(SigningFailureReason.AlreadySigned, result.FailureReason);
    }

    [Fact]
    public void ValidateSigningRequest_RejectsOutOfOrderCategory()
    {
        var result = _service.ValidateSigningRequest(
            FileName,
            existingSignatures: [],
            SignatureCategory.Zatwierdzil,
            UserWithRole("DocumentSigner.Zatwierdzil"),
            submittedBytes: [1, 2, 3]);

        Assert.False(result.IsValid);
        Assert.Equal(SigningFailureReason.OutOfOrder, result.FailureReason);
    }

    [Fact]
    public void ValidateSigningRequest_RejectsCallerWithoutRequiredRole()
    {
        var result = _service.ValidateSigningRequest(
            FileName,
            existingSignatures: [],
            SignatureCategory.Opracowal,
            UserWithRole("SomeOtherRole"),
            submittedBytes: [1, 2, 3]);

        Assert.False(result.IsValid);
        Assert.Equal(SigningFailureReason.RoleNotAuthorized, result.FailureReason);
    }

    [Fact]
    public void ValidateSigningRequest_RejectsStaleDocumentForLaterCategory()
    {
        var existing = new[] { Signature(SignatureCategory.Opracowal, hash: HashOf([9, 9, 9])) };

        var result = _service.ValidateSigningRequest(
            FileName,
            existing,
            SignatureCategory.Sprawdzil,
            UserWithRole("DocumentSigner.Sprawdzil"),
            submittedBytes: [1, 2, 3]);

        Assert.False(result.IsValid);
        Assert.Equal(SigningFailureReason.StaleDocumentState, result.FailureReason);
    }

    [Fact]
    public void ValidateSigningRequest_AllowsLaterCategoryWhenHashMatchesPrecedingStage()
    {
        byte[] currentBytes = [1, 2, 3];
        var existing = new[] { Signature(SignatureCategory.Opracowal, hash: HashOf(currentBytes)) };

        var result = _service.ValidateSigningRequest(
            FileName,
            existing,
            SignatureCategory.Sprawdzil,
            UserWithRole("DocumentSigner.Sprawdzil"),
            currentBytes);

        Assert.True(result.IsValid);
    }

    private static ClaimsPrincipal UserWithRole(string role) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "Test"));

    private static string HashOf(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static DocumentSignature Signature(SignatureCategory category, string hash) => new()
    {
        FileName = FileName,
        Category = category,
        DocumentHash = hash,
        SignedBy = "someone",
        SignedAtUtc = DateTime.UtcNow,
    };
}
