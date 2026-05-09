namespace AlbionApp.Domain.Crafting;

/// <summary>
/// Parámetros de entrada completos para una sesión de cálculo de crafteo.
/// Representa todo lo que el usuario configura antes de iniciar el cálculo.
/// </summary>
/// <param name="TargetItemId">
///     Identificador del ítem a craftear (puede incluir enchantment: "T4_SWORD@1").
/// </param>
/// <param name="Quantity">Cantidad de ítems a fabricar.</param>
/// <param name="ReturnRate">
///     Parámetros de tasa de retorno (ciudad, foco, hideout, etc.).
/// </param>
/// <param name="MarketPrices">
///     Precios de mercado por ítem: itemId → precio en plata por unidad.
///     Los ítems sin precio registrado no tendrán costo calculado.
/// </param>
/// <param name="PerItemDecisions">
///     Override de decisión por ítem (Craft/Buy/Auto).
///     Si un ítem no aparece aquí, se usa <see cref="CraftDecision.Auto"/>.
/// </param>
/// <param name="MaxRecursionDepth">
///     Profundidad máxima del árbol de crafteo para evitar bucles o stacks infinitos.
/// </param>
public sealed record CraftingParameters(
    string TargetItemId,
    int Quantity,
    ReturnRateParameters ReturnRate,
    IReadOnlyDictionary<string, decimal> MarketPrices,
    IReadOnlyDictionary<string, CraftDecision>? PerItemDecisions = null,
    int MaxRecursionDepth = 12
)
{
    /// <summary>
    /// Retorna la decisión explícita para un ítem, o <see cref="CraftDecision.Auto"/> si no está configurada.
    /// </summary>
    public CraftDecision GetDecision(string itemId) =>
        PerItemDecisions?.TryGetValue(itemId, out var d) == true
            ? d
            : CraftDecision.Auto;

    /// <summary>Obtiene el precio de mercado de un ítem, o null si no está disponible.</summary>
    public decimal? GetMarketPrice(string itemId) =>
        MarketPrices.TryGetValue(itemId, out var p) ? p : null;
}
