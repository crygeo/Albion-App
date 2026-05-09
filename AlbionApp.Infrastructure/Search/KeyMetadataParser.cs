using AlbionApp.Domain.Search;

namespace AlbionApp.Infrastructure.Search;

/// <summary>
/// Parsea metadata estructural (tier, enchantment) desde claves de localización TMX.
///
/// <para>Formatos reconocidos en las claves de ítems de Albion Online:</para>
/// <code>
///   T{N}      → Tier = N        Ejemplo: "T8" → Tier=8
///   LEVEL{N}  → Enchantment = N  Ejemplo: "LEVEL2" → Enchantment=2
/// </code>
///
/// <para>Ejemplos completos:</para>
/// <code>
///   "@ITEMS_T8_PLANKS_LEVEL2"   →  SearchMetadata(Tier=8, Enchantment=2)
///   "@ITEMS_T4_MAIN_SWORD"      →  SearchMetadata(Tier=4, Enchantment=null)
///   "@ITEMS_QUESTITEM_ROYAL"    →  SearchMetadata(Tier=null, Enchantment=null)
///   "@ITEMS_T5_BAG@1"           →  SearchMetadata(Tier=5, Enchantment=null)
/// </code>
///
/// <para>Diseño:</para>
/// <list type="bullet">
///   <item>Span-based — sin heap allocations en el path de parseo.</item>
///   <item>Un solo pass sobre la clave, segmento a segmento (separador '_').</item>
///   <item>Early exit al encontrar ambos valores — sin iteraciones innecesarias.</item>
///   <item>Llamado UNA SOLA VEZ por entrada durante la construcción del índice en startup.</item>
///   <item>Sin regex, sin reflection.</item>
/// </list>
/// </summary>
internal static class KeyMetadataParser
{
    private const int LevelPrefixLength = 5; // "LEVEL".Length

    /// <summary>
    /// Parsea la clave y retorna la metadata estructural.
    /// Retorna <see cref="SearchMetadata"/> con valores null si no hay patrones reconocibles.
    /// </summary>
    public static SearchMetadata ParseFromKey(ReadOnlySpan<char> key)
    {
        int? tier        = null;
        int? enchantment = null;

        int start = 0;

        while (start < key.Length)
        {
            // Encontrar el siguiente separador '_'
            int sepIdx  = key[start..].IndexOf('_');
            var segment = sepIdx < 0
                ? key[start..]
                : key[start..(start + sepIdx)];

            if (tier is null)
                tier = TryParseTierSegment(segment);

            if (enchantment is null)
                enchantment = TryParseEnchantmentSegment(segment);

            // Early exit: ya encontramos ambos valores
            if (tier.HasValue && enchantment.HasValue)
                break;

            if (sepIdx < 0) break;
            start += sepIdx + 1;
        }

        return new SearchMetadata(tier, enchantment);
    }

    // ── Parsers de segmento ───────────────────────────────────────────────────

    /// <summary>
    /// Reconoce el patrón T{N} → N.
    /// Ejemplo: "T8" → 8, "T4" → 4. Solo dígito único (T1..T9).
    /// </summary>
    private static int? TryParseTierSegment(ReadOnlySpan<char> segment)
    {
        if (segment.Length == 2 &&
            segment[0] is 'T' or 't' &&
            char.IsDigit(segment[1]))
        {
            return segment[1] - '0';
        }

        return null;
    }

    /// <summary>
    /// Reconoce el patrón LEVEL{N} → N.
    /// Ejemplo: "LEVEL2" → 2, "LEVEL1" → 1.
    /// Las claves TMX siempre están en mayúsculas, pero se acepta OrdinalIgnoreCase
    /// para robustez ante futuros cambios en el formato del juego.
    /// </summary>
    private static int? TryParseEnchantmentSegment(ReadOnlySpan<char> segment)
    {
        if (segment.Length == LevelPrefixLength + 1 &&
            char.IsDigit(segment[LevelPrefixLength]))
        {
            ReadOnlySpan<char> levelPrefix = "LEVEL";
            if (segment[..LevelPrefixLength].Equals(levelPrefix, StringComparison.OrdinalIgnoreCase))
                return segment[LevelPrefixLength] - '0';
        }

        return null;
    }
}
