namespace AlbionApp.Domain.Crafting;

/// <summary>
/// Representa un libro de laborer de Albion Online.
/// Agrupa los ítems que llenan ese libro y la fama máxima que admite.
/// </summary>
public sealed record JournalItem(
    string UniqueName,
    int    Tier,
    double MaxFame);
