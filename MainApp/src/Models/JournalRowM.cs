using AlbionApp.Domain.Crafting;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ItemBaseVm = Albion_App.Components.Item.ItemBaseVm;

namespace Albion_App.Models;

/// <summary>Fila observable del libro de laborer en Card 3.</summary>
public sealed partial class JournalRowM : ObservableObject
{
    public JournalItem Journal   { get; init; } = null!;
    public ItemBaseVm? JournalVm { get; init; }

    public string DisplayName    => JournalVm?.DisplayName ?? Journal.UniqueName;
    public int    BooksNeeded    { get; init; }
    public int    BooksCompleted { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(JournalIncome))]
    [NotifyPropertyChangedFor(nameof(TotalFullValue))]
    private decimal _fullPrice;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(JournalIncome))]
    [NotifyPropertyChangedFor(nameof(TotalEmptyValue))]
    private decimal _emptyPrice;

    public decimal TotalFullValue  => BooksCompleted * FullPrice;
    public decimal TotalEmptyValue => BooksNeeded    * EmptyPrice;
    public decimal JournalIncome   => TotalFullValue - TotalEmptyValue;

    // Delegates inyectados por CalculadoraSvm
    // Para el libro: Auto asigna FullPrice con Sell y EmptyPrice con Buy del mismo ítem
    public Func<Task>? OnAutoPrice      { get; set; }
    public Func<Task>? OnOpenPriceDialog { get; set; }

    [RelayCommand]
    private Task AutoPrecio() => OnAutoPrice?.Invoke() ?? Task.CompletedTask;

    [RelayCommand]
    private Task OpenPriceDialog() => OnOpenPriceDialog?.Invoke() ?? Task.CompletedTask;
}
