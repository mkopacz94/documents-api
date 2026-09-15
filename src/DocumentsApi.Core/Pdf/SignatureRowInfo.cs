using DocumentsApi.Core.Domain;

namespace DocumentsApi.Core.Pdf;

/// <summary>
/// One row of the signature table. <see cref="SignedBy"/>/<see cref="SignedAtUtc"/>
/// are null when that category hasn't been signed yet, rendering as a blank row.
/// </summary>
public record SignatureRowInfo(SignatureCategory Category, string? SignedBy, DateTime? SignedAtUtc);
