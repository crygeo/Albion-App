using AlbionApp.Domain.Interfaces.Services;
using LibAlbionGame.Interfaces;

namespace AlbionApp.Infrastructure.Services;

/// <summary>
/// Adapta <see cref="IItemDataService"/> (catálogo de ítems del dominio)
/// a <see cref="IItemIndexResolver"/> (interfaz de LibAlbionGame).
///
/// Permite que <see cref="LibAlbionGame.Trackers.PartyTracker"/> resuelva
/// los índices posicionales de red (int) a item IDs string sin depender
/// directamente de AlbionApp.Domain.
///
/// Registro: singleton — una instancia por lifetime de la app,
/// compartida entre PartyTracker y cualquier otro consumidor de LibAlbionGame.
/// </summary>
public sealed class ItemIndexResolverAdapter : IItemIndexResolver
{
    private readonly IItemDataService _items;

    public ItemIndexResolverAdapter(IItemDataService items)
        => _items = items;

    /// <inheritdoc/>
    public string? ResolveItemId(int index)
        => _items.GetByIndex(index)?.ItemId;

    /// <inheritdoc/>
    public bool IsTwoHanded(int index)
        => _items.GetByIndex(index)?.IsTwoHanded ?? false;
}
