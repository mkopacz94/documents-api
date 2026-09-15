namespace DocumentsApi.Core.Services;

/// <summary>
/// Thrown when persisting a <see cref="Data.Entities.DocumentSignature"/> hits
/// the unique (FileName, Category) constraint - i.e. two concurrent requests
/// raced to sign the same category and this one lost. The in-memory
/// already-signed check in <see cref="ISigningWorkflowService"/> can't catch
/// this by itself, since both requests can read "not yet signed" before
/// either one writes.
/// </summary>
public class DuplicateSignatureException : Exception
{
    public DuplicateSignatureException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
