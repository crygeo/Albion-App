using System.Diagnostics.CodeAnalysis;
using AlbionApp.Domain.ItemSearch;

namespace AlbionApp.Domain.Interfaces;

/// <summary>
/// Contrato del catálogo de ítems de Albion Online.
///
/// Proporciona acceso de solo lectura al catálogo completo una vez que el
/// servicio está activo. Todos los métodos son O(1) o O(n) sobre índices
/// preconstruidos en FrozenDictionary — sin IO ni parseo en tiempo de llamada.
/// </summary>
public interface IItemDataService
{
    /// <summary>
    /// Retorna todos los ítems del catálogo (~20 k ítems, incluye variantes de encantamiento).
    /// </summary>
    IReadOnlyList<ItemBase> GetAll();

    /// <summary>
    /// Retorna los primeros <paramref name="count"/> ítems del catálogo.
    /// Usado como vista por defecto cuando no hay filtro activo.
    /// </summary>
    IReadOnlyList<ItemBase> GetFirst(int count);

    /// <summary>
    /// Busca un ítem por <paramref name="itemId"/> (incluye variantes @N).
    /// Retorna <c>null</c> si el ID no existe en el catálogo.
    /// </summary>
    ItemBase? GetById(string itemId);

    /// <summary>
    /// Intenta obtener un ítem por <paramref name="itemId"/>.
    /// Retorna <c>true</c> y popula <paramref name="item"/> si existe.
    /// </summary>
    bool TryGetById(string itemId, [NotNullWhen(true)] out ItemBase? item);

    /// <summary>
    /// Batch lookup por lista de IDs. Los IDs no encontrados se omiten silenciosamente.
    /// </summary>
    IReadOnlyList<ItemBase> GetItemsByIds(IReadOnlyList<string> itemIds);

    /// <summary>
    /// Retorna las recetas de crafteo del ítem indicado.
    /// Array vacío si el ítem no es crafteable o no existe en catálogo.
    /// Nunca retorna null.
    /// </summary>
    IRecipe[] GetRecipes(string itemId);
}
