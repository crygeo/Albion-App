namespace AlbionApp.Domain.ItemSearch;

/// <summary>
/// Nodo en la jerarquía de categorías de tienda de Albion Online,
/// utilizado por la capa Application para construir el árbol de navegación.
///
/// El modelo es recursivo e ilimitado en profundidad, espejando la estructura
/// real del juego (<c>shopcategory → shopsubcategory1 → shopsubcategory2 → …</c>)
/// sin acoplarse a un número fijo de niveles.
///
/// <see cref="Id"/> coincide con los atributos <c>shopcategory</c>,
/// <c>shopsubcategory1</c>, etc. de los ítems en items.xml (usado para filtrar).
///
/// <see cref="LocalizationId"/> es la clave de localización completa extraída del
/// atributo <c>displayname</c> del XML del juego, tal cual
/// (ej. <c>"@MARKETPLACEGUI_ROLLOUT_SHOPCATEGORY_WEAPONS"</c>).
/// Se pasa directamente a <c>ILocalizationService.GetText()</c>.
/// Cuando es <c>null</c>, el caller usa <see cref="Id"/> como fallback de texto.
///
/// <see cref="SortValue"/> refleja el atributo <c>value=""</c> del XML del juego,
/// que define el orden canónico de presentación.
///
/// <para><b>Sistema de valor numérico de categoría:</b></para>
/// Cada nodo suma su propio <c>value</c> XML al de sus ancestros, produciendo
/// un entero único que identifica la posición exacta en la jerarquía.
/// <code>
///   artefacts     → Value = 12000000, AccumulatedValue = 12000000
///     weapons     → Value =    30000, AccumulatedValue = 12030000
///       arcanestaff → Value = 100,   AccumulatedValue = 12030100
/// </code>
///
/// <see cref="AccumulatedValue"/> es el límite inferior del rango de filtrado.
/// <see cref="MaxDescendantValue"/> es el límite superior: todos los descendientes
/// tienen <c>AccumulatedValue</c> dentro de <c>[AccumulatedValue, MaxDescendantValue]</c>.
///
/// <see cref="Children"/> son los nodos del nivel inmediatamente inferior, ya
/// ordenados por <see cref="SortValue"/> ascendente por el loader.
/// </summary>
public sealed record Category(
    string                    Id,
    string?                   LocalizationId,
    int                       SortValue,
    int                       Depth,
    int                       Value,
    int                       AccumulatedValue,
    int                       MaxDescendantValue,
    IReadOnlyList<Category>   Children)
    : ICategory
{
    // ICategory.Children retorna IReadOnlyList<ICategory> — implementación explícita
    // para preservar el tipo fuerte en el modelo sin copiar la lista.
    IReadOnlyList<ICategory> ICategory.Children
        => Children.Count == 0
            ? []
            : Children.Select(c => (ICategory)c).ToList().AsReadOnly();
}
