using System.Collections.Frozen;
using System.Xml.Linq;
using AlbionApp.Domain.Interfaces;
using AlbionApp.Domain.Localization;
using AlbionApp.Domain.Search;
using AlbionApp.Domain.Text;
using AlbionApp.Infrastructure.Search;
using LibAlbionData;
using LibAlbionData.Core;
using LibServices;

namespace AlbionApp.Infrastructure.Services;

/// <summary>
/// Servicio de localización con doble responsabilidad:
///
/// <para><b>1. Storage principal: GetText(key)</b></para>
/// <code>FrozenDictionary&lt;string tuid, IReadOnlyDictionary&lt;string lang, string text&gt;&gt;</code>
/// Lookup O(1) para el caso de uso más frecuente (resolver nombres de ítems en VMs).
///
/// <para><b>2. Índices de búsqueda secundarios: GetTexts(query, filter)</b></para>
/// <code>FrozenDictionary&lt;string lang, SearchEntry[]&gt;</code>
/// Un índice por dominio semántico (<see cref="SearchDomain"/>). Permite búsqueda híbrida
/// (texto + metadata estructural) sin recorrer el diccionario principal completo.
///
/// <para><b>Proceso de construcción del índice (startup):</b></para>
/// <list type="number">
///   <item>Se parsea el TMX completo y se construye el storage principal.</item>
///   <item>Se itera el storage y se filtran las claves por prefijo de dominio.</item>
///   <item>Para cada clave del dominio se crea un <see cref="SearchEntry"/> con:
///     <c>NormalizedText</c> pre-normalizado y <c>Metadata</c> pre-parseada.</item>
///   <item>Las entradas se agrupan por idioma y se congelan en FrozenDictionary.</item>
/// </list>
///
/// <para><b>Thread-safety:</b></para>
/// Ambos almacenamientos son inmutables tras <c>OnStartAsync</c>. Lecturas concurrentes
/// lock-free. <c>SetLanguage</c> actualiza <c>_currentSupportedLanguage</c> de forma
/// atómica — <c>GetTexts</c> captura el idioma al inicio de la búsqueda.
///
/// <para><b>Decisión de diseño: índices en el mismo servicio</b></para>
/// Los índices se construyen aquí (y no en un servicio separado) porque:
/// (a) el costo marginal es O(n) en el parseo ya realizado — sin IO adicional,
/// (b) evita duplicar el almacenamiento de 200k entradas,
/// (c) el idioma activo y el índice a consultar son inseparables.
/// </summary>
public sealed class LocalizationService(
    AlbionData         albionData,
    SupportedLanguage? initialLanguage = null)
    : ServiceBase, ILocalizationService
{
    // ── Constantes de dominio (prefijos TMX del juego) ────────────────────────

    private const string ItemsKeyPrefix  = "@ITEMS_";
    private const string MarketKeyPrefix = "@MARKETPLACEGUI_ROLLOUT_";

    // ── Fallback ──────────────────────────────────────────────────────────────

    private static readonly SupportedLanguage FallbackLanguage =
        SupportedLanguage.All["EN-US"];

    // ── Storage principal: tuid → (lang → text) ───────────────────────────────

    private FrozenDictionary<string, IReadOnlyDictionary<string, string>> _localization =
        FrozenDictionary<string, IReadOnlyDictionary<string, string>>.Empty;

    // ── Índices de búsqueda: lang → SearchEntry[] ─────────────────────────────

    /// <summary>Índice del dominio @ITEMS_</summary>
    private FrozenDictionary<string, SearchEntry[]> _itemSearchIndex =
        FrozenDictionary<string, SearchEntry[]>.Empty;

    /// <summary>Índice del dominio @MARKETPLACEGUI_ROLLOUT_</summary>
    private FrozenDictionary<string, SearchEntry[]> _marketSearchIndex =
        FrozenDictionary<string, SearchEntry[]>.Empty;

    // ── Estado de idioma ──────────────────────────────────────────────────────

    private IReadOnlyList<SupportedLanguage> _availableLanguages =
        Array.Empty<SupportedLanguage>();

    private SupportedLanguage _currentSupportedLanguage =
        initialLanguage ?? FallbackLanguage;

    // ── IAlbionService ────────────────────────────────────────────────────────

    public override string ServiceName => "LocalizationService";

    // ── Acceso directo al índice (para VMs que evitan GetTexts) ──────────────

    /// <summary>
    /// Índice de búsqueda del dominio Items listo para usar con
    /// <c>SearchIndexExtensions.Filter(query, language)</c>.
    /// Disponible después de <c>StartAsync</c>; vacío antes del arranque.
    /// </summary>
    public FrozenDictionary<string, SearchEntry[]> ItemSearchIndex => _itemSearchIndex;

    // ── ILocalizationService ──────────────────────────────────────────────────

    public SupportedLanguage CurrentSupportedLanguage
    {
        get => _currentSupportedLanguage;
        private set => _currentSupportedLanguage = value;
    }

    public IReadOnlyList<SupportedLanguage> AvailableLanguages => _availableLanguages;

    public event EventHandler? LanguageChanged;

    /// <inheritdoc/>
    public string GetText(string key)
    {
        var store = _localization;

        if (!store.TryGetValue(key, out var langMap))
            return key;

        var currentCode = CurrentSupportedLanguage.Code;

        if (langMap.TryGetValue(currentCode, out var text) &&
            !string.IsNullOrWhiteSpace(text))
            return text;

        if (langMap.TryGetValue(FallbackLanguage.Code, out var fallback) &&
            !string.IsNullOrWhiteSpace(fallback))
            return fallback;

        foreach (var value in langMap.Values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return key;
    }

    

    /// <inheritdoc/>
    public void SetLanguage(SupportedLanguage supportedLanguage)
    {
        ArgumentNullException.ThrowIfNull(supportedLanguage);

        if (CurrentSupportedLanguage == supportedLanguage) return;

        CurrentSupportedLanguage = supportedLanguage;
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── Ciclo de vida ─────────────────────────────────────────────────────────

    protected override async Task OnStartAsync(CancellationToken ct)
    {
        SetProgress(0);

        var doc = albionData.GetXDocument(GameDataPath.Localization);

        SetProgress(10);

        // Fase 1: parsear el TMX completo — O(n) con n ≈ 200k entradas
        var (localization, languages) = await Task
            .Run(() => ParseTmx(doc, ct), ct)
            .ConfigureAwait(false);

        SetProgress(60);

        // Fase 2: construir índices de búsqueda en background
        // Los índices se construyen sobre el storage ya en memoria — sin IO adicional.
        var (itemIndex, marketIndex) = await Task
            .Run(() => BuildSearchIndices(localization, ct), ct)
            .ConfigureAwait(false);

        // Asignar los tres almacenamientos atómicamente (desde el hilo llamante)
        _localization       = localization;
        _itemSearchIndex    = itemIndex;
        _marketSearchIndex  = marketIndex;

        _availableLanguages = languages
            .Select(code => SupportedLanguage.All.GetValueOrDefault(code))
            .Where(lang => lang is not null)
            .Cast<SupportedLanguage>()
            .ToArray();

        SetProgress(100);
    }

    protected override Task OnStopAsync(CancellationToken ct)
    {
        _localization      = FrozenDictionary<string, IReadOnlyDictionary<string, string>>.Empty;
        _itemSearchIndex   = FrozenDictionary<string, SearchEntry[]>.Empty;
        _marketSearchIndex = FrozenDictionary<string, SearchEntry[]>.Empty;
        _availableLanguages = Array.Empty<SupportedLanguage>();
        return Task.CompletedTask;
    }

    // ── Parser TMX ────────────────────────────────────────────────────────────

    private static (
        FrozenDictionary<string, IReadOnlyDictionary<string, string>> Localization,
        IReadOnlyList<string>                                          Languages)
    ParseTmx(XDocument doc, CancellationToken ct)
    {
        var localization = new Dictionary<string, IReadOnlyDictionary<string, string>>(
            capacity: 200_000,
            comparer: StringComparer.OrdinalIgnoreCase);

        var langSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tu in doc.Descendants("tu"))
        {
            ct.ThrowIfCancellationRequested();

            var tuid = (string?)tu.Attribute("tuid");
            if (string.IsNullOrWhiteSpace(tuid)) continue;

            var langMap = BuildLangMap(tu);
            if (langMap.Count == 0) continue;

            localization[tuid] = langMap;

            foreach (var lang in langMap.Keys)
                langSet.Add(lang);
        }

        return (
            localization.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
            langSet.OrderBy(x => x).ToArray()
        );
    }

    private static Dictionary<string, string> BuildLangMap(XElement tu)
    {
        var map = new Dictionary<string, string>(
            capacity: 16,
            comparer: StringComparer.OrdinalIgnoreCase);

        foreach (var tuv in tu.Elements("tuv"))
        {
            var lang = (string?)tuv.Attribute(XNamespace.Xml + "lang");
            var text = tuv.Element("seg")?.Value;

            if (string.IsNullOrWhiteSpace(lang)) continue;
            if (string.IsNullOrWhiteSpace(text)) continue;

            map[lang] = text;
        }

        return map;
    }

    // ── Construcción de índices de búsqueda ───────────────────────────────────

    /// <summary>
    /// Construye los índices para los dominios Items y Market en un solo pass
    /// sobre el storage principal. Retorna ambos índices congelados.
    ///
    /// Complejidad: O(n × m) con n = entradas TMX, m = idiomas por entrada.
    /// Memoria: ≈ 2 × 30k × 8 idiomas ≈ 480k SearchEntry (referencias ligeras).
    /// </summary>
    private static (
        FrozenDictionary<string, SearchEntry[]> ItemIndex,
        FrozenDictionary<string, SearchEntry[]> MarketIndex)
    BuildSearchIndices(
        FrozenDictionary<string, IReadOnlyDictionary<string, string>> localization,
        CancellationToken ct)
    {
        // byLang[dominio][lang] = lista de entradas acumuladas
        var itemsByLang   = new Dictionary<string, List<SearchEntry>>(StringComparer.OrdinalIgnoreCase);
        var marketsByLang = new Dictionary<string, List<SearchEntry>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, langMap) in localization)
        {
            ct.ThrowIfCancellationRequested();

            bool isItem   = key.StartsWith(ItemsKeyPrefix,  StringComparison.OrdinalIgnoreCase);
            bool isMarket = !isItem &&
                            key.StartsWith(MarketKeyPrefix, StringComparison.OrdinalIgnoreCase);

            if (!isItem && !isMarket) continue;

            var targetMap = isItem ? itemsByLang : marketsByLang;
            var prefix    = isItem ? ItemsKeyPrefix : MarketKeyPrefix;

            // ItemId = clave sin el prefijo de dominio
            // Ejemplo: "@ITEMS_T8_PLANKS_LEVEL2" → "T8_PLANKS_LEVEL2"
            var itemId = key.Length > prefix.Length ? key[prefix.Length..] : key;

            // Metadata extraída de la clave UNA SOLA VEZ para todos los idiomas
            var metadata = KeyMetadataParser.ParseFromKey(key.AsSpan());

            // Crear una SearchEntry por idioma (texto normalizado es distinto por idioma)
            foreach (var (lang, rawText) in langMap)
            {
                if (string.IsNullOrWhiteSpace(rawText)) continue;

                if (!targetMap.TryGetValue(lang, out var list))
                {
                    // Capacidad inicial estimada: 25k ítems por idioma
                    list = new List<SearchEntry>(25_000);
                    targetMap[lang] = list;
                }

                list.Add(new SearchEntry(
                    ItemId:         itemId,
                    NormalizedText: TextNormalizer.Normalize(rawText),
                    Metadata:       metadata));
            }
        }

        return (
            FreezeIndex(itemsByLang),
            FreezeIndex(marketsByLang)
        );
    }

    /// <summary>
    /// Convierte el diccionario de acumulación en un FrozenDictionary inmutable.
    /// Las listas se convierten a arrays para acceso más eficiente en el loop de búsqueda.
    /// </summary>
    private static FrozenDictionary<string, SearchEntry[]> FreezeIndex(
        Dictionary<string, List<SearchEntry>> byLang)
    {
        var frozen = new Dictionary<string, SearchEntry[]>(
            byLang.Count,
            StringComparer.OrdinalIgnoreCase);

        foreach (var (lang, list) in byLang)
            frozen[lang] = list.ToArray();

        return frozen.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private FrozenDictionary<string, SearchEntry[]> SelectIndex(SearchDomain domain)
        => domain == SearchDomain.Items ? _itemSearchIndex : _marketSearchIndex;
}
