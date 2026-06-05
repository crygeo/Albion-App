namespace AlbionApp.Domain.ItemSearch;

/// <summary>
/// Modelo de dominio general de un ítem de Albion Online.
///
/// Unifica los datos de AlbionItem (LibAlbionData) en un único record del dominio
/// de la aplicación. Es el único modelo de ítem de la app — válido para cualquier
/// caso de uso: búsqueda, crafteo, mercado, inventario, etc.
///
/// Diseño:
///   • Inmutable (record) — puede ser cacheado, compartido y comparado por valor.
///   • No contiene textos localizados (DisplayName, Description). Esos los resuelve
///     la capa de UI via ILocalizationService.GetText(NameLocalizationKey).
///   • <see cref="RawAttributes"/> cubre atributos específicos por tipo de ítem
///     (armadura, arma, monturas…) sin requerir herencia ni tipado complejo.
/// </summary>
public sealed record ItemBase(
    string  ItemId,
    string  BaseItemId,
    string? NameLocalizationKey,
    string? DescriptionLocalizationKey,
    int?    Tier,
    int     EnchantmentLevel,
    int     CategoryValue,
    string? CraftingCategory,
    string? NodeType,
    bool     IsCraftable,
    SlotType SlotType,
    bool     IsTwoHanded,
    IReadOnlyDictionary<string, string> RawAttributes)
    : IItemBase
{
    /// <inheritdoc/>
    public string TierLabel => Tier.HasValue
        ? EnchantmentLevel > 0 ? $"T{Tier}.{EnchantmentLevel}" : $"T{Tier}"
        : string.Empty;

    /// <summary>
    /// ID del ítem en el formato que acepta la API de Albion Online Data Project.
    /// Si el ítem tiene encantamiento y su ItemId no contiene ya '@', agrega '@{N}'.
    /// Ejemplos:
    ///   T7_PLANKS_LEVEL3  (EnchantmentLevel=3) → T7_PLANKS_LEVEL3@3
    ///   T4_MAIN_SWORD@1   (ya tiene @)          → T4_MAIN_SWORD@1
    ///   T7_PLANKS         (sin encantamiento)   → T7_PLANKS
    /// </summary>
    public string ApiItemId => EnchantmentLevel > 0 && !ItemId.Contains('@')
        ? $"{ItemId}@{EnchantmentLevel}"
        : ItemId;
}
