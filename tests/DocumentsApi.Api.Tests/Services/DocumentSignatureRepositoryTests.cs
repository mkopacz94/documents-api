using System.Reflection;
using DocumentsApi.Core.Data;
using DocumentsApi.Core.Data.Entities;
using DocumentsApi.Core.Domain;
using DocumentsApi.Core.Services;
using DocumentsApi.Core.Services.Signing;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Xunit;

namespace DocumentsApi.Api.Tests.Services;

public class DocumentSignatureRepositoryTests
{
    private const string FileName = "729#VIPD2#v1.00.16";

    [Fact]
    public async Task GetByFileNameAsync_ReturnsOnlySignaturesForThatFileName_OrderedByCategory()
    {
        await using var dbContext = CreateInMemoryContext();
        dbContext.DocumentSignatures.AddRange(
            Signature(FileName, SignatureCategory.Zatwierdzil),
            Signature(FileName, SignatureCategory.Opracowal),
            Signature("other#Project#v1.0", SignatureCategory.Opracowal));
        await dbContext.SaveChangesAsync();

        var repository = new DocumentSignatureRepository(dbContext);
        var result = await repository.GetByFileNameAsync(FileName);

        Assert.Equal(2, result.Count);
        Assert.Equal(SignatureCategory.Opracowal, result[0].Category);
        Assert.Equal(SignatureCategory.Zatwierdzil, result[1].Category);
    }

    [Fact]
    public async Task GetByFileNameAsync_ReturnsEmpty_WhenNoSignaturesExistForFileName()
    {
        await using var dbContext = CreateInMemoryContext();
        var repository = new DocumentSignatureRepository(dbContext);

        var result = await repository.GetByFileNameAsync(FileName);

        Assert.Empty(result);
    }

    [Fact]
    public async Task AddAsync_PersistsTheSignature()
    {
        await using var dbContext = CreateInMemoryContext();
        var repository = new DocumentSignatureRepository(dbContext);

        await repository.AddAsync(Signature(FileName, SignatureCategory.Opracowal));

        var stored = await dbContext.DocumentSignatures.SingleAsync();
        Assert.Equal(FileName, stored.FileName);
        Assert.Equal(SignatureCategory.Opracowal, stored.Category);
    }

    [Fact]
    public async Task AddAsync_ThrowsDuplicateSignatureException_WhenSaveFailsWithMySqlDuplicateKeyError()
    {
        await using var dbContext = CreateThrowingContext();
        var repository = new DocumentSignatureRepository(dbContext);

        await Assert.ThrowsAsync<DuplicateSignatureException>(
            () => repository.AddAsync(Signature(FileName, SignatureCategory.Opracowal)));
    }

    [Fact]
    public async Task FindByHashAsync_ReturnsTheMatchingSignature()
    {
        await using var dbContext = CreateInMemoryContext();
        var signature = Signature(FileName, SignatureCategory.Opracowal, hash: new string('a', 64));
        dbContext.DocumentSignatures.Add(signature);
        await dbContext.SaveChangesAsync();

        var repository = new DocumentSignatureRepository(dbContext);
        var result = await repository.FindByHashAsync(new string('a', 64));

        Assert.NotNull(result);
        Assert.Equal(FileName, result!.FileName);
    }

    [Fact]
    public async Task FindByHashAsync_ReturnsNull_WhenNoSignatureMatchesHash()
    {
        await using var dbContext = CreateInMemoryContext();
        var repository = new DocumentSignatureRepository(dbContext);

        var result = await repository.FindByHashAsync(new string('a', 64));

        Assert.Null(result);
    }

    private static AppDbContext CreateInMemoryContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static ThrowingOnSaveDbContext CreateThrowingContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static DocumentSignature Signature(string fileName, SignatureCategory category, string? hash = null) => new()
    {
        FileName = fileName,
        RepositoryId = "729",
        ProjectName = "VIPD2",
        Version = "v1.00.16",
        Category = category,
        SignedBy = "someone",
        SignedAtUtc = DateTime.UtcNow,
        DocumentHash = hash ?? new string('a', 64),
    };

    /// <summary>
    /// The unique-constraint violation DocumentSignatureRepository translates
    /// into DuplicateSignatureException is a real MySQL error (code 1062)
    /// that the EF Core InMemory provider has no way to reproduce - it
    /// doesn't enforce unique indexes at all. This subclass short-circuits
    /// SaveChangesAsync to throw the same shape of exception the MySQL
    /// provider would, so the translation logic itself can be tested without
    /// a live database.
    /// </summary>
    private sealed class ThrowingOnSaveDbContext : AppDbContext
    {
        public ThrowingOnSaveDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            throw new DbUpdateException("Duplicate entry.", CreateMySqlDuplicateKeyException());

        // MySqlException only exposes internal constructors (it's meant to be
        // thrown solely by the driver), so building one for a test has to go
        // through reflection.
        private static MySqlException CreateMySqlDuplicateKeyException()
        {
            var constructor = typeof(MySqlException).GetConstructor(
                BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null,
                types: [typeof(MySqlErrorCode), typeof(string)],
                modifiers: null);

            if (constructor is null)
            {
                throw new InvalidOperationException(
                    "MySqlException(MySqlErrorCode, string) constructor not found - MySqlConnector's internal API may have changed.");
            }

            return (MySqlException)constructor.Invoke([MySqlErrorCode.DuplicateKeyEntry, "Duplicate entry."]);
        }
    }
}
