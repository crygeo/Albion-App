using AlbionApp.Application.UseCases.Crafting;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Albion_App._1Calculadora;

public sealed partial class StickyResultsBarVm : ObservableObject
{
    [ObservableProperty] private decimal _profitLoss;
    [ObservableProperty] private decimal _roi;
    [ObservableProperty] private string  _quantityLabel = "0";
    [ObservableProperty] private double  _fameBase;

    // true cuando hay datos reales (ítem cargado y calculado)
    [ObservableProperty] private bool _hasData;

    public void UpdateFromQuantityResult(QuantityResult result, int quantity)
    {
        ProfitLoss    = result.ProfitLoss;
        Roi           = result.Roi;
        QuantityLabel = quantity.ToString("N0");
        FameBase      = result.FameBase;
        HasData       = result.Lines.Count > 0 || result.FocusPerCraft.HasValue;
    }

    public void Reset()
    {
        ProfitLoss    = 0m;
        Roi           = 0m;
        QuantityLabel = "0";
        FameBase      = 0.0;
        HasData       = false;
    }
}
