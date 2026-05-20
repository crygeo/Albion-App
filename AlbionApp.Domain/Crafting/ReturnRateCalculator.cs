namespace AlbionApp.Domain.Crafting;

/// <summary>
/// Calcula la tasa de retorno de materiales (Resource Return Rate) de Albion Online.
///
/// Fórmula oficial del juego:
///   LPB  = BaseBonus + EspecializaciónCiudad + FocusBonus + ExtraBonus
///   RRR  = LPB / (1 + LPB)       (todos los valores en decimal, ej: 0.18 = 18%)
///
/// Ejemplos verificados:
///   Ciudad sin esp. sin focus:   LPB=0.18        → RRR ≈ 15.3%
///   Ciudad con esp. crafteo:     LPB=0.18+0.15   → RRR ≈ 24.8%
///   Ciudad con esp. refinado:    LPB=0.18+0.40   → RRR ≈ 36.7%
///   Con focus (+0.59):
///     Base sola:                 LPB=0.18+0.59   → RRR ≈ 43.5%
///     Crafteo especializado:     LPB=0.18+0.15+0.59 → RRR ≈ 47.9%
///     Refinado especializado:    LPB=0.18+0.40+0.59 → RRR ≈ 53.9%
///
/// Nota sobre diarios (journals):
///   El bono de diario NO afecta el RRR por ciclo. Es un sistema paralelo
///   donde el labrador devuelve materiales extra al entregar el diario lleno.
/// </summary>
public static class ReturnRateCalculator
{
    /// <summary>
    /// Bono de focus como Local Production Bonus: +59% (0.59).
    /// Valor fijo del juego, independiente del tier o ítem.
    /// </summary>
    public const decimal FocusLpb = 0.59m;

    /// <summary>
    /// Categorías de crafteo que corresponden a refinado.
    /// Usan <see cref="CraftingCityData.RefiningBonus"/> como bonus base.
    /// </summary>
    private static readonly HashSet<string> RefiningCategories =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "fiber",   // Telas → Lymhurst
            "ore",     // Barras de metal → Thetford
            "hide",    // Cuero → Martlock
            "wood",    // Tablas → Fort Sterling
            "rock"     // Bloques de piedra → Bridgewatch
        };

    /// <summary>Tipo de bonus en el sistema de achievements que añade RRR extra con focus.</summary>
    public const string AchievementReturnBonusType = "resourcereturnbonusfactor";

    // ─── API pública ───────────────────────────────────────────────────────────

    /// <summary>
    /// Determina si una categoría de crafteo corresponde a refinado de recursos.
    /// </summary>
    public static bool IsRefining(string? craftingCategory)
        => RefiningCategories.Contains(craftingCategory ?? string.Empty);

    /// <summary>
    /// Calcula el RRR para una ciudad y categoría dadas.
    /// Selecciona automáticamente RefiningBonus o CraftingBonus según la categoría.
    /// </summary>
    /// <param name="city">Datos de la ciudad seleccionada.</param>
    /// <param name="craftingCategory">Categoría del ítem (ej: "leather_shoes", "ore").</param>
    /// <param name="useFocus">Si el jugador usa focus en este ciclo.</param>
    /// <param name="achievementBonus">
    ///     Bono extra de achievements (tipo resourcereturnbonusfactor).
    ///     Solo se aplica cuando <paramref name="useFocus"/> es true.
    /// </param>
    /// <param name="journalBonus">
    ///     Bono de diario de rendimiento (0.10 = 10%, 0.20 = 20%).
    ///     Se añade al LPB siempre, independientemente del focus.
    /// </param>
    public static decimal Calculate(
        CraftingCityData city,
        string?          craftingCategory,
        bool             useFocus,
        decimal          achievementBonus = 0m,
        decimal          journalBonus     = 0m)
    {
        var isRefining    = IsRefining(craftingCategory);
        var baseBonus     = isRefining ? city.RefiningBonus : city.CraftingBonus;
        var categoryBonus = city.GetBonusFor(craftingCategory);
        var focusBonus    = useFocus ? FocusLpb + achievementBonus : 0m;

        return FromLpb(baseBonus + categoryBonus + focusBonus + journalBonus);
    }

    /// <summary>
    /// Calcula RRR a partir de componentes LPB individuales (todos en decimal).
    /// </summary>
    public static decimal Calculate(
        decimal baseBonus,
        decimal categoryBonus,
        bool    useFocus,
        decimal achievementBonus = 0m,
        decimal journalBonus     = 0m)
    {
        var focusBonus = useFocus ? FocusLpb + achievementBonus : 0m;
        return FromLpb(baseBonus + categoryBonus + focusBonus + journalBonus);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>RRR = LPB / (1 + LPB)  — 0 si LPB ≤ 0.</summary>
    private static decimal FromLpb(decimal lpb)
        => lpb <= 0m ? 0m : lpb / (1m + lpb);
}
