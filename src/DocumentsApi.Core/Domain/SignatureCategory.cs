namespace DocumentsApi.Core.Domain;

/// <summary>
/// The three signature stages a document goes through, in required order.
/// </summary>
public enum SignatureCategory
{
    Opracowal = 0,
    Sprawdzil = 1,
    Zatwierdzil = 2,
}

public static class SignatureCategoryExtensions
{
    /// <summary>
    /// The required signing order. A document must collect signatures in this sequence.
    /// </summary>
    public static readonly IReadOnlyList<SignatureCategory> Sequence = new[]
    {
        SignatureCategory.Opracowal,
        SignatureCategory.Sprawdzil,
        SignatureCategory.Zatwierdzil,
    };

    public static string DisplayName(this SignatureCategory category) => category switch
    {
        SignatureCategory.Opracowal => "Opracował",
        SignatureCategory.Sprawdzil => "Sprawdził",
        SignatureCategory.Zatwierdzil => "Zatwierdził",
        _ => category.ToString(),
    };
}
