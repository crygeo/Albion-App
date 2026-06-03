using AlbionApp.Domain.Interfaces.Services;
using AlbionApp.Domain.ItemSearch;

namespace AlbionApp.Domain.Crafting;

/// <summary>
/// Calcula la fama obtenida al refinar o craftear un ítem.
/// </summary>
public static class FameCalculator
{
    // ── Tablas ────────────────────────────────────────────────────────────────

    private static readonly IReadOnlyDictionary<int, double> FameByTier =
        new Dictionary<int, double>
        {
            { 2, 1.5   },
            { 3, 7.5   },
            { 4, 22.5  },
            { 5, 90.0  },
            { 6, 270.0 },
            { 7, 645.0 },
            { 8, 1395.0},
        };

    private static readonly IReadOnlyDictionary<ArtifactType, double> ArtifactBonus =
        new Dictionary<ArtifactType, double>
        {
            { ArtifactType.None,         0.0 },
            { ArtifactType.Rune,         0.1 },
            { ArtifactType.Soul,         0.2 },
            { ArtifactType.Relic,        0.3 },
            { ArtifactType.Mist,         0.3 },
            { ArtifactType.Avalonian,    0.4 },
            { ArtifactType.Crystal,      0.4 },
            { ArtifactType.Shapeshifter, 0.0 },
        };

    // ── Libros ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Libros necesarios para absorber la fama total generada.
    /// Usa siempre la fama base (sin premium) aunque el jugador tenga premium.
    /// </summary>
    public static int CalculateBooks(double famePerItem, int quantity, double maxFame)
        => (int)Math.Ceiling(famePerItem * quantity / maxFame);

    // ── Refinado ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Fama al refinar una unidad del recurso indicado.
    /// Aplica a ítems con shopsubcategory1="refinedresources".
    /// </summary>
    public static double CalculateRefiningFame(int tier, int enchantmentLevel, bool premium)
    {
        if (!FameByTier.TryGetValue(tier, out var baseFame))
            return 0;

        var fame = baseFame * Math.Pow(2, enchantmentLevel);

        if (premium)
            fame *= 1.5;

        return fame;
    }

    // ── Crafteo ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Fama al craftear un ítem a partir de sus ingredientes.
    /// Los ingredientes de tipo artefacto (uniquename contiene "ARTEFACT") se excluyen
    /// del cálculo de fama base — su contribución va por <paramref name="artifactType"/>.
    /// </summary>
    public static double CalculateCraftFame(
        IEnumerable<IIngredient> ingredients,
        ArtifactType             artifactType,
        bool                     premium)
    {
        var baseFame = 0.0;

        foreach (var ing in ingredients)
        {
            if (IsArtifact(ing.ItemId)) continue;

            var tier        = ParseTier(ing.ItemId);
            var enchantment = ParseEnchantment(ing.ItemId);

            if (tier is null || !FameByTier.TryGetValue(tier.Value, out var tierFame))
                continue;

            baseFame += tierFame * Math.Pow(2, enchantment) * ing.Count;
        }

        baseFame *= 1 + ArtifactBonus[artifactType];

        if (premium)
            baseFame *= 1.5;

        return baseFame;
    }

    // ── Resolución de artefacto ───────────────────────────────────────────────

    /// <summary>
    /// Determina el tipo de artefacto de un ítem a partir de sus ingredientes.
    ///
    /// Flujo (dos niveles):
    ///   1. Busca un ingrediente con "ARTEFACT" en su ItemId.
    ///   2. Obtiene la receta de ese artefacto y busca el fragmento que usa
    ///      (RUNE, SOUL, RELIC, SHARD_AVALONIAN, SHARD_CRYSTAL, SHARD_SHAPESHIFTER).
    ///   3. Mapea al enum ArtifactType.
    /// </summary>
    public static ArtifactType ResolveArtifactType(
        IEnumerable<IIngredient> ingredients,
        IItemDataService         itemDataService)
    {
        foreach (var ing in ingredients)
        {
            if (!IsArtifact(ing.ItemId)) continue;

            // Buscar la receta del artefacto para ver qué fragmento usa
            var artifactRecipes = itemDataService.GetRecipes(ing.ItemId);
            if (artifactRecipes.Length == 0) continue;

            foreach (var recipe in artifactRecipes)
            {
                foreach (var artifactIng in recipe.Ingredients)
                {
                    var type = FragmentToArtifactType(artifactIng.ItemId);
                    if (type != ArtifactType.None)
                        return type;
                }
            }
        }

        return ArtifactType.None;
    }

    // ── Helpers privados ──────────────────────────────────────────────────────

    private static bool IsArtifact(string itemId)
        => itemId.Contains("ARTEFACT", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Parsea el tier del ItemId. Formato esperado: T{N}_...
    /// Ej. "T4_METALBAR_LEVEL1" → 4
    /// </summary>
    private static int? ParseTier(string itemId)
    {
        if (itemId.Length < 2 || itemId[0] is not ('T' or 't'))
            return null;

        var underscore = itemId.IndexOf('_');
        if (underscore < 2) return null;

        return int.TryParse(itemId.AsSpan(1, underscore - 1), out var tier) ? tier : null;
    }

    /// <summary>
    /// Parsea el nivel de encantamiento del ItemId.
    /// Soporta sufijo _LEVEL{N} (ej. T4_METALBAR_LEVEL1 → 1) y sin sufijo → 0.
    /// </summary>
    private static int ParseEnchantment(string itemId)
    {
        const string prefix = "_LEVEL";
        var idx = itemId.LastIndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return 0;

        var levelStr = itemId.AsSpan(idx + prefix.Length);
        return int.TryParse(levelStr, out var level) ? level : 0;
    }

    /// <summary>
    /// Mapea el ItemId de un fragmento de artefacto al ArtifactType correspondiente.
    /// </summary>
    private static ArtifactType FragmentToArtifactType(string itemId) =>
        itemId switch
        {
            _ when itemId.Contains("SHARD_SHAPESHIFTER", StringComparison.OrdinalIgnoreCase) => ArtifactType.Shapeshifter,
            _ when itemId.Contains("SHARD_AVALONIAN",    StringComparison.OrdinalIgnoreCase) => ArtifactType.Avalonian,
            _ when itemId.Contains("SHARD_CRYSTAL",      StringComparison.OrdinalIgnoreCase) => ArtifactType.Crystal,
            _ when itemId.Contains("_RELIC",             StringComparison.OrdinalIgnoreCase) => ArtifactType.Relic,
            _ when itemId.Contains("_SOUL",              StringComparison.OrdinalIgnoreCase) => ArtifactType.Soul,
            _ when itemId.Contains("_RUNE",              StringComparison.OrdinalIgnoreCase) => ArtifactType.Rune,
            _ when itemId.Contains("_MIST",              StringComparison.OrdinalIgnoreCase) => ArtifactType.Mist,
            _                                                                                 => ArtifactType.None,
        };
}
