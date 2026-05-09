namespace AlbionApp.Domain.Search;

/// <summary>
/// Consulta de búsqueda descompuesta en tokens de texto normalizados y filtros
/// estructurales opcionales.
///
/// Resultado del parseo realizado por <c>QueryParser.Parse()</c> en la capa Application.
/// El motor de búsqueda trabaja sobre esta forma ya parseada — nunca sobre el raw text.
///
/// Gramática soportada (ejemplos):
/// <code>
///   "tabla 8.2"  →  TextTokens=["tabla"], Tier=8, Enchantment=2
///   "t4 espada"  →  TextTokens=["espada"], Tier=4, Enchantment=null
///   ".3"         →  TextTokens=[], Tier=null, Enchantment=3
///   "arco t6.1"  →  TextTokens=["arco"], Tier=6, Enchantment=1
///   ""           →  SearchQuery.Empty
/// </code>
///
/// Inmutable: los arrays son snapshots del momento de parseo.
/// </summary>
public readonly struct SearchQuery
{
    /// <summary>Tokens de texto normalizados (sin diacríticos, minúsculas, sin stop words).</summary>
    public readonly string[] TextTokens;

    /// <summary>Filtro de tier extraído del query. Null = sin restricción de tier.</summary>
    public readonly int? Tier;

    /// <summary>Filtro de encantamiento extraído del query. Null = sin restricción de encantamiento.</summary>
    public readonly int? Enchantment;

    public SearchQuery(string[] textTokens, int? tier, int? enchantment)
    {
        TextTokens  = textTokens;
        Tier        = tier;
        Enchantment = enchantment;
    }

    /// <summary>La consulta contiene al menos un token de texto significativo.</summary>
    public bool HasTextTokens => TextTokens is { Length: > 0 };

    /// <summary>La consulta especifica al menos un filtro estructural (tier o enchantment).</summary>
    public bool HasStructural => Tier.HasValue || Enchantment.HasValue;

    /// <summary>
    /// La consulta no contiene ningún criterio — sin filtrado de texto ni estructural.
    /// El motor debe retornar todos los ítems del dominio.
    /// </summary>
    public bool IsEmpty => !HasTextTokens && !HasStructural;

    /// <summary>Consulta vacía que no filtra ningún ítem.</summary>
    public static readonly SearchQuery Empty = new([], null, null);
}
