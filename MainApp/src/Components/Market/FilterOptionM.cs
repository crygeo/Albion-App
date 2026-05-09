namespace Albion_App.Components.Market;

/// <summary>
/// Opción de filtro para los ComboBox de Nivel y Encantamiento.
/// Value == null representa "Todo" (sin filtro).
/// </summary>
public sealed record FilterOptionM(int? Value, string Label)
{
    public static readonly IReadOnlyList<FilterOptionM> LevelOptions =
    [
        new(null, "Nivel"),
        new(1,  "Nivel 1"),
        new(2,  "Nivel 2"),
        new(3,  "Nivel 3"),
        new(4,  "Nivel 4"),
        new(5,  "Nivel 5"),
        new(6,  "Nivel 6"),
        new(7,  "Nivel 7"),
        new(8,  "Nivel 8"),
    ];

    public static readonly IReadOnlyList<FilterOptionM> EnchantmentOptions =
    [
        new(null, "Encantamiento"),
        new(0,  "Encantamiento 0"),
        new(1,  "Encantamiento 1"),
        new(2,  "Encantamiento 2"),
        new(3,  "Encantamiento 3"),
        new(4,  "Encantamiento 4"),
    ];
}
