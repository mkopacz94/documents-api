using System.Security.Claims;
using System.Security.Cryptography;
using DocumentsApi.Core.Data.Entities;
using DocumentsApi.Core.Domain;
using DocumentsApi.Core.Options;
using DocumentsApi.Core.Pdf;
using DocumentsApi.Core.Services;
using DocumentsApi.Core.Services.Signing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DocumentsApi.Api.Tests.Services;

public class DocumentProcessingServiceTests
{
    private const string FileName = "729#VIPD2#v1.00.16";

    [Fact]
    public void PrepareForSigning_ReturnsRenderedBytes_OnSuccess()
    {
        var pdfSigningService = new FakePdfSigningService();
        var service = CreateService(new FakeSignatureRepository(), pdfSigningService, out _);

        var outcome = service.PrepareForSigning(FileName, [1, 2, 3]);

        Assert.True(outcome.IsValid);
        Assert.Equal(new byte[] { 1, 2, 3 }, outcome.RenderedBytes);
    }

    [Fact]
    public void PrepareForSigning_ReturnsFailure_WhenRenderingThrows()
    {
        var pdfSigningService = new FakePdfSigningService { ThrowOnRenderTable = true };
        var service = CreateService(new FakeSignatureRepository(), pdfSigningService, out _);

        var outcome = service.PrepareForSigning(FileName, [1, 2, 3]);

        Assert.False(outcome.IsValid);
        Assert.Null(outcome.RenderedBytes);
        Assert.NotNull(outcome.ErrorMessage);
    }

    [Fact]
    public async Task SignAsync_ReturnsFailureFromPrecheck_WithoutRenderingOrPersisting()
    {
        var repository = new FakeSignatureRepository();
        var pdfSigningService = new FakePdfSigningService();
        var service = CreateService(repository, pdfSigningService, out _);

        // No role claim, so the precheck rejects before anything else runs.
        var command = Command(SignatureCategory.Opracowal, UserWithRole("SomeOtherRole"));

        var outcome = await service.SignAsync(command);

        Assert.False(outcome.IsValid);
        Assert.Equal(SigningFailureReason.RoleNotAuthorized, outcome.FailureReason);
        Assert.Null(outcome.RenderedBytes);
        Assert.Empty(repository.Signatures);
        Assert.False(pdfSigningService.FillSignatureRowCalled);
    }

    [Fact]
    public async Task SignAsync_ReturnsProcessingFailed_AndLogsFailure_WhenRenderingThrows()
    {
        var repository = new FakeSignatureRepository();
        var pdfSigningService = new FakePdfSigningService { ThrowOnFill = true };
        var service = CreateService(repository, pdfSigningService, out var failureLogger);

        var command = Command(SignatureCategory.Opracowal, UserWithRole("DocumentSigner.Opracowal"));

        var outcome = await service.SignAsync(command);

        Assert.False(outcome.IsValid);
        Assert.Equal(SigningFailureReason.ProcessingFailed, outcome.FailureReason);
        Assert.Single(failureLogger.Logged);
        Assert.Empty(repository.Signatures);
    }

    [Fact]
    public async Task SignAsync_ReturnsAlreadySigned_WhenRepositoryDetectsConcurrentRace()
    {
        var repository = new FakeSignatureRepository { ThrowDuplicateOnAdd = true };
        var pdfSigningService = new FakePdfSigningService();
        var service = CreateService(repository, pdfSigningService, out _);

        var command = Command(SignatureCategory.Opracowal, UserWithRole("DocumentSigner.Opracowal"));

        var outcome = await service.SignAsync(command);

        Assert.False(outcome.IsValid);
        Assert.Equal(SigningFailureReason.AlreadySigned, outcome.FailureReason);
    }

    [Fact]
    public async Task SignAsync_PersistsSignatureAndReturnsNextExpectedCategory_WhenNotFullySigned()
    {
        var repository = new FakeSignatureRepository();
        var pdfSigningService = new FakePdfSigningService();
        var service = CreateService(repository, pdfSigningService, out _);

        var command = Command(SignatureCategory.Opracowal, UserWithRole("DocumentSigner.Opracowal"));

        var outcome = await service.SignAsync(command);

        Assert.True(outcome.IsValid);
        Assert.False(outcome.IsFullySigned);
        Assert.Equal(SignatureCategory.Sprawdzil, outcome.NextExpectedCategory);
        Assert.Single(repository.Signatures);
        Assert.Equal(command.RepositoryId, repository.Signatures[0].RepositoryId);
    }

    [Fact]
    public async Task SignAsync_ReturnsFullySigned_WithNoNextCategory_WhenLastStageSigned()
    {
        var repository = new FakeSignatureRepository();
        repository.Signatures.Add(Signature(SignatureCategory.Opracowal));
        // Zatwierdzil's stale-hash check compares against the immediately
        // preceding stage (Sprawdzil), so its hash must match the submitted
        // bytes ([1, 2, 3]) for the precheck to pass.
        repository.Signatures.Add(Signature(SignatureCategory.Sprawdzil, hash: Convert.ToHexString(SHA256.HashData([1, 2, 3]))));
        var pdfSigningService = new FakePdfSigningService();
        var service = CreateService(repository, pdfSigningService, out _);

        var command = Command(SignatureCategory.Zatwierdzil, UserWithRole("DocumentSigner.Zatwierdzil"));

        var outcome = await service.SignAsync(command);

        Assert.True(outcome.IsValid);
        Assert.True(outcome.IsFullySigned);
        Assert.Null(outcome.NextExpectedCategory);
    }

    private static DocumentProcessingService CreateService(
        FakeSignatureRepository repository,
        FakePdfSigningService pdfSigningService,
        out FakeFailureLogger failureLogger)
    {
        failureLogger = new FakeFailureLogger();
        var workflow = new SigningWorkflowService(Options.Create(new SignaturePermissionOptions()));
        return new DocumentProcessingService(
            repository,
            workflow,
            pdfSigningService,
            failureLogger,
            NullLogger<DocumentProcessingService>.Instance);
    }

    private static SignDocumentCommand Command(SignatureCategory category, ClaimsPrincipal user) =>
        new(FileName, "729", "VIPD2", "v1.00.16", category, user, [1, 2, 3]);

    private static ClaimsPrincipal UserWithRole(string role) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "Test"));

    private static DocumentSignature Signature(SignatureCategory category, string hash = "") => new()
    {
        FileName = FileName,
        Category = category,
        SignedBy = "someone",
        SignedAtUtc = DateTime.UtcNow,
        DocumentHash = hash.Length > 0 ? hash : new string('a', 64),
    };

    private sealed class FakeSignatureRepository : IDocumentSignatureRepository
    {
        public List<DocumentSignature> Signatures { get; } = [];

        public bool ThrowDuplicateOnAdd { get; init; }

        public Task<IReadOnlyList<DocumentSignature>> GetByFileNameAsync(string fileName, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DocumentSignature>>(
                Signatures.Where(s => s.FileName == fileName).OrderBy(s => s.Category).ToList());

        public Task AddAsync(DocumentSignature signature, CancellationToken cancellationToken = default)
        {
            if (ThrowDuplicateOnAdd)
            {
                throw new DuplicateSignatureException("duplicate", new InvalidOperationException());
            }

            Signatures.Add(signature);
            return Task.CompletedTask;
        }

        public Task<DocumentSignature?> FindByHashAsync(string hash, CancellationToken cancellationToken = default) =>
            Task.FromResult(Signatures.FirstOrDefault(s => s.DocumentHash == hash));
    }

    private sealed class FakePdfSigningService : IPdfSigningService
    {
        public bool ThrowOnFill { get; init; }

        public bool ThrowOnRenderTable { get; init; }

        public bool FillSignatureRowCalled { get; private set; }

        public byte[] RenderSignatureTable(byte[] originalPdf, IReadOnlyList<SignatureRowInfo> rows)
        {
            if (ThrowOnRenderTable)
            {
                throw new InvalidOperationException("Simulated PDF rendering failure.");
            }

            return originalPdf;
        }

        public byte[] FillSignatureRow(byte[] currentPdf, SignatureCategory category, string signedBy, DateTime signedAtUtc)
        {
            FillSignatureRowCalled = true;
            if (ThrowOnFill)
            {
                throw new InvalidOperationException("Simulated PDF rendering failure.");
            }

            return currentPdf;
        }
    }

    private sealed class FakeFailureLogger : ISigningFailureLogger
    {
        public List<SigningFailure> Logged { get; } = [];

        public Task LogAsync(SigningFailure failure, CancellationToken cancellationToken = default)
        {
            Logged.Add(failure);
            return Task.CompletedTask;
        }
    }
}
