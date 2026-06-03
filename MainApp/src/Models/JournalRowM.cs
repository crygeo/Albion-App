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
    public int    BooksNeeded   { get; init; }
    /// <summary>Libros completamente llenos (floor). Solo estos se pueden vender.</summary>
    public int    BooksCompleted { get; init; }

    /// <summary>Precio de mercado del libro lleno.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(JournalIncome))]
    [NotifyPropertyChangedFor(nameof(TotalFullValue))]
    private decimal _fullPrice;

    /// <summary>Precio de mercado del libro vacío.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(JournalIncome))]
    [NotifyPropertyChangedFor(nameof(TotalEmptyValue))]
    private decimal _emptyPrice;

    /// <summary>Solo los libros completos generan ingreso al venderse.</summary>
    public decimal TotalFullValue      => BooksCompleted * FullPrice;
    /// <summary>Todos los libros necesarios se compran vacíos.</summary>
    public decimal TotalEmptyValue     => BooksNeeded * EmptyPrice;
    /// <summary>Ingreso neto = llenos vendidos − vacíos comprados.</summary>
    public decimal JournalIncome       => TotalFullValue - TotalEmptyValue;

    [RelayCommand]
    private void AutoPrecio() { }
}
