namespace AlbionApp.Domain.Crafting;

/// <summary>
/// Nodo del árbol de crafteo. Puede representar un ítem crafteado o comprado.
/// El árbol se construye de forma recursiva: los hijos son los ingredientes,
/// y cada ingrediente puede ser a su vez un nodo crafteado.
/// </summary>
/// <param name="ItemId">Identificador del ítem.</param>
/// <param name="DisplayName">Nombre localizado para mostrar.</param>
/// <param name="GrossQuantity">
///     Cantidad bruta requerida por el nodo padre antes de aplicar retorno.
/// </param>
/// <param name="ReturnedQuantity">
///     Cantidad esperada devuelta por la mecánica de retorno del crafteo padre.
///     Cero si este ingrediente no participa en el retorno.
/// </param>
/// <param name="NetQuantity">
///     Cantidad neta efectiva = <paramref name="GrossQuantity"/> - <paramref name="ReturnedQuantity"/>.
///     Es lo que realmente se consume o debe obtenerse.
/// </param>
/// <param name="ParticipatesInReturn">
///     Indica si este ítem puede ser parcialmente devuelto cuando se usa como ingrediente.
/// </param>
/// <param name="IsCrafted">
///     True si este nodo se resuelve crafteando; false si se compra en el mercado.
/// </param>
/// <param name="Children">
///     Ingredientes de la receta (sólo relevante cuando <paramref name="IsCrafted"/> es true).
/// </param>
/// <param name="UnitMarketPrice">Precio unitario en el mercado (null si no se proporcionó).</param>
/// <param name="TotalCost">
///     Costo total en plata de este subárbol.
///     Si es crafteado: suma del costo de los hijos.
///     Si es comprado: <paramref name="NetQuantity"/> × <paramref name="UnitMarketPrice"/>.
/// </param>
public sealed record CraftingNode(
    string ItemId,
    string DisplayName,
    decimal GrossQuantity,
    decimal ReturnedQuantity,
    decimal NetQuantity,
    bool ParticipatesInReturn,
    bool IsCrafted,
    IReadOnlyList<CraftingNode> Children,
    decimal? UnitMarketPrice,
    decimal TotalCost
)
{
    /// <summary>True si el nodo es una hoja (no tiene hijos, se compra del mercado).</summary>
    public bool IsLeaf => !IsCrafted || Children.Count == 0;

    /// <summary>True si el nodo tiene precio de mercado configurado.</summary>
    public bool HasMarketPrice => UnitMarketPrice.HasValue;

    /// <summary>
    /// Retorno esperado en plata si este ítem se vende (sólo útil para el nodo raíz).
    /// Null si no hay precio de venta configurado.
    /// </summary>
    public decimal? MarketValue => UnitMarketPrice.HasValue
        ? GrossQuantity * UnitMarketPrice.Value
        : null;
}
