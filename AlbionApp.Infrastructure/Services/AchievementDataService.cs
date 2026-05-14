using System.Collections.Frozen;
using System.Globalization;
using System.Xml.Linq;
using AlbionApp.Domain.Achievement;
using AlbionApp.Domain.Interfaces;
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

    // ── IAchievementDataService ───────────────────────────────────────────────

    public IReadOnlyDictionary<string, AlbionTemplate> Templates => _templates;
    public IReadOnlyDictionary<string, AlbionAchievement> ById => _byId;
    public IReadOnlyDictionary<int, AlbionAchievement> ByOrdinal => _byOrdinal;

    // ── Constructor ───────────────────────────────────────────────────────────

    public AchievementDataService(AlbionData albionData)
        => _albionData = albionData;

    // ── Ciclo de vida (ServiceBase) ───────────────────────────────────────────

    protected override Task OnStartAsync(CancellationToken ct)
    {
        SetProgress(0);
        var doc = _albionData.GetXDocument(GameDataPath.Achievements);
        SetProgress(20);

        var (templates, byId, byOrdinal) = Parse(doc);
        SetProgress(90);

        _templates = templates;
        _byId = byId;
        _byOrdinal = byOrdinal;

        SetProgress(100);
        return Task.CompletedTask;
    }

    protected override Task OnStopAsync(CancellationToken ct)
    {
        _templates = FrozenDictionary<string, AlbionTemplate>.Empty;
        _byId = FrozenDictionary<string, AlbionAchievement>.Empty;
        _byOrdinal = FrozenDictionary<int, AlbionAchievement>.Empty;
        return Task.CompletedTask;
    }

    // ── Parser ────────────────────────────────────────────────────────────────

    private static (
        FrozenDictionary<string, AlbionTemplate> templates,
        FrozenDictionary<string, AlbionAchievement> byId,
        FrozenDictionary<int, AlbionAchievement> byOrdinal
        ) Parse(XDocument doc)
    {
        var root = doc.Root ?? throw new InvalidDataException("achievements.bin: root vacío.");

        var templates = new Dictionary<string, AlbionTemplate>();
        var byId = new Dictionary<string, AlbionAchievement>();
        var byOrdinal = new Dictionary<int, AlbionAchievement>();

        int ordinal = 0; // incrementa solo con <templateachievement>

        foreach (var el in root.Elements())
        {
            switch (el.Name.LocalName)
            {
                case "template":
                    var tmpl = ParseTemplate(el);
                    if (tmpl is not null)
                        templates[tmpl.Name] = tmpl;
                    break;

                case "achievement":
                    // Nodo raíz sin ordinal (ADVENTURER_*, COMBAT_CLOTH, etc.)
                    var ach = ParseAchievement(el, ordinalId: -1);
                    if (ach is not null)
                        byId[ach.Id] = ach;
                    ordinal++;
                    break;

                case "templateachievement":
                    // Spec del jugador — recibe ordinal y se indexa también por número
                    var tach = ParseAchievement(el, ordinalId: ordinal);
                    if (tach is not null)
                    {
                        byId[tach.Id] = tach;
                        byOrdinal[tach.OrdinalId] = tach;
                    }

                    ordinal++;
                    break;
            }
        }

        return (
            templates.ToFrozenDictionary(),
            byId.ToFrozenDictionary(),
            byOrdinal.ToFrozenDictionary()
        );
    }

    // ── Parsers de elementos ──────────────────────────────────────────────────

    private static AlbionTemplate? ParseTemplate(XElement el)
    {
        var name = (string?)el.Attribute("name");
        if (string.IsNullOrEmpty(name)) return null;

        var baseLevels = el.Element("baselevels");
        if (baseLevels is null)
            return new AlbionTemplate { Name = name };

        var levels = ParseLevels(baseLevels);
        return new AlbionTemplate { Name = name, Levels = levels };
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

    private static AlbionAchievement? ParseAchievement(XElement el, int ordinalId)
    {
        var id = (string?)el.Attribute("id");
        if (string.IsNullOrEmpty(id)) return null;

        var useTemplate = (string?)el.Attribute("usetemplate");
        var nameLocalization = (string?)el.Element("title")?.Attribute("tag") ?? id;

        var parents = el.Element("parentachievements")
                          ?.Elements("achievement")
                          .Select(p => (string?)p.Attribute("id"))
                          .Where(pid => !string.IsNullOrEmpty(pid))
                          .Select(pid => pid!)
                          .ToArray()
                      ?? [];

        return new AlbionAchievement
        {
            OrdinalId = ordinalId,
            Id = id,
            UseTemplate = useTemplate,
            Parents = parents,
            NameLocalization = nameLocalization,
        };
    }

    // ── Guard ─────────────────────────────────────────────────────────────────

    private void EnsureOn()
    {
        if (State is not ServiceState.On)
            throw new InvalidOperationException(
                $"[{ServiceName}] no está activo (estado: {State}). Llama StartAsync primero.");
    }
}