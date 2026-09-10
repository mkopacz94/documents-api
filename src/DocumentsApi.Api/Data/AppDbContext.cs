using DocumentsApi.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DocumentsApi.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<DocumentSignature> DocumentSignatures => Set<DocumentSignature>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DocumentSignature>(entity =>
        {
            entity.ToTable("DocumentSignatures");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FileName).IsRequired().HasMaxLength(255);
            entity.Property(e => e.SignedBy).IsRequired().HasMaxLength(255);
            entity.Property(e => e.DocumentHash).IsRequired().HasMaxLength(64).IsFixedLength();
            entity.HasIndex(e => e.SignedBy);
        });
    }
}
