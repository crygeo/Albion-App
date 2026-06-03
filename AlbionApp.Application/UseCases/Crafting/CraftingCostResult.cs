namespace AlbionApp.Application.UseCases.Crafting;

/// <summary>
/// Resultado del cálculo de crafteo.
/// Inmutable, testeable sin WPF.
/// </summary>
public sealed record CraftingCostResult
{
    /// <summary>Tasa de retorno efectiva (0–1). Ej: 0.367 = 36.7%.</summary>
    public decimal ReturnRate { get; init; }

    /// <summary>Foco necesario por craft individual. Null si no se usa foco o la receta no tiene coste.</summary>
    public int? FocusPerCraft { get; init; }

    /// <summary>Foco total para la cantidad objetivo. Null si no se usa foco.</summary>
    public int? TotalFocusCost { get; init; }

    /// <summary>Reducción de foco aportada por el Destiny Board (0–100 %).</summary>
    public int FocusReductionPercent { get; init; }

    /// <summary>
    /// Foco ahorrado por craft gracias al bono especialista del hideout.
    /// Es la diferencia entre el foco sin y con bono especialista en el exponente.
    /// Null cuando no hay bono especialista activo.
    /// </summary>
    public int? FocusSpecialistSaving { get; init; }

    /// <summary>
    /// Filas de materiales necesarios.
    /// Vacío cuando no hay ciudad seleccionada (solo se calculó el foco).
    /// </summary>
    public IReadOnlyList<MaterialCostLine> Lines { get; init; } = [];

    /// <summary>Costo total de todos los materiales.</summary>
    public decimal TotalCost { get; init; }

    /// <summary>Resultado vacío (sin receta o sin ciudad).</summary>
    public static readonly CraftingCostResult Empty = new();
}

/// <summary>
/// Fila de material en el resultado.
/// El ViewModel se encarga de mapear <see cref="ItemId"/> al VM con imagen.
/// </summary>
public sealed record MaterialCostLine(
    string  ItemId,
    int     GrossQuantity,
    int     NetToBuy,
    int     ReturnedQuantity,
    string  BuyLocation,
    decimal UnitPrice)
{
    public decimal TotalCost     => NetToBuy * UnitPrice;
    public decimal ReturnedValue => ReturnedQuantity * UnitPrice;
}
