using AlbionApp.Domain.Search;
using AlbionApp.Domain.Text;

namespace AlbionApp.Application.Search;

/// <summary>
/// Parsea un texto raw de búsqueda en un <see cref="SearchQuery"/> estructurado.
///
/// <para>Gramática soportada (case-insensitive, compatible con la sintaxis del juego):</para>
/// <code>
///   "t4"      → Tier=4
///   "t4.3"    → Tier=4, Enchantment=3
///   "T6.1"    → Tier=6, Enchantment=1
///   ".3"      → Enchantment=3
///   "8"       → Tier=8  (alias numérico — dígito 1-8 aislado)
///   "8.2"     → Tier=8, Enchantment=2
///   "espada"  → TextTokens=["espada"]
///   "tabla 8.2" → TextTokens=["tabla"], Tier=8, Enchantment=2
/// </code>
///
/// <para>Reglas de precedencia:</para>
/// <list type="bullet">
///   <item>El primer fragmento estructural de cada tipo (tier/enchantment) prevalece.</item>
///   <item>Los fragmentos estructurales posteriores se ignoran — no se acumulan.</item>
///   <item>Los tokens de texto se normalizan y filtran stop words.</item>
/// </list>
///
/// Sin estado, sin dependencias de instancia. Totalmente testeable.
/// </summary>
public static class QueryParser
{
    /// <summary>
    /// Parsea <paramref name="rawQuery"/> en un <see cref="SearchQuery"/>.
    /// Nunca lanza — retorna <see cref="SearchQuery.Empty"/> para entrada nula o vacía.
    /// </summary>
    public static SearchQuery Parse(this string? rawQuery)
    {
        if (string.IsNullOrWhiteSpace(rawQuery))
            return SearchQuery.Empty;

        var parts = rawQuery.Trim()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        int?    tier       = null;
        int?    enchant    = null;
        var     textTokens = new List<string>(parts.Length);

        foreach (var part in parts)
        {
            var span = part.AsSpan();

            // Intentar parsear fragmentos estructurales (fail-fast: más específico primero)
            if (TryParseTierAndEnchant(span, out int t, out int e))
            {
                tier    ??= t;
                enchant ??= e;
                continue;
            }

            if (TryParseTierOnly(span, out t))
            {
                tier ??= t;
                continue;
            }

            if (TryParseEnchantOnly(span, out e))
            {
                enchant ??= e;
                continue;
            }

            // Token de texto: normalizar y excluir stop words
            var normalized = TextNormalizer.Normalize(part);
            if (normalized.Length > 0 && !TextNormalizer.IsStopWord(normalized))
                textTokens.Add(normalized);
        }

        return new SearchQuery(
            textTokens: textTokens.Count > 0 ? textTokens.ToArray() : [],
            tier:        tier,
            enchantment: enchant);
    }

    // ── Parsers de fragmentos estructurales ───────────────────────────────────

    /// <summary>
    /// Parsea "t4.2", "T4.2" o "4.2" → Tier=4, Enchantment=2.
    /// </summary>
    private static bool TryParseTierAndEnchant(ReadOnlySpan<char> span, out int tier, out int enchant)
    {
        tier = enchant = 0;

        // "t4.2" | "T4.2" — longitud 4
        if (span.Length == 4 &&
            span[0] is 't' or 'T' &&
            char.IsDigit(span[1]) &&
            span[2] == '.' &&
            char.IsDigit(span[3]))
        {
            tier    = span[1] - '0';
            enchant = span[3] - '0';
            return true;
        }

        // "4.2" — longitud 3 (alias sin prefijo t)
        if (span.Length == 3 &&
            char.IsDigit(span[0]) &&
            span[1] == '.' &&
            char.IsDigit(span[2]))
        {
            tier    = span[0] - '0';
            enchant = span[2] - '0';
            return true;
        }

        return false;
    }

    /// <summary>
    /// Parsea "t4", "T4" o "4" → Tier=4.
    /// El dígito aislado "0" se ignora (no es un tier válido).
    /// </summary>
    private static bool TryParseTierOnly(ReadOnlySpan<char> span, out int tier)
    {
        tier = 0;

        // "t4" | "T4"
        if (span.Length == 2 &&
            span[0] is 't' or 'T' &&
            char.IsDigit(span[1]))
        {
            tier = span[1] - '0';
            return true;
        }

        // "4" — dígito aislado, excluye '0' (tier 0 no existe en el juego)
        if (span.Length == 1 &&
            char.IsDigit(span[0]) &&
            span[0] != '0')
        {
            tier = span[0] - '0';
            return true;
        }

        return false;
    }

    /// <summary>
    /// Parsea ".2" → Enchantment=2.
    /// </summary>
    private static bool TryParseEnchantOnly(ReadOnlySpan<char> span, out int enchant)
    {
        enchant = 0;

        if (span.Length == 2 &&
            span[0] == '.' &&
            char.IsDigit(span[1]))
        {
            enchant = span[1] - '0';
            return true;
        }

        return false;
    }
}
