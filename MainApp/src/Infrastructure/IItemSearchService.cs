using AlbionApp.Domain.ItemSearch;
using ItemBaseVm = Albion_App.Components.Item.ItemBaseVm;

namespace Albion_App.Infrastructure;

/// <summary>
/// Abstracción del diálogo de búsqueda y fábrica de VMs de ítems.
/// Desacopla el ViewModel de la View concreta (<c>ItemSearchDialogV</c>).
/// </summary>
public interface IItemSearchService
{
    /// <summary>
    /// Abre el diálogo de búsqueda y espera la selección del usuario.
    /// Si se indica <paramref name="slotTypeFilter"/>, solo se muestran ítems
    /// cuyo SlotType coincida ("head", "mainhand", "potion", …).
    /// <paramref name="defaultTier"/> y <paramref name="defaultEnchantment"/> pre-seleccionan
    /// los filtros de nivel y encantamiento al abrir (se restauran al cerrar).
    /// Retorna el ítem seleccionado, o <c>null</c> si el usuario canceló.
    /// </summary>
    Task<ItemBaseVm?> SearchAsync(
        SlotType? slotTypeFilter       = null,
        int?      defaultTier          = null,
        int?      defaultEnchantment   = null);

    /// <summary>
    /// Construye un <see cref="ItemBaseVm"/> para un ingrediente dado su ID.
    /// Centraliza localización, imagen y fallback para IDs no encontrados.
    /// </summary>
    ItemBaseVm BuildItemVm(string itemId, CancellationToken ct);
}
