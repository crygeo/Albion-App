using System.Runtime.CompilerServices;
using AlbionApp.Application.Search;
using AlbionApp.Domain.Interfaces;
using AlbionApp.Domain.Interfaces.Services;
using AlbionApp.Domain.ItemSearch;
using AlbionApp.Domain.Search;

namespace AlbionApp.Application.UseCases.SearchItems;

/// <summary>
/// Implementación del caso de uso de búsqueda de ítems.
///
/// <para><b>Pipeline (todas las fases en background, fuera del hilo UI)</b>:</para>
/// <list type="number">
///   <item><b>Parse</b>: el texto raw se descompone en tokens + tier/enchantment
///         estructurales via <see cref="QueryParser"/>.</item>
///   <item><b>Resolve filters</b>: tier/enchantment del dropdown tienen
///         precedencia sobre los del texto (decisión de UX validada).</item>
///   <item><b>Candidate set</b>: si hay tokens de texto, se consulta el índice
///         de localización; si no, se consume el catálogo completo o sólo los
///         primeros N ítems si tampoco hay filtros estructurales.</item>
///   <item><b>Post-filter</b>: filtros sobre <c>ItemBase</c> (fuente de verdad,
///         no la clave TMX) — categoría por rango, tier exacto, encantamiento.</item>
///   <item><b>Sort &amp; project</b>: ordenamiento estable por categoría → tier →
///         encantamiento → ItemId, take(N), proyección a <see cref="ItemSearchHit"/>.</item>
/// </list>
///
/// <para><b>Por qué <c>Task.Run</c></b>: el caso de uso se invoca desde el hilo
/// UI vía debounce. Mover el pipeline completo al thread pool garantiza que el
/// UI siga respondiendo durante búsquedas con 20k candidatos. La cancelación se
/// honra en cada fase mediante <see cref="CancellationToken"/>.</para>
///
/// <para><b>Sin estado</b>: la implementación es thread-safe — múltiples
/// invocaciones concurrentes son seguras (los servicios subyacentes son
/// inmutables tras el startup).</para>
/// </summary>
public sealed class SearchItemsUseCase : ISearchItemsUseCase
{
    private readonly IItemDataService     _itemDataService;
    private readonly ILocalizationService _localizationService;

    public SearchItemsUseCase(
        IItemDataService     itemDataService,
        ILocalizationService localizationService)
    {
        _itemDataService     = itemDataService     ?? throw new ArgumentNullException(nameof(itemDataService));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
    }

    public async Task<SearchItemsResult> ExecuteAsync(SearchItemsQuery query, CancellationToken ct = default)
    {
        // Implementado como collector del stream para evitar duplicar la lógica
        // del pipeline. El costo extra del IAsyncEnumerable es despreciable
        // (~unos pocos microsegundos por la state machine).
        var hits = new List<ItemSearchHit>(query.MaxResults > 0 ? query.MaxResults : 64);
        await foreach (var hit in StreamAsync(query, ct).ConfigureAwait(false))
            hits.Add(hit);
        return new SearchItemsResult(hits);
    }

    public async IAsyncEnumerable<ItemSearchHit> StreamAsync(
        SearchItemsQuery                            query,
        [EnumeratorCancellation] CancellationToken  ct = default)
    {
        // 1-4. Parse + resolve + filter + order + take. Todo el trabajo CPU-bound
        //      se ejecuta en el thread pool ANTES del primer yield. Esto garantiza
        //      orden global (no emitimos hits en orden parcial) y evita que la
        //      preparación bloquee el hilo del consumidor (UI).
        var ordered = await Task.Run(() => PrepareOrderedItems(query, ct), ct)
                                .ConfigureAwait(false);

        // 5. Proyección streaming. La proyección por hit es O(1) y barata —
        //    cada yield cede el control al consumidor para que pueda materializar
        //    e intercalar trabajo de UI entre lotes.
        foreach (var item in ordered)
        {
            ct.ThrowIfCancellationRequested();
            yield return Project(item);
        }
    }

    // ── Pipeline síncrono (CPU-bound) ─────────────────────────────────────────

    /// <summary>
    /// Ejecuta el pipeline completo de parse + resolve + filter + sort + take y
    /// devuelve la lista materializada de <see cref="ItemBase"/> en el orden final.
    /// La proyección a <see cref="ItemSearchHit"/> queda fuera porque el consumidor
    /// la hace per-item durante el yield.
    /// </summary>
    private List<ItemBase> PrepareOrderedItems(SearchItemsQuery query, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // 1. Parsear el texto. Tier/enchantment del texto se usan sólo si los
        //    dropdowns no tienen valor — los dropdowns ganan en precedencia.
        var parsed    = query.RawText.Parse();
        var tier      = parsed.Tier ?? query.TierFilter;
        var enchant   = parsed.Enchantment ?? query.EnchantmentFilter;
        var effective = new SearchQuery(parsed.TextTokens, tier, enchant);

        // 2. Resolver candidatos (índice de texto vs catálogo completo).
        var candidates = ResolveCandidates(effective, query, ct);
        ct.ThrowIfCancellationRequested();

        // 3. Aplicar post-filtros estructurales.
        var filtered = candidates.Filter(
            minCategory: query.MinCategoryValue,
            maxCategory: query.MaxCategoryValue,
            tier:        effective.Tier,
            enchantment: effective.Enchantment);

        // 4. Ordenar y limitar.
        IEnumerable<ItemBase> ordered = filtered
            .Where(i => query.SlotTypeFilter is null or SlotType.None || i.SlotType == query.SlotTypeFilter)
            .OrderBy(i => i.CategoryValue)
            .ThenBy(i => i.Tier ?? int.MaxValue)
            .ThenBy(i => i.EnchantmentLevel)
            .ThenBy(i => i.ItemId, StringComparer.OrdinalIgnoreCase);

        if (query.MaxResults > 0)
            ordered = ordered.Take(query.MaxResults);

        // Materializar antes de yield-ear: necesario para que el orden completo
        // esté determinado y para liberar la cadena LINQ (que cierra sobre los
        // objetos de query) antes de devolver el control al consumidor.
        return ordered.ToList();
    }

    // ── Resolución de candidatos ──────────────────────────────────────────────

    /// <summary>
    /// Decide la fuente de candidatos según el patrón de la query:
    /// <list type="bullet">
    ///   <item>Con texto → consulta el índice de localización.</item>
    ///   <item>Sin texto, con filtros estructurales → catálogo completo (los filtros lo acotarán).</item>
    ///   <item>Sin texto y sin filtros → primeros N ítems (vista por defecto).</item>
    /// </list>
    /// </summary>
    private IReadOnlyList<ItemBase> ResolveCandidates(
        SearchQuery       query,
        SearchItemsQuery  request,
        CancellationToken ct)
    {
        if (query.HasTextTokens)
        {
            // El índice de localización solo tiene entradas para ítems base
            // (el TMX no duplica el nombre en cada variante @N). Usamos
            // GetItemsByBaseIds para incluir automáticamente las variantes de
            // encantamiento que comparten el mismo BaseItemId.
            var baseItemIds = _localizationService.ItemSearchIndex
                .Filter(query.TextTokens, _localizationService.CurrentSupportedLanguage.Code);
            return _itemDataService.GetItemsByBaseIds(baseItemIds);
        }

        bool hasStructuralFilter = request.MinCategoryValue.HasValue
                                || query.Tier.HasValue
                                || query.Enchantment.HasValue;

        if (hasStructuralFilter)
            return _itemDataService.GetAll();

        // Sin texto + sin filtros: vista por defecto. Limitar para no proyectar 20k VMs.
        var limit = request.MaxResults > 0 ? request.MaxResults : DefaultEmptyQueryLimit;
        return _itemDataService.GetFirst(limit);
    }

    // ── Proyección Domain → DTO ───────────────────────────────────────────────

    private static ItemSearchHit Project(ItemBase item)
        => new(
            ItemId:              item.ItemId,
            BaseItemId:          item.BaseItemId,
            NameLocalizationKey: item.NameLocalizationKey,
            Tier:                item.Tier,
            EnchantmentLevel:    item.EnchantmentLevel,
            CategoryValue:       item.CategoryValue,
            TierLabel:           item.TierLabel);

    // ── Constantes ────────────────────────────────────────────────────────────

    private const int DefaultEmptyQueryLimit = 300;
}
