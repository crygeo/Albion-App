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
    /// Retorna el ítem seleccionado, o <c>null</c> si el usuario canceló.
    /// </summary>
    Task<ItemBaseVm?> SearchAsync();

    /// <summary>
    /// Construye un <see cref="ItemBaseVm"/> para un ingrediente dado su ID.
    /// Centraliza localización, imagen y fallback para IDs no encontrados.
    /// </summary>
    ItemBaseVm BuildItemVm(string itemId, CancellationToken ct);
}
