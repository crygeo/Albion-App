using ItemBaseVm = Albion_App.Components.Item.ItemBaseVm;

namespace Albion_App.Models;

public sealed class MaterialResultM
{
    public ItemBaseVm Item { get; init; } = null!;

    public string ItemId      => Item.ItemId;
    public string DisplayName => Item.DisplayName;

    public int     GrossQuantity    { get; init; }
    public int     NetQuantity      { get; init; }
    public int     ReturnedQuantity { get; init; }
    public string  BuyLocation      { get; init; } = string.Empty;
    public decimal UnitPrice        { get; init; }

    public decimal TotalCost      => NetQuantity * UnitPrice;
    public decimal GrossTotalCost => GrossQuantity * UnitPrice;
    public decimal ReturnedValue  => ReturnedQuantity * UnitPrice;

    public bool HasPrice    => UnitPrice > 0;
    public bool HasReturned => ReturnedQuantity > 0;
}
