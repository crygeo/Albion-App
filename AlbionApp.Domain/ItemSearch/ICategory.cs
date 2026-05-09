namespace AlbionApp.Domain.ItemSearch;

/// <summary>
/// Contrato de un nodo en la jerarquía de categorías de tienda de Albion Online.
///
/// Implementado por:
/// - <see cref="Category"/> (record inmutable del dominio, árbol de datos)
/// - Future: <c>ItemCategoryVm</c> (wrapper WPF con estado de expansión / selección)
///
/// La jerarquía es recursiva e ilimitada en profundidad, espejando la estructura
/// real del juego (shopcategory → shopsubcategory1 → shopsubcategory2 → …).
/// </summary>
public interface ICategory
{
    /// <summary>Identificador de filtro del juego (e.g., "weapons", "bow").</summary>
    string Id { get; }

    /// <summary>
    /// Clave de localización completa para resolver el nombre visible
    /// (ej. <c>"@MARKETPLACEGUI_ROLLOUT_SHOPCATEGORY_WEAPONS"</c>).
    /// Se pasa directamente a <c>ILocalizationService.GetText()</c>.
    /// <c>null</c> si el nodo no tiene atributo <c>displayname</c>; el caller usa <see cref="Id"/> como fallback.
    /// </summary>
    string? LocalizationId { get; }

    /// <summary>Orden de presentación canónico del juego (menor = primero).</summary>
    int SortValue { get; }

    /// <summary>Nodos hijo del siguiente nivel de la jerarquía.</summary>
    IReadOnlyList<ICategory> Children { get; }
}
