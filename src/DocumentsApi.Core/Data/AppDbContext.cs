using DocumentsApi.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DocumentsApi.Core.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Document> Documents => Set<Document>();

    public DbSet<DocumentSignature> DocumentSignatures => Set<DocumentSignature>();

    public DbSet<SigningFailure> SigningFailures => Set<SigningFailure>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Document>(entity =>
        {
            entity.ToTable("Documents");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FileName).IsRequired().HasMaxLength(300);
            entity.Property(e => e.RepositoryId).IsRequired().HasMaxLength(100);
            entity.Property(e => e.ProjectName).IsRequired().HasMaxLength(150);
            entity.Property(e => e.Version).IsRequired().HasMaxLength(100);
            entity.Property(e => e.UploadedBy).IsRequired().HasMaxLength(255);
            entity.Property(e => e.CurrentHash).IsRequired().HasMaxLength(64).IsFixedLength();
            entity.HasIndex(e => e.FileName).IsUnique();
        });

        modelBuilder.Entity<DocumentSignature>(entity =>
        {
            entity.ToTable("DocumentSignatures");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SignedBy).IsRequired().HasMaxLength(255);
            entity.Property(e => e.DocumentHash).IsRequired().HasMaxLength(64).IsFixedLength();
            entity.HasIndex(e => new { e.DocumentId, e.Category }).IsUnique();
            entity.HasOne(e => e.Document)
                .WithMany(d => d.Signatures)
                .HasForeignKey(e => e.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SigningFailure>(entity =>
        {
            entity.ToTable("SigningFailures");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FileName).IsRequired().HasMaxLength(300);
            entity.Property(e => e.AttemptedBy).IsRequired().HasMaxLength(255);
            entity.Property(e => e.ErrorMessage).IsRequired().HasMaxLength(2000);
        });
    }
}
