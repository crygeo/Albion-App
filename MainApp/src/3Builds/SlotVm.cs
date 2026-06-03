using Albion_App.Components.Item;
using AlbionApp.Domain.Interfaces;
using AlbionApp.Domain.ItemSearch;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Albion_App.Infrastructure;
using AlbionApp.Domain.Interfaces.Services;

namespace Albion_App._3Builds;

/// <summary>
/// ViewModel de un slot de equipamiento en el editor de builds.
/// Gestiona selección de ítem, validación de tipo de slot y cantidad (para consumibles).
/// </summary>
public partial class SlotVm : ObservableObject
{
    private readonly IItemSearchService  _search;
    private readonly IItemDataService    _itemData;

    public string   Label       { get; }
    public SlotType SlotType    { get; }
    public int      MaxQuantity { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasItem))]
    [NotifyPropertyChangedFor(nameof(DisplayText))]
    private ItemBaseVm? _item;

    [ObservableProperty] private int  _quantity = 1;
    [ObservableProperty] private bool _isLocked;

    public bool   HasItem     => Item is not null;
    public bool   HasQuantity => MaxQuantity > 1;
    public string DisplayText => Item?.ShortLabel ?? "—";
    public string? ItemId     => Item?.ItemId;

    public SlotVm(string label, SlotType slotType, int maxQuantity,
                  IItemSearchService search, IItemDataService itemData)
    {
        Label       = label;
        SlotType    = slotType;
        MaxQuantity = maxQuantity;
        _search     = search;
        _itemData   = itemData;
    }

    // ── Comandos ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task Pick()
    {
        if (IsLocked) return;

        // SlotType.None = slot "extra" sin restricción de tipo → sin filtro en el buscador.
        SlotType? filter = SlotType == SlotType.None ? null : SlotType;
        var vm = await _search.SearchAsync(filter, defaultTier: 8, defaultEnchantment: 0);
        if (vm is null) return;

        var item = _itemData.GetById(vm.ItemId);
        if (item is null) return;

        // Guardia de seguridad: el diálogo ya filtra por slot, pero validamos
        // igualmente para evitar asignaciones inválidas si el filtro falla.
        if (!IsValidForSlot(item.SlotType)) return;

        Item = vm;
    }

    [RelayCommand]
    private void Clear()
    {
        Item     = null;
        Quantity = 1;
    }

    [RelayCommand]
    private void Increment()
    {
        if (HasQuantity && Quantity < MaxQuantity) Quantity++;
    }

    [RelayCommand]
    private void Decrement()
    {
        if (HasQuantity && Quantity > 1) Quantity--;
    }

    // ── API pública para el editor ────────────────────────────────────────────

    public void SetItem(ItemBaseVm? vm) => Item = vm;

    public void Reset()
    {
        Item     = null;
        Quantity = 1;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Valida si un <paramref name="itemSlot"/> puede equiparse en este slot.
    /// MainHand acepta armas 1H y 2H (ambas tienen SlotType.MainHand en el XML).
    /// OffHand solo acepta armas con SlotType.OffHand.
    /// </summary>
    /// <summary>
    /// SlotType.None = slot extra genérico → acepta cualquier ítem.
    /// Resto de slots solo aceptan ítems del mismo SlotType.
    /// </summary>
    private bool IsValidForSlot(SlotType itemSlot) =>
        SlotType == SlotType.None || itemSlot == SlotType;
}
