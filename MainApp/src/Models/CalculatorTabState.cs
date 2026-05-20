namespace Albion_App.Models;

/// <summary>
/// Estado serializable de una pestaña individual del workspace.
/// No contiene lógica de UI — es un DTO puro para persistencia.
/// </summary>
public sealed class CalculatorTabState
{
    /// <summary>ID del ítem seleccionado (ej. "T6_CLOTH"). Null si la pestaña está vacía.</summary>
    public string? ItemId { get; set; }

    /// <summary>Cantidad a fabricar configurada por el usuario.</summary>
    public int Quantity { get; set; } = 1;

    /// <summary>
    /// Título visible en la pestaña.
    /// Se almacena para mostrarlo durante la restauración antes de que el ítem cargue.
    /// </summary>
    public string Title { get; set; } = "Nueva pestaña";
}
