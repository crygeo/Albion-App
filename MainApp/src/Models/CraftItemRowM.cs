using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ItemBaseVm = Albion_App.Components.Item.ItemBaseVm;

namespace Albion_App.Models;

/// <summary>Fila observable del ítem a craftear en Card 3.</summary>
public sealed partial class CraftItemRowM : ObservableObject
{
    public ItemBaseVm Item { get; init; } = null!;

    public string DisplayName => Item.DisplayName;

    /// <summary>Precio de mercado unitario del ítem crafteado (input del usuario).</summary>
    [ObservableProperty]
    private decimal _unitPrice;

    [RelayCommand]
    private void AutoPrecio() { }
}
