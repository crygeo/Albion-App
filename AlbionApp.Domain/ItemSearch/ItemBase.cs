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
}
