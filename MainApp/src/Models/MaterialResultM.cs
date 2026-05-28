using Albion_App.Components.Market;
using ItemBaseVm = Albion_App.Components.Item.ItemBaseVm;

namespace Albion_App.Models;

/// <summary>
/// Fila de resultado en la Card 4 (Resumen de Materiales).
/// Inmutable: se crea al ejecutar el cálculo y se reemplaza en cada recalculación.
///
/// Porta el <see cref="ItemBaseVm"/> completo para que la vista pueda bindear
/// <c>{Binding Item.Image}</c> sin converters.
///
/// <see cref="TotalCost"/> se computa inline dado que la fila es inmutable —
/// no existe escenario de mutación parcial.
/// </summary>
public sealed class MaterialResultM
{
    /// <summary>VM del material con imagen reactiva.</summary>
    public ItemBaseVm Item { get; init; } = null!;

    // ── Shorthands para bindings XAML limpios ────────────────────────────────
    public string ItemId      => Item.ItemId;
    public string DisplayName => Item.DisplayName;

    /// <summary>
    /// Cantidad neta a adquirir: bruto × cantidad objetivo − inventario propio,
    /// ajustada por la tasa de retorno esperada.
    /// </summary>
    public int NetQuantity { get; init; }

    /// <summary>Ciudad de compra sugerida (o la ciudad de crafteo seleccionada).</summary>
    public string BuyLocation { get; init; } = string.Empty;

    /// <summary>Precio unitario aproximado en plata. Cero si no hay precio disponible.</summary>
    public decimal UnitPrice { get; init; }

    /// <summary>Costo total = NetQuantity × UnitPrice.</summary>
    public decimal TotalCost => NetQuantity * UnitPrice;

    /// <summary>True si hay precio de mercado registrado para este ítem.</summary>
    public bool HasPrice => UnitPrice > 0;

    /// <summary>
    /// Ingredientes que sobran al finalizar todos los crafts
    /// (owned + comprado − consumo neto). Cero cuando no hay excedente.
    /// </summary>
    public int Surplus { get; init; }

    public bool HasSurplus => Surplus > 0;
}
