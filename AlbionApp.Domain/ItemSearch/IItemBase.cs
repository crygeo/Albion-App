namespace AlbionApp.Domain.ItemSearch;

/// <summary>
/// Contrato de datos de un ítem de Albion Online.
///
/// Principio central: el ítem NO conoce su nombre ni descripción localizada.
/// Esos son textos de presentación que la capa de UI resuelve en tiempo de
/// ejecución usando <c>ILocalizationService.GetText(NameLocalizationKey)</c>.
///
/// <see cref="NameLocalizationKey"/> y <see cref="DescriptionLocalizationKey"/>
/// son las claves para que el VM consulte al servicio de localización.
///
/// Implementado por:
/// - <see cref="ItemBase"/>  (record inmutable del dominio)
/// - <c>ItemBaseVm</c>       (wrapper WPF: añade imagen reactiva y nombre localizado)
/// </summary>
public interface IItemBase
{
    /// <summary>Identificador único incluyendo encantamiento: "T4_MAIN_SWORD@2".</summary>
    string ItemId { get; }

    /// <summary>Identificador base sin encantamiento: "T4_MAIN_SWORD".</summary>
    string BaseItemId { get; }

    /// <summary>
    /// Clave de localización para el nombre (atributo descvariable0 en el XML).
    /// Ejemplo: "@ITEMS_T4_MAIN_SWORD". Null si el ítem no tiene clave de nombre.
    /// </summary>
    string? NameLocalizationKey { get; }

    /// <summary>
    /// Clave de localización para la descripción (atributo descriptionlocatag).
    /// Null si el ítem no tiene descripción localizada.
    /// </summary>
    string? DescriptionLocalizationKey { get; }

    /// <summary>Tier del ítem (1–8). Null para ítems sin tier.</summary>
    int? Tier { get; }

    /// <summary>Nivel de encantamiento (0–4).</summary>
    int EnchantmentLevel { get; }

    /// <summary>
    /// Valor numérico que codifica la posición del ítem en la jerarquía de categorías de tienda.
    /// Es la suma de los atributos <c>value</c> de cada nivel (shopcategory + shopsubcategory1 + …).
    /// 0 significa sin categoría.
    ///
    /// <para>Uso para filtrado por rango:</para>
    /// <code>
    ///   // Todos los ítems de una categoría:
    ///   CategoryValue >= category.AccumulatedValue &amp;&amp;
    ///   CategoryValue &lt;= category.MaxDescendantValue
    /// </code>
    /// </summary>
    int CategoryValue { get; }

    /// <summary>Categoría de crafteo (e.g., "plate_armor"). Null si no es crafteable.</summary>
    string? CraftingCategory { get; }

    /// <summary>Tipo de nodo XML del ítem (e.g., "equipmentitem", "simpleitem").</summary>
    string? NodeType { get; }

    /// <summary>True si el ítem tiene al menos una receta de crafteo con ingredientes.</summary>
    bool IsCraftable { get; }

    /// <summary>
    /// Todos los atributos del elemento XML tal como aparecen en items.bin.
    /// Permite acceder a atributos específicos de un tipo de ítem
    /// (e.g., "itempower", "physicalarmor") sin necesidad de tipado fuerte en el dominio.
    /// </summary>
    IReadOnlyDictionary<string, string> RawAttributes { get; }

    /// <summary>
    /// Etiqueta de tier/encantamiento: "T4", "T4.1", "T4.2", etc.
    /// String vacío si el ítem no tiene tier.
    /// </summary>
    string TierLabel { get; }
}
