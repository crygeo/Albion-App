using AlbionApp.Application.UseCases.SearchItems;
using AlbionApp.Domain.Interfaces;
using AlbionApp.Domain.ItemSearch;

namespace Albion_App.Components.Market;

/// <summary>
/// Fábrica especializada en construir <see cref="ItemBaseVm"/>.
///
/// <para><b>Decisión arquitectónica</b>: la creación de un VM requiere combinar
/// tres servicios (<see cref="IItemDataService"/>, <see cref="ILocalizationService"/>,
/// <see cref="IItemImageService"/>). Empotrar esa orquestación en cada caller
/// (MarketVm, ItemSearchVM, CalculadoraSVM…) viola DRY y dispersa el conocimiento
/// de cómo se materializa un VM. Centralizarlo en una fábrica explícita permite:</para>
///
/// <list type="bullet">
///   <item>Reutilización real entre múltiples ViewModels sin re-inyectar servicios.</item>
///   <item>Testabilidad: un único punto a mockear para verificar construcción de VMs.</item>
///   <item>Extensibilidad: futuras decoraciones (telemetría, prefetch, caching de
///         imágenes en memoria) se aplican aquí sin tocar callers.</item>
/// </list>
///
/// <para>Sin estado, thread-safe (los servicios subyacentes son inmutables tras
/// el startup) — apto para inyectarse como singleton.</para>
/// </summary>
public sealed class ItemBaseVmFactory
{
    private readonly IItemDataService     _items;
    private readonly ILocalizationService _localization;
    private readonly IItemImageService    _images;

    public ItemBaseVmFactory(
        IItemDataService     items,
        ILocalizationService localization,
        IItemImageService    images)
    {
        _items        = items        ?? throw new ArgumentNullException(nameof(items));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _images       = images       ?? throw new ArgumentNullException(nameof(images));
    }

    /// <summary>
    /// Crea un VM a partir de un hit de búsqueda. Lanza la carga de imagen en
    /// fire-and-forget bajo el <paramref name="ct"/> del caller.
    /// </summary>
    public ItemBaseVm Create(ItemSearchHit hit, CancellationToken ct = default)
    {
        var vm = new ItemBaseVm(hit, _localization);
        _ = vm.LoadImageAsync(_images, ct);
        return vm;
    }

    /// <summary>
    /// Crea un VM a partir de un <c>itemId</c>. Si el ítem existe en el catálogo
    /// se proyecta a <see cref="ItemSearchHit"/>; en caso contrario se construye
    /// un placeholder con la clave de localización estándar para no romper la UI.
    /// </summary>
    public ItemBaseVm CreateById(string itemId, CancellationToken ct = default)
    {
        var hit = _items.TryGetById(itemId, out var item)
            ? Project(item)
            : Placeholder(itemId);

        return Create(hit, ct);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ItemSearchHit Project(ItemBase item)
        => new(
            ItemId:              item.ItemId,
            BaseItemId:          item.BaseItemId,
            NameLocalizationKey: item.NameLocalizationKey,
            Tier:                item.Tier,
            EnchantmentLevel:    item.EnchantmentLevel,
            CategoryValue:       item.CategoryValue,
            TierLabel:           item.TierLabel);

    private static ItemSearchHit Placeholder(string itemId)
        => new(
            ItemId:              itemId,
            BaseItemId:          itemId,
            NameLocalizationKey: $"@ITEMS_{itemId}",
            Tier:                null,
            EnchantmentLevel:    0,
            CategoryValue:       0,
            TierLabel:           string.Empty);
}
