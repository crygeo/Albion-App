using System.Collections.Concurrent;
using Albion_App.Interfaces;
using AlbionApp.Application.UseCases.SearchItems;
using AlbionApp.Domain.Interfaces;
using AlbionApp.Domain.Interfaces.Services;
using AlbionApp.Domain.ItemSearch;

namespace Albion_App.Components.Item;

/// <summary>
/// Factory de ItemBaseVm con reutilización transparente.
///
/// Estrategia:
/// - Reutiliza instancias existentes por ItemId.
/// - Usa WeakReference para evitar retención permanente.
/// - Evita reconstrucción de imágenes y re-suscripciones
///   innecesarias a LocalizationChanged.
/// - Thread-safe para uso concurrente.
///
/// El GC puede liberar VMs no utilizadas.
/// </summary>
public sealed class ItemBaseVmFactory
    : IDisplayVmFactory<ItemSearchHit, ItemBaseVm>,
      IDisposable
{
    private readonly IItemDataService _items;
    private readonly ILocalizationService _localization;
    private readonly IAlbionImageService _images;

    /// <summary>
    /// Cache liviano de VMs.
    /// WeakReference evita leaks permanentes.
    /// </summary>
    private readonly ConcurrentDictionary<
        string,
        WeakReference<ItemBaseVm>> _cache = new();

    public ItemBaseVmFactory(
        IItemDataService items,
        ILocalizationService localization,
        IAlbionImageService images)
    {
        _items = items
            ?? throw new ArgumentNullException(nameof(items));

        _localization = localization
            ?? throw new ArgumentNullException(nameof(localization));

        _images = images
            ?? throw new ArgumentNullException(nameof(images));
    }

    // ─────────────────────────────────────────────────────────────
    // Creación principal
    // ─────────────────────────────────────────────────────────────

    public ItemBaseVm Create(
        ItemSearchHit hit,
        CancellationToken ct = default)
    {
        // Fast path:
        // ya existe una instancia viva.
        if (_cache.TryGetValue(hit.ItemId, out var weak) &&
            weak.TryGetTarget(out var cachedVm))
        {
            return cachedVm;
        }

        // Crear VM nueva.
        var vm = new ItemBaseVm(hit, _localization);

        // Fire-and-forget seguro.
        _ = vm.LoadImageAsync(_images, ct);

        // Registrar en cache.
        _cache[hit.ItemId] = new(vm);

        return vm;
    }

    // ─────────────────────────────────────────────────────────────
    // Creación por ItemId
    // ─────────────────────────────────────────────────────────────

    public ItemBaseVm CreateById(
        string itemId,
        CancellationToken ct = default)
    {
        // Fast path primero.
        if (_cache.TryGetValue(itemId, out var weak) &&
            weak.TryGetTarget(out var cachedVm))
        {
            return cachedVm;
        }

        var hit = _items.TryGetById(itemId, out var item)
            ? Project(item)
            : Placeholder(itemId);

        return Create(hit, ct);
    }

    // ─────────────────────────────────────────────────────────────
    // Limpieza opcional de referencias muertas
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Limpia entradas ya recolectadas por GC.
    /// Puede llamarse periódicamente.
    /// </summary>
    public void CompactCache()
    {
        foreach (var pair in _cache)
        {
            if (!pair.Value.TryGetTarget(out _))
            {
                _cache.TryRemove(pair.Key, out _);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Dispose
    // ─────────────────────────────────────────────────────────────

    public void Dispose()
    {
        foreach (var weak in _cache.Values)
        {
            if (weak.TryGetTarget(out var vm))
            {
                vm.Dispose();
            }
        }

        _cache.Clear();
    }

    // ─────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────

    private static ItemSearchHit Project(ItemBase item)
        => new(
            ItemId: item.ItemId,
            BaseItemId: item.BaseItemId,
            NameLocalizationKey: item.NameLocalizationKey,
            Tier: item.Tier,
            EnchantmentLevel: item.EnchantmentLevel,
            CategoryValue: item.CategoryValue,
            TierLabel: item.TierLabel);

    private static ItemSearchHit Placeholder(string itemId)
        => new(
            ItemId: itemId,
            BaseItemId: itemId,
            NameLocalizationKey: $"@ITEMS_{itemId}",
            Tier: null,
            EnchantmentLevel: 0,
            CategoryValue: 0,
            TierLabel: string.Empty);
}