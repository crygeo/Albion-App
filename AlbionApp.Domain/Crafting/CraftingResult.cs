namespace AlbionApp.Domain.Crafting;

/// <summary>
/// Resultado completo de un cálculo de crafteo.
/// Contiene tanto el árbol detallado como la vista plana de materiales base
/// y los indicadores de rentabilidad.
/// </summary>
/// <param name="RootNode">
///     Nodo raíz del árbol de crafteo (el ítem objetivo).
/// </param>
/// <param name="FlatMaterials">
///     Lista de materiales base (hojas del árbol) con cantidades agregadas.
///     Útil para mostrar una lista de compras consolidada.
/// </param>
/// <param name="TotalMaterialCost">
///     Suma del costo en plata de todos los materiales base (precio neto total).
/// </param>
/// <param name="ExpectedSalePrice">
///     Ingreso esperado por vender el ítem final en el mercado.
///     Null si no se proporcionó precio de venta.
/// </param>
/// <param name="EstimatedProfit">
///     Beneficio estimado = <paramref name="ExpectedSalePrice"/> - <paramref name="TotalMaterialCost"/>.
///     Null si no hay precio de venta.
/// </param>
/// <param name="EffectiveReturnRate">
///     Tasa de retorno efectiva aplicada al crafteo raíz (entre 0 y 1).
/// </param>
/// <param name="Warnings">
///     Advertencias generadas durante el cálculo (ítems sin precio, recetas no encontradas, etc.).
/// </param>
public sealed record CraftingResult(
    CraftingNode RootNode,
    IReadOnlyList<MaterialRequirement> FlatMaterials,
    decimal TotalMaterialCost,
    decimal? ExpectedSalePrice,
    decimal? EstimatedProfit,
    decimal EffectiveReturnRate,
    IReadOnlyList<string> Warnings
)
{
    /// <summary>True si el crafteo es rentable según los precios proporcionados.</summary>
    public bool IsProfitable => EstimatedProfit.HasValue && EstimatedProfit.Value > 0;

    /// <summary>Margen de beneficio relativo sobre el costo (0 a N).</summary>
    public decimal? ProfitMargin => TotalMaterialCost > 0 && EstimatedProfit.HasValue
        ? EstimatedProfit.Value / TotalMaterialCost
        : null;
}
