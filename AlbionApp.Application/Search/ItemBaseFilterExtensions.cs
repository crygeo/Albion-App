using AlbionApp.Domain.ItemSearch;

namespace AlbionApp.Application.Search;

/// <summary>
/// Post-filtros estructurales sobre <see cref="ItemBase"/>.
///
/// <para>Se aplican después de obtener los candidatos (por índice de localización,
/// <c>GetAll</c> o <c>GetFirst</c>). Todos los valores provienen del XML del
/// catálogo — fuente de verdad, no de la clave TMX.</para>
///
/// <para><b>Filtro de categoría:</b> comparación de enteros por rango, sin strings.
/// <c>minCategory</c> y <c>maxCategory</c> provienen de
/// <c>Category.AccumulatedValue</c> y <c>Category.MaxDescendantValue</c>
/// respectivamente. Un par <c>null</c> significa sin filtro de categoría.</para>
///
/// <para>La cadena de filtros es lazy — solo materializa al iterar.</para>
/// </summary>
public static class ItemBaseFilterExtensions
{
    /// <summary>
    /// Aplica los tres filtros estructurales en una sola llamada.
    /// </summary>
    /// <param name="source">Candidatos a filtrar.</param>
    /// <param name="minCategory">
    ///   Límite inferior del rango de categoría (<see cref="ItemBase.CategoryValue"/> ≥ minCategory).
    ///   <c>null</c> = sin filtro de categoría.
    /// </param>
    /// <param name="maxCategory">
    ///   Límite superior del rango de categoría (<see cref="ItemBase.CategoryValue"/> ≤ maxCategory).
    ///   <c>null</c> = sin filtro de categoría.
    /// </param>
    /// <param name="tier">Tier requerido; <c>null</c> = sin filtro.</param>
    /// <param name="enchantment">Nivel de encantamiento requerido; <c>null</c> = sin filtro.</param>
    public static IEnumerable<ItemBase> Filter(
        this IEnumerable<ItemBase> source,
        int? minCategory,
        int? maxCategory,
        int? tier,
        int? enchantment)
    {
        if (minCategory.HasValue && maxCategory.HasValue)
        {
            var min = minCategory.Value;
            var max = maxCategory.Value;
            source = source.Where(i => i.CategoryValue >= min && i.CategoryValue <= max);
        }

        if (tier.HasValue)
            source = source.Where(i => i.Tier == tier.Value);

        if (enchantment.HasValue)
            source = source.Where(i => i.EnchantmentLevel == enchantment.Value);

        return source;
    }

}