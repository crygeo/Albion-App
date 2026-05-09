using System.Collections.Frozen;
using AlbionApp.Domain.Search;

namespace AlbionApp.Application.Search;

/// <summary>
/// Extensiones de búsqueda sobre el índice de localización
/// <c>FrozenDictionary&lt;string lang, SearchEntry[]&gt;</c>.
///
/// <para><b>Por qué texto-puro y sin metadata de clave:</b></para>
/// El índice almacena metadata (tier, encantamiento) extraída de la clave TMX
/// (ej. <c>@ITEMS_T4_MAIN_SWORD</c>). Sin embargo, esa metadata es incompleta:
/// ítems especiales (<c>@ITEMS_QUESTITEM_ROYAL</c>), monturas, recursos
/// agrupados y otros no siguen el patrón <c>T{N}_..._LEVEL{N}</c>.
/// Filtrar por ella en el índice produce falsos negativos — el ítem existe
/// pero no se encuentra porque su clave no tiene el tier codificado.
///
/// La solución correcta: el índice solo filtra por <b>texto</b>.
/// Tier y encantamiento se aplican como post-filtro sobre <see cref="AlbionApp.Domain.ItemSearch.ItemBase"/>,
/// que obtiene esos valores directamente del XML del catálogo (fuente de verdad).
///
/// <para><b>Rendimiento:</b></para>
/// Un solo pass sobre el array del idioma activo.
/// Sin LINQ, sin heap allocations en el loop de matching.
/// Span-based para la búsqueda de tokens.
/// </summary>
public static class SearchIndexExtensions
{
    /// <summary>
    /// Filtra el índice por tokens de texto para el idioma dado.
    /// Todos los tokens deben estar presentes (AND implícito).
    /// </summary>
    /// <param name="index">Índice keyed por código de idioma.</param>
    /// <param name="tokens">Query parseado — solo se usan <see cref="SearchQuery.TextTokens"/>.</param>
    /// <param name="language">Código de idioma activo (ej. "ES-ES", "EN-US").</param>
    /// <returns>
    /// ItemIds que satisfacen el texto de la query.
    /// Lista vacía — nunca null — si no hay texto, no hay entradas o no hay coincidencias.
    /// </returns>
    public static IReadOnlyList<string> Filter(
        this FrozenDictionary<string, SearchEntry[]> index,
        string[] tokens,
        string language)
    {
        // Sin tokens de texto no hay nada que filtrar por nombre.
        // Tier y encantamiento se tratan como post-filtros sobre ItemBase.
        if (!(tokens is { Length: > 0})) return [];

        if (!index.TryGetValue(language, out var entries)) return [];

        var results = new List<string>(128);

        foreach (var entry in entries)
        {
            var haystack = entry.NormalizedText.AsSpan();
            bool match   = true;

            // AND implícito: todos los tokens deben estar presentes en el nombre.
            for (int i = 0; i < tokens.Length; i++)
            {
                if (!haystack.Contains(tokens[i].AsSpan(), StringComparison.Ordinal))
                {
                    match = false;
                    break;
                }
            }

            if (match) results.Add(entry.ItemId);
        }

        return results;
    }
}
