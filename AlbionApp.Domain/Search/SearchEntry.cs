namespace AlbionApp.Domain.Search;

/// <summary>
/// Entrada del índice de búsqueda de localización para un ítem.
///
/// Diseñada para hot-path: todos los campos se calculan UNA SOLA VEZ durante
/// el startup — el motor de búsqueda solo lee y compara, nunca transforma.
///
/// <list type="bullet">
///   <item><see cref="ItemId"/> — ID del ítem listo para lookup directo en ItemDataService.</item>
///   <item><see cref="NormalizedText"/> — Nombre localizado pre-normalizado (sin diacríticos,
///     minúsculas). Comparación directa sin transformación en cada búsqueda.</item>
///   <item><see cref="Metadata"/> — Tier/Enchantment extraídos de la clave TMX. Filtros
///     estructurales en O(1) sin parsing.</item>
/// </list>
///
/// Inmutable: creada una vez, compartida entre hilos sin locks.
/// </summary>
public sealed record SearchEntry(
    string         ItemId,
    string         NormalizedText,
    SearchMetadata Metadata);
