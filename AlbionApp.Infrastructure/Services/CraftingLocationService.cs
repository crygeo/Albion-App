using AlbionApp.Domain.Crafting;
using AlbionApp.Domain.Interfaces;
using AlbionApp.Domain.Interfaces.Services;
using LibAlbionData;
using LibAlbionData.Clusters;
using LibAlbionData.Core;
using LibServiceLifecycle;

namespace AlbionApp.Infrastructure.Services;

/// <summary>
/// Carga y expone las ciudades de crafteo con sus bonificaciones.
///
/// Fuentes de datos (via AlbionData):
///   • world.bin             → AlbionClusterLoader          → clusters del mundo
///   • craftingmodifiers.bin → AlbionCraftingModifiersLoader → modificadores de ciudad
///
/// Solo expone los clusters que tienen entrada directa por clusterid en
/// craftingmodifiers (las ciudades principales: Lymhurst, Bridgewatch, etc.).
///
/// Nombres: en world.bin el displayname de las ciudades principales viene vacío;
/// los nombres se resuelven vía ILocalizationService usando un mapa de claves conocidas.
/// </summary>
public sealed class CraftingLocationService : ServiceBase, ICraftingLocationService
{
    private readonly AlbionData          _albionData;
    private readonly ILocalizationService _localization;

    public override string ServiceName => "CraftingLocationService";

    /// <summary>
    /// Mapa de clusterid → clave de localización para las ciudades conocidas.
    /// Verificado contra albionjournal.xml (gotocluster clusterdirect + nameloca).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> ClusterLocaKeys =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["0000"] = "@JOURNAL_CITIES_THETFORD",
            ["0008"] = "@JOURNAL_CITIES_MORGANAS_REST",
            ["1000"] = "@JOURNAL_CITIES_LYMHURST",
            ["1012"] = "@JOURNAL_CITIES_MERLYNS_REST",
            ["2000"] = "@JOURNAL_CITIES_BRIDGEWATCH",
            ["3003"] = "@JOURNAL_CITIES_CAERLEON",
            ["3004"] = "@JOURNAL_CITIES_MARTLOCK",
            ["4000"] = "@JOURNAL_CITIES_FORT_STERLING",
            ["4300"] = "@JOURNAL_CITIES_ARTHURS_REST",
            ["5000"] = "@JOURNAL_CITIES_BRECILIEN",
        };

    private volatile IReadOnlyList<CraftingCityData> _cities = [];

    public IReadOnlyList<CraftingCityData> Cities => _cities;

    public CraftingLocationService(AlbionData albionData, ILocalizationService localization)
    {
        _albionData   = albionData;
        _localization = localization;
    }

    /// <summary>
    /// Resuelve el nombre de una ciudad con triple fallback:
    ///   1. Clave de localización conocida (mapa estático → localización).
    ///   2. DisplayName directo del XML si no está vacío.
    ///   3. ClusterId como último recurso legible.
    /// </summary>
    private string ResolveName(AlbionCluster cluster)
    {
        if (ClusterLocaKeys.TryGetValue(cluster.ClusterId, out var locaKey))
        {
            var resolved = _localization.GetText(locaKey);
            // GetText retorna la clave si no hay traducción — filtramos ese caso
            if (!string.IsNullOrWhiteSpace(resolved) && resolved != locaKey)
                return resolved;
        }

        if (!string.IsNullOrWhiteSpace(cluster.DisplayName))
            return cluster.DisplayName;

        return cluster.ClusterId;
    }

    protected override Task OnStartAsync(CancellationToken ct)
    {
        SetProgress(0);

        var worldDoc      = _albionData.GetXDocument(GameDataPath.World);
        var modifiersDoc  = _albionData.GetXDocument(GameDataPath.CraftingModifiers);

        SetProgress(30);

        var clusters  = new AlbionClusterLoader().LoadFromDocument(worldDoc);
        var modifiers = new AlbionCraftingModifiersLoader().LoadFromDocument(modifiersDoc);

        SetProgress(70);

        // Indexar clusters por id para join eficiente.
        // Usamos asignación por índice en lugar de ToDictionary para tolerar
        // clusterids duplicados en world.xml (ej: "0000" aparece varias veces).
        var clusterById = new Dictionary<string, AlbionCluster>(StringComparer.OrdinalIgnoreCase);
        foreach (var cluster in clusters)
            clusterById[cluster.ClusterId] = cluster;

        // Iterar sobre TODOS los clusters y usar GetModifiers para resolver el bonus.
        // Esto incluye:
        //   • Ciudades principales (ByClusterId) — Lymhurst, Caerleon, etc.
        //   • Zonas de Outlands (ByGroup via continent+biome+quality)
        // Clusters sin bonus (RED, YELLOW, ROADS sin entrada, islas…) quedan excluidos.
        var cities = new List<CraftingCityData>();

        foreach (var cluster in clusters)
        {
            var location = modifiers.GetModifiers(cluster);
            if (location is null) continue;

            // Excluir clusters de debug o sin nombre útil
            if (string.IsNullOrWhiteSpace(cluster.DisplayName)) continue;

            cities.Add(new CraftingCityData(
                ClusterId:     cluster.ClusterId,
                Name:          ResolveName(cluster),
                ClusterType:   cluster.Type,
                RefiningBonus: location.RefiningBonus,
                CraftingBonus: location.CraftingBonus,
                Modifiers:     location.Modifiers
                    .Select(m => new CraftingModifier(m.Name, m.Value))
                    .ToList()));
        }

        // Ordenar: ciudades conocidas primero (tienen clave de loca), luego el resto alfabético
        _cities = [.. cities.OrderBy(c =>
            ClusterLocaKeys.ContainsKey(c.ClusterId) ? 0 : 1)
            .ThenBy(c => c.Name)];

        SetProgress(100);
        return Task.CompletedTask;
    }

    protected override Task OnStopAsync(CancellationToken ct)
    {
        _cities = [];
        return Task.CompletedTask;
    }
}
