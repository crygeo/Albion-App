using Albion_App.Components.Market;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Albion_App.Models;

/// <summary>
/// Modelo observable para la fila de inventario de ingredientes (Card 3).
///
/// Porta el <see cref="ItemBaseVm"/> completo (con imagen reactiva) para que la
/// vista pueda bindear <c>{Binding Item.Image}</c> directamente.
///
/// El <see cref="RequiredCount"/> es inmutable por ciclo de cálculo:
/// si cambia la cantidad objetivo o la receta, el VM destruye la colección y
/// la reconstruye en lugar de mutar instancias existentes.
/// </summary>
public sealed partial class IngredientInventoryM : ObservableObject
{
    /// <summary>VM del ingrediente con imagen reactiva.</summary>
    public ItemBaseVm Item { get; init; } = null!;

    // ── Shorthands para bindings XAML limpios ────────────────────────────────
    public string ItemId      => Item.ItemId;
    public string DisplayName => Item.DisplayName;

    /// <summary>
    /// Cantidad total requerida para la cantidad objetivo solicitada.
    /// Calculado externamente: Ingredient.Count × QuantityToCraft.
    /// </summary>
    public int RequiredCount { get; init; }

    /// <summary>Cantidad que el jugador ya posee en inventario (editable por el usuario).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeededCount))]
    private int _ownedCount;

    /// <summary>
    /// Cantidad neta a comprar: max(0, RequiredCount − OwnedCount).
    /// Se notifica automáticamente cuando <see cref="OwnedCount"/> cambia.
    /// </summary>
    public int NeededCount => Math.Max(0, RequiredCount - OwnedCount);
}
