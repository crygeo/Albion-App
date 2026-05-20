using AlbionApp.Application.UseCases.Player;
using AlbionApp.Domain.Crafting;
using AlbionApp.Domain.ItemSearch;

namespace AlbionApp.Application.UseCases.Crafting;

/// <summary>
/// Input del cálculo de crafteo.
/// Inmutable, testeable sin WPF.
/// </summary>
public sealed record CraftingCostRequest
{
    /// <summary>Receta seleccionada (carrusel Card 2).</summary>
    public required IRecipe Recipe { get; init; }

    /// <summary>
    /// Ciudad de crafteo. Null cuando aún no se ha seleccionado ninguna;
    /// en ese caso el UseCase devuelve solo el costo de foco.
    /// </summary>
    public CraftingCityData? City { get; init; }

    /// <summary>Cantidad objetivo de ítems a craftear.</summary>
    public int Quantity { get; init; } = 1;

    /// <summary>Categoría de crafteo del ítem (ej: "leather_shoes", "ore").</summary>
    public string? CraftingCategory { get; init; }

    /// <summary>
    /// Valor del ítem en plata (<c>itemvalue</c> del XML).
    /// Se usa para normalizar el costo de foco: <c>focusBase = CraftingFocus / ItemValue</c>.
    /// El XML almacena focus en unidades internas; dividir por itemvalue da el valor en pantalla.
    /// </summary>
    public int ItemValue { get; init; } = 1;

    /// <summary>Si el jugador usa Focus en este ciclo.</summary>
    public bool UseFocus { get; init; }

    /// <summary>Bono de diario de rendimiento (0.10 = 10%, 0.20 = 20%).</summary>
    public decimal JournalBonus { get; init; }

    /// <summary>
    /// Bonuses del Destiny Board del jugador.
    /// Vacío si el jugador no ha cargado su cuenta todavía.
    /// </summary>
    public IReadOnlyList<AggregatedBonus> AchievementBonuses { get; init; } = [];

    /// <summary>Stock actual del jugador por ingrediente.</summary>
    public IReadOnlyList<IngredientStock> OwnedStock { get; init; } = [];
}

/// <summary>Inventario del jugador para un ingrediente específico.</summary>
public sealed record IngredientStock(
    string  ItemId,
    int     OwnedCount,
    decimal UnitPrice);
