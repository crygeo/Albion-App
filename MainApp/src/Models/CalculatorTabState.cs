namespace Albion_App.Models;

/// <summary>
/// Estado serializable de una pestaña individual del workspace.
/// Contiene todos los ajustes específicos de esa pestaña — nada se comparte
/// con otras pestañas a través de este objeto.
/// </summary>
public sealed class CalculatorTabState
{
    public string? ItemId   { get; set; }
    public int     Quantity { get; set; } = 1;
    public string  Title    { get; set; } = "Nueva pestaña";

    // ── Ajustes per-tab (antes en calculator_settings.json, ahora per-pestaña) ──
    public bool    UseFocus           { get; set; }
    public bool    UseSpecialistBonus { get; set; }
    public int     HideoutLevel       { get; set; } = 1;
    public decimal JournalBonusValue  { get; set; } = 0m;
    public string? CityClusterId      { get; set; }
}
