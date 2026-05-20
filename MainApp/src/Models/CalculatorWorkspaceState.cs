namespace Albion_App.Models;

/// <summary>
/// Estado completo del workspace de la calculadora.
/// Serializado a JSON al cerrar la app y restaurado al abrir.
/// </summary>
public sealed class CalculatorWorkspaceState
{
    /// <summary>Índice de la pestaña activa al momento de cerrar la app.</summary>
    public int SelectedTabIndex { get; set; }

    /// <summary>Estado de cada pestaña en orden de aparición.</summary>
    public List<CalculatorTabState> Tabs { get; set; } = [];
}
