using System.Diagnostics.CodeAnalysis;
using AlbionApp.Domain.Crafting;
using AlbionApp.Domain.ItemSearch;

namespace AlbionApp.Domain.Interfaces.Services;

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
    /// Retorna todos los ítems (base + variantes de encantamiento) cuyos
    /// <c>BaseItemId</c> están en <paramref name="baseItemIds"/>.
    /// Diseñado para expandir resultados de búsqueda de texto: el índice de
    /// localización solo almacena el nombre base, por lo que la búsqueda devuelve
    /// IDs base; este método agrega automáticamente las variantes @N.
    /// </summary>
    IReadOnlyList<ItemBase> GetItemsByBaseIds(IReadOnlyList<string> baseItemIds);

    /// <summary>
    /// Resuelve el índice posicional que envía la red (CharacterEquipmentChanged,
    /// HealthUpdate, etc.) al <see cref="ItemBase"/> correspondiente.
    /// El índice es 1-based, orden XML, excluyendo &lt;shopcategories&gt;,
    /// variantes de encantamiento incluidas en la secuencia.
    /// Retorna <c>null</c> si el índice es 0, negativo o no existe.
    /// </summary>
    ItemBase? GetByIndex(int index);

    /// <summary>
    /// Retorna las recetas de crafteo del ítem indicado.
    /// Array vacío si el ítem no es crafteable o no existe en catálogo.
    /// Nunca retorna null.
    /// </summary>
    IRecipe[] GetRecipes(string itemId);

    /// <summary>
    /// Retorna el libro de laborer que se llena al craftear el ítem indicado,
    /// o null si el ítem no aparece en ningún craftitemfame journal.
    /// Acepta IDs con encantamiento (@N) — los normaliza automáticamente.
    /// </summary>
    JournalItem? GetJournalForItem(string itemId);
}
