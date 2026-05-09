using System.Collections.Frozen;
using System.Xml.Linq;

namespace AlbionApp.Infrastructure.Services;

/// <summary>
/// Índice de resolución rápida de categorías string → valor numérico acumulado.
///
/// <para><b>Propósito:</b> permite a <see cref="ItemDataService"/> convertir los cuatro
/// atributos de categoría de cada ítem (<c>shopcategory</c>, <c>shopsubcategory1</c>,
/// <c>shopsubcategory2</c>, <c>shopsubcategory3</c>) a un único <c>int</c> durante
/// el parseo inicial, sin comparaciones string en caliente.</para>
///
/// <para><b>Estructura interna:</b> árbol de <see cref="FrozenDictionary{TKey,TValue}"/>
/// anidados que espeja la jerarquía XML, con lookup O(depth) ≈ O(4) por ítem.</para>
///
/// <para><b>Ciclo de vida:</b> se construye una sola vez al inicio de
/// <see cref="ItemDataService.OnStartAsync"/> a partir del mismo <see cref="XDocument"/>
/// de items.bin (ya en memoria — sin IO adicional). Es inmutable tras su construcción.</para>
/// </summary>
internal sealed class CategoryValueIndex
{
    // ── Nodo interno ──────────────────────────────────────────────────────────

    private sealed record Node(
        int                                   AccumulatedValue,
        FrozenDictionary<string, Node>        Children);

    // ── Instancia vacía ───────────────────────────────────────────────────────

    public static readonly CategoryValueIndex Empty =
        new(FrozenDictionary<string, Node>.Empty);

    // ── Estado ────────────────────────────────────────────────────────────────

    private readonly FrozenDictionary<string, Node> _roots;

    private CategoryValueIndex(FrozenDictionary<string, Node> roots)
        => _roots = roots;

    // ── Factory ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Construye el índice parseando la sección <c>&lt;shopcategories&gt;</c>
    /// del XDocument de items.bin. O(n) con n = nodos de categoría.
    /// </summary>
    public static CategoryValueIndex Build(XDocument doc)
    {
        var root = doc.Root;
        if (root is null) return Empty;

        var container = root.Elements()
            .FirstOrDefault(e => Named(e, "shopcategories"));

        if (container is null) return Empty;

        var roots = BuildLevel(
            container.Elements().Where(e => Named(e, "shopcategory")),
            depth:             0,
            parentAccumulated: 0);

        return new CategoryValueIndex(roots);
    }

    // ── Resolución ────────────────────────────────────────────────────────────

    /// <summary>
    /// Convierte los cuatro IDs de categoría de un ítem a su <c>CategoryValue</c> entero.
    ///
    /// <para>Toma el <c>AccumulatedValue</c> del nodo más profundo encontrado.
    /// Si un nivel es <c>null</c> o no existe en el árbol, se devuelve el acumulado
    /// del nivel anterior. Retorna 0 si el ítem no tiene categoría.</para>
    /// </summary>
    public int Resolve(string? cat0, string? cat1, string? cat2, string? cat3)
    {
        if (cat0 is null || !_roots.TryGetValue(cat0, out var n0)) return 0;
        if (cat1 is null || !n0.Children.TryGetValue(cat1, out var n1)) return n0.AccumulatedValue;
        if (cat2 is null || !n1.Children.TryGetValue(cat2, out var n2)) return n1.AccumulatedValue;
        if (cat3 is null || !n2.Children.TryGetValue(cat3, out var n3)) return n2.AccumulatedValue;
        return n3.AccumulatedValue;
    }

    // ── Construcción del árbol ────────────────────────────────────────────────

    private static FrozenDictionary<string, Node> BuildLevel(
        IEnumerable<XElement> elements,
        int                   depth,
        int                   parentAccumulated)
    {
        var dict = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);

        var childTag = depth == 0 ? "shopsubcategory" : $"shopsubcategory{depth + 1}";

        foreach (var el in elements)
        {
            var id = (string?)el.Attribute("id");
            if (string.IsNullOrEmpty(id)) continue;

            var value       = ParseInt(el, "value");
            var accumulated = parentAccumulated + value;

            var children = BuildLevel(
                el.Elements().Where(c => Named(c, childTag)),
                depth + 1,
                accumulated);

            dict[id] = new Node(accumulated, children);
        }

        return dict.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool Named(XElement el, string localName)
        => el.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase);

    private static int ParseInt(XElement el, string attr)
        => int.TryParse((string?)el.Attribute(attr), out var v) ? v : 0;
}
