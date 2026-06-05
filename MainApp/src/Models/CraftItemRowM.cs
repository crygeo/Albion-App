using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ItemBaseVm = Albion_App.Components.Item.ItemBaseVm;

namespace Albion_App.Models;

/// <summary>Fila observable del ítem a craftear en Card 3.</summary>
public sealed partial class CraftItemRowM : ObservableObject
{
    public ItemBaseVm Item { get; init; } = null!;
    public string DisplayName => Item.DisplayName;

    [ObservableProperty]
    private decimal _unitPrice;

    // Delegates inyectados por CalculadoraSvm
    public Func<Task>? OnAutoPrice    { get; set; }
    public Func<Task>? OnOpenPriceDialog { get; set; }

    [RelayCommand]
    private Task AutoPrecio() => OnAutoPrice?.Invoke() ?? Task.CompletedTask;

    [RelayCommand]
    private Task OpenPriceDialog() => OnOpenPriceDialog?.Invoke() ?? Task.CompletedTask;
}
