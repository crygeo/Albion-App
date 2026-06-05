using AlbionApp.Domain.Market;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Albion_App._1Calculadora;

public sealed partial class PriceSelectionDialogVm : ObservableObject
{
    // ── Datos ─────────────────────────────────────────────────────────────────

    public string ItemDisplayName { get; init; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPrices))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private IReadOnlyList<CurrentCityPrice> _prices = [];

    [ObservableProperty] private bool    _isLoading;
    [ObservableProperty] private string? _error;

    public bool HasPrices => Prices.Count > 0;
    public bool IsEmpty   => !IsLoading && Prices.Count == 0 && Error is null;

    // ── Resultado ─────────────────────────────────────────────────────────────

    /// <summary>Precio seleccionado por el usuario. Null si canceló.</summary>
    public decimal? SelectedPrice { get; private set; }

    /// <summary>Invocado cuando el usuario selecciona un precio — cierra el diálogo.</summary>
    public Action? CloseDialog { get; set; }

    // ── Comandos ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private void SelectPrice(decimal price)
    {
        if (price <= 0) return;
        SelectedPrice = price;
        CloseDialog?.Invoke();
    }
}
