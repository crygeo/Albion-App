namespace AlbionApp.Domain.Text;

/// <summary>
/// Normalización de texto a forma canónica para indexación y búsqueda.
///
/// <para><b>Decisión arquitectónica</b>: vive en el dominio porque es una primitiva
/// transversal — la usan tanto la capa Infrastructure (para construir índices a
/// partir del XML del juego) como la capa Application (para tokenizar la query
/// del usuario). Mantenerla en Application generaba la dependencia espuria
/// <c>Infrastructure → Application</c>; al ser una utilidad de texto sin lógica
/// de negocio, su lugar correcto es Domain.</para>
///
/// <para><b>Reglas aplicadas</b> (en orden):</para>
/// <list type="number">
///   <item>Conversión a minúsculas (cultura invariante).</item>
///   <item>Diacríticos reemplazados por su base (á→a, é→e, ñ→n…).</item>
///   <item>Puntuación y separadores (<c>- _ , ( ) [ ]</c>) → espacio.</item>
/// </list>
///
/// <para><b>Rendimiento</b>: span-based, sin allocations en el hot path —
/// se invoca decenas de miles de veces durante el build del índice.</para>
///
/// Sin estado, sin dependencias, totalmente testeable.
/// </summary>
public static class TextNormalizer
{
    // Stop words multi-idioma. Array estático — sin allocations en IsStopWord.
    private static readonly string[] StopWords =
    [
        // Español
        "de", "del", "el", "la", "los", "las", "y", "en", "con",
        // Inglés
        "the", "of", "and", "with", "a", "an", "in",
        // Portugués
        "da", "do", "e",
        // Otros comunes
        "i"
    ];

    // ── API pública ───────────────────────────────────────────────────────────

    /// <summary>
    /// Normaliza el texto a forma canónica continua (sin split). Usado para construir
    /// los textos del índice durante el startup. Retorna cadena vacía para entrada nula.
    /// </summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        Span<char> buffer = stackalloc char[text.Length];
        int len = 0;

        foreach (char c in text.ToLowerInvariant())
            buffer[len++] = MapChar(c);

        return buffer[..len].ToString();
    }

    /// <summary>
    /// Normaliza, divide en tokens y filtra stop words. Usado para tokenizar
    /// queries de búsqueda. Retorna array vacío para entrada nula, vacía o
    /// compuesta solo de stop words.
    /// </summary>
    public static string[] Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        Span<char> buffer = stackalloc char[text.Length];
        int len = 0;

        foreach (char c in text.ToLowerInvariant())
            buffer[len++] = MapChar(c);

        var parts = buffer[..len].ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var result = new List<string>(parts.Length);
        foreach (var word in parts)
        {
            if (!IsStopWord(word))
                result.Add(word);
        }

        return result.Count == 0 ? [] : result.ToArray();
    }

    /// <summary>
    /// Retorna <c>true</c> si la palabra (ya normalizada) es stop word.
    /// Comparación Ordinal — sin allocations.
    /// </summary>
    public static bool IsStopWord(string word)
    {
        foreach (var sw in StopWords)
        {
            if (string.Equals(sw, word, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    // ── Mapping de caracteres ─────────────────────────────────────────────────

    private static char MapChar(char c) => c switch
    {
        'á' or 'à' or 'ä' or 'â' => 'a',
        'é' or 'è' or 'ë' or 'ê' => 'e',
        'í' or 'ì' or 'ï' or 'î' => 'i',
        'ó' or 'ò' or 'ö' or 'ô' => 'o',
        'ú' or 'ù' or 'ü' or 'û' => 'u',
        'ñ'                       => 'n',
        'ç'                       => 'c',
        ' ' or ',' or '-' or '_'
            or '(' or ')' or '[' or ']'
            or '/' or '\\' or '.' => ' ',
        _ => c
    };
}
