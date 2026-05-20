using System.Collections.Frozen;
using System.Globalization;
using System.Xml.Linq;
using AlbionApp.Domain.Achievement;
using AlbionApp.Domain.Interfaces;
using AlbionApp.Domain.ItemSearch;
using AlbionApp.Domain.Interfaces.Services;
using LibAlbionData;
using LibAlbionData.Core;
using LibServiceLifecycle;

namespace AlbionApp.Infrastructure.Services;

/// <summary>
/// Servicio que parsea y expone el árbol completo de achievements de Albion Online.
///
/// Lee <c>achievements.bin</c> desde <see cref="AlbionData"/> (ya desencriptado y
/// en caché) y construye tres diccionarios inmutables en <c>OnStartAsync</c>.
///
/// Estructura del XML:
/// <list type="bullet">
///   <item><c>&lt;template name="REFINE_T4"&gt;</c> — curva Fama/LP (100 niveles).</item>
///   <item><c>&lt;achievement id="ADVENTURER_MASTER"&gt;</c> — nodo raíz, sin ordinal.</item>
///   <item><c>&lt;templateachievement id="CRAFT_REFINE_FIBER_T4"&gt;</c> — spec del jugador,
///         con OrdinalId = posición en el XML ignorando templates.</item>
/// </list>
/// </summary>
public sealed class AchievementDataService : ServiceBase, IAchievementDataService
{
    private readonly AlbionData _albionData;

    public override string ServiceName => "AchievementDataService";

    // ── Índices (FrozenDictionary — escritura única, lecturas sin lock) ────────

    private volatile FrozenDictionary<string, AlbionTemplate> _templates
        = FrozenDictionary<string, AlbionTemplate>.Empty;

    private volatile FrozenDictionary<string, AlbionAchievement> _byId
        = FrozenDictionary<string, AlbionAchievement>.Empty;

    private volatile FrozenDictionary<int, AlbionAchievement> _byOrdinal
        = FrozenDictionary<int, AlbionAchievement>.Empty;

    /// <summary>
    /// Índice compuesto (spriteReward, tier) → achievement exacto.
    /// Clave: <c>"{spriteReward.ToLower()}_{tier}"</c> — ej: <c>"planks_8"</c>.
    /// Permite localizar el achievement correcto desde cualquier tier de un item.
    /// </summary>
    private volatile FrozenDictionary<string, AlbionAchievement> _bySpriteAndTier
        = FrozenDictionary<string, AlbionAchievement>.Empty;

    private volatile IReadOnlyList<(string AchievementId, AchievementBonus Bonus)> _bonusLookup
        = [];

    // ── IAchievementDataService ───────────────────────────────────────────────

    public IReadOnlyDictionary<string, AlbionTemplate>   Templates        => _templates;
    public IReadOnlyDictionary<string, AlbionAchievement> ById            => _byId;
    public IReadOnlyDictionary<int,    AlbionAchievement> ByOrdinal       => _byOrdinal;
    public IReadOnlyDictionary<string, AlbionAchievement> BySpriteAndTier => _bySpriteAndTier;
    public IReadOnlyList<(string AchievementId, AchievementBonus Bonus)>  BonusLookup => _bonusLookup;

    // ── Constructor ───────────────────────────────────────────────────────────

    public AchievementDataService(AlbionData albionData)
        => _albionData = albionData;

    // ── Ciclo de vida (ServiceBase) ───────────────────────────────────────────

    protected override Task OnStartAsync(CancellationToken ct)
    {
        SetProgress(0);
        var doc = _albionData.GetXDocument(GameDataPath.Achievements);
        SetProgress(20);

        var (templates, byId, byOrdinal, bySpriteAndTier, bonusLookup) = Parse(doc);
        SetProgress(90);

        _templates        = templates;
        _byId             = byId;
        _byOrdinal        = byOrdinal;
        _bySpriteAndTier  = bySpriteAndTier;
        _bonusLookup      = bonusLookup;

        SetProgress(100);
        return Task.CompletedTask;
    }

    protected override Task OnStopAsync(CancellationToken ct)
    {
        _templates        = FrozenDictionary<string, AlbionTemplate>.Empty;
        _byId             = FrozenDictionary<string, AlbionAchievement>.Empty;
        _byOrdinal        = FrozenDictionary<int,    AlbionAchievement>.Empty;
        _bySpriteAndTier  = FrozenDictionary<string, AlbionAchievement>.Empty;
        _bonusLookup      = [];
        return Task.CompletedTask;
    }

    // ── Parser ────────────────────────────────────────────────────────────────

    private static (
        FrozenDictionary<string, AlbionTemplate>    templates,
        FrozenDictionary<string, AlbionAchievement> byId,
        FrozenDictionary<int,    AlbionAchievement> byOrdinal,
        FrozenDictionary<string, AlbionAchievement> bySpriteAndTier,
        IReadOnlyList<(string AchievementId, AchievementBonus Bonus)> bonusLookup
        ) Parse(XDocument doc)
    {
        var root = doc.Root ?? throw new InvalidDataException("achievements.bin: root vacío.");

        var templates = new Dictionary<string, AlbionTemplate>(StringComparer.OrdinalIgnoreCase);
        var byId      = new Dictionary<string, AlbionAchievement>(StringComparer.OrdinalIgnoreCase);
        var byOrdinal = new Dictionary<int, AlbionAchievement>();

        // Pasada 1: templates — necesitamos su MaxLevel antes de parsear achievements.
        foreach (var el in root.Elements())
        {
            if (el.Name.LocalName != "template") continue;
            var tmpl = ParseTemplate(el);
            if (tmpl is not null)
                templates[tmpl.Name] = tmpl;
        }

        // Pasada 2: achievements con MaxLevel resuelto desde el template.
        int ordinal = 0;
        foreach (var el in root.Elements())
        {
            switch (el.Name.LocalName)
            {
                case "achievement":
                    var ach = ParseAchievement(el, ordinalId: -1, templates);
                    if (ach is not null)
                        byId[ach.Id] = ach;
                    ordinal++;
                    break;

                case "templateachievement":
                    var tach = ParseAchievement(el, ordinalId: ordinal, templates);
                    if (tach is not null)
                    {
                        byId[tach.Id]             = tach;
                        byOrdinal[tach.OrdinalId] = tach;
                    }
                    ordinal++;
                    break;
            }
        }

        // Índice compuesto (spriteReward, tier) para crafting/refinación.
        // Clave: "{spriteReward.ToLower()}_{ring-1}"  →  ej: "planks_7"
        // ring - 1 = tier del item (ring=8 → tier=7).
        // GroupBy en lugar de ToDictionary para evitar duplicados.
        // Colisiones ocurren en armas de combate (ej: arcane staff normal vs great
        // comparten spriteReward + ring). Para esos casos usamos combatspecachievement
        // directo, así que aquí basta con quedarnos con cualquiera de los duplicados.
        var bySpriteAndTier = byId.Values
            .Where(a => a.SpriteReward is not null && a.Ring > 0)
            .GroupBy(a => $"{a.SpriteReward!.ToLower()}_{a.Ring - 1}",
                     StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(),
                          StringComparer.OrdinalIgnoreCase);

        // Lista plana de todos los (achievementId, bonus) con patrones,
        // usada para el scan global en GetBonusesForItem.
        var bonusLookup = byId.Values
            .SelectMany(a => a.Bonuses
                .Where(b => b.ItemPatterns.Count > 0)
                .Select(b => (a.Id, b)))
            .ToList();

        return (
            templates.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
            byId.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
            byOrdinal.ToFrozenDictionary(),
            bySpriteAndTier.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
            (IReadOnlyList<(string, AchievementBonus)>)bonusLookup
        );
    }

    // ── Parsers de elementos ──────────────────────────────────────────────────

    private static AlbionTemplate? ParseTemplate(XElement el)
    {
        var name = (string?)el.Attribute("name");
        if (string.IsNullOrEmpty(name)) return null;

        var levels      = el.Element("baselevels")  is { } bl ? ParseLevels(bl) : [];
        var eliteLevels = el.Element("elitelevels") is { } el2 ? ParseLevels(el2) : [];

        return new AlbionTemplate { Name = name, Levels = levels, EliteLevels = eliteLevels };
    }

    private static IReadOnlyList<AlbionAchievementLevel> ParseLevels(XElement baseLevels)
    {
        // Cada línea: "3713;15;2;8;2;8;4"  (Fame;LP;...)
        // El contenido puede ser el value del elemento o una lista de <level> hijos.
        var lines = baseLevels.Value
            .Split(['\n', '\r', '\t', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Where(l => l.Contains(';'))
            .ToArray();

        var result = new List<AlbionAchievementLevel>(lines.Length);
        foreach (var line in lines)
        {
            var parts = line.Split(';');
            if (parts.Length < 2) continue;

            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var fame))
                continue;
            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var lp))
                continue;

            result.Add(new AlbionAchievementLevel { Fame = fame, LP = lp });
        }

        return result;
    }

    private static AlbionAchievement? ParseAchievement(
        XElement                              el,
        int                                   ordinalId,
        Dictionary<string, AlbionTemplate>    templates)
    {
        var id = (string?)el.Attribute("id");
        if (string.IsNullOrEmpty(id)) return null;

        var useTemplate      = (string?)el.Attribute("usetemplate");
        var nameLocalization = (string?)el.Element("title")?.Attribute("tag") ?? id;
        var itemForSprite    = (string?)el.Attribute("itemforsprite");
        var spriteReward     = (string?)el.Attribute("spriteReward");
        int.TryParse((string?)el.Attribute("ring"), out var ring);

        var parents = el.Element("parentachievements")
                          ?.Elements("achievement")
                          .Select(p => (string?)p.Attribute("id"))
                          .Where(pid => !string.IsNullOrEmpty(pid))
                          .Select(pid => pid!)
                          .ToArray()
                      ?? [];

        var maxLevel = useTemplate is not null
                    && templates.TryGetValue(useTemplate, out var tmpl)
            ? tmpl.MaxLevel
            : 100;

        var bonuses = ParseBonuses(el.Element("baserewards"));

        return new AlbionAchievement
        {
            OrdinalId        = ordinalId,
            Id               = id,
            UseTemplate      = useTemplate,
            Parents          = parents,
            NameLocalization = nameLocalization,
            MaxLevel         = maxLevel,
            Ring             = ring,
            ItemForSprite    = itemForSprite,
            SpriteReward     = spriteReward,
            Bonuses          = bonuses,
        };
    }

    private static IReadOnlyList<AchievementBonus> ParseBonuses(XElement? baserewards)
    {
        if (baserewards is null) return [];

        var result = new List<AchievementBonus>();

        foreach (var el in baserewards.Elements("bonus"))
        {
            var type = (string?)el.Attribute("type");
            if (string.IsNullOrEmpty(type)) continue;

            if (!double.TryParse(
                    (string?)el.Attribute("bonus"),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var bonusValue))
                continue;

            int.TryParse((string?)el.Attribute("mintier"), out var minTier);
            int.TryParse((string?)el.Attribute("maxtier"), out var maxTier);

            var descTag = (string?)el.Element("description")?.Attribute("tag");

            var patterns = el.Elements("itempattern")
                .Select(p => (string?)p.Attribute("pattern"))
                .Where(p => !string.IsNullOrEmpty(p))
                .Select(p => p!)
                .ToList();

            result.Add(new AchievementBonus
            {
                Type           = type,
                BonusValue     = bonusValue,
                MinTier        = minTier,
                MaxTier        = maxTier,
                DescriptionTag = descTag,
                ItemPatterns   = patterns,
            });
        }

        return result;
    }

    // ── Lookup helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Localiza el achievement correspondiente a un item usando dos estrategias:
    /// <list type="bullet">
    ///   <item>
    ///     <b>Combate:</b> el item tiene <c>combatspecachievement</c> como atributo directo
    ///     (ej: T4_OFF_SHIELD_AVALON → COMBAT_SHIELDS_AVALON).
    ///   </item>
    ///   <item>
    ///     <b>Refinación / crafteo:</b> se cruza <c>shopsubcategory2</c> del item con
    ///     <c>spriteReward</c> del achievement y <c>ring - 1</c> con <c>tier</c>
    ///     (ej: T7_PLANKS → "planks_7" → CRAFT_REFINE_WOOD_T7).
    ///   </item>
    /// </list>
    /// Retorna <c>null</c> si no se encuentra coincidencia.
    /// </summary>
    public AlbionAchievement? FindByItem(ItemBase item)
    {
        // Path 1: items de combate — atributo directo en el XML del item.
        var combatId = item.RawAttributes.GetValueOrDefault("combatspecachievement");
        if (!string.IsNullOrEmpty(combatId) && _byId.TryGetValue(combatId, out var combat))
            return combat;

        // Path 2: items de refinación/crafteo — índice compuesto (spriteReward, tier).
        var subcategory = item.RawAttributes.GetValueOrDefault("shopsubcategory2") ?? "";
        var tier        = item.Tier ?? 0;
        var key         = $"{subcategory.ToLower()}_{tier}";
        return _bySpriteAndTier.GetValueOrDefault(key);
    }

    // ── Guard ─────────────────────────────────────────────────────────────────

    private void EnsureOn()
    {
        if (State is not ServiceState.On)
            throw new InvalidOperationException(
                $"[{ServiceName}] no está activo (estado: {State}). Llama StartAsync primero.");
    }
}