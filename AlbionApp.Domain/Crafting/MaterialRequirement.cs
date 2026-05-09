namespace AlbionApp.Domain.Crafting;

/// <summary>
/// Requerimiento plano de un material base (hoja del árbol de crafteo).
/// Agrupa la cantidad total acumulada de un ítem específico a través de todo el árbol.
/// </summary>
/// <param name="ItemId">Identificador del ítem.</param>
/// <param name="DisplayName">Nombre localizado.</param>
/// <param name="TotalGross">Cantidad bruta total necesaria (antes de retorno).</param>
/// <param name="TotalNet">
///     Cantidad neta efectiva a adquirir (después de retorno esperado).
/// </param>
/// <param name="UnitPrice">Precio unitario en plata (null si no está disponible).</param>
/// <param name="TotalCost">
///     Costo total: <paramref name="TotalNet"/> × <paramref name="UnitPrice"/>.
///     Cero si no hay precio.
/// </param>
public sealed record MaterialRequirement(
    string ItemId,
    string DisplayName,
    decimal TotalGross,
    decimal TotalNet,
    decimal? UnitPrice,
    decimal TotalCost
)
{
    /// <summary>True si hay precio de mercado registrado para este ítem.</summary>
    public bool HasPrice => UnitPrice.HasValue;
}
