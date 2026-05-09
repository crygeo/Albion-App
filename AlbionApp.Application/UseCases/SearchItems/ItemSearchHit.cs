namespace AlbionApp.Application.UseCases.SearchItems;

/// <summary>
/// Proyección de búsqueda de un ítem.
///
/// <para><b>Decisión arquitectónica</b>: este DTO es la salida del caso de uso
/// <see cref="ISearchItemsUseCase"/> y es lo que la UI consume directamente.
/// No es la entidad de dominio (<c>ItemBase</c>) y no es un VM.</para>
///
/// <para><b>Razones para separarla de <c>ItemBase</c></b>:</para>
/// <list type="bullet">
///   <item><b>SRP</b>: <c>ItemBase</c> agrega todos los datos del catálogo (incluido
///         <c>RawAttributes</c>, ~30 entradas/string por ítem). La UI de búsqueda
///         sólo necesita un puñado de campos para renderizar tiles.</item>
///   <item><b>Footprint y allocations</b>: cuando se devuelven cientos de hits,
///         exponer un record minimal evita arrastrar diccionarios pesados a la UI
///         y reduce presión sobre el GC durante búsquedas en vivo.</item>
///   <item><b>Estabilidad de contrato</b>: la UI depende solo de los campos que
///         realmente consume. Los detalles del XML del juego pueden evolucionar en
///         <c>ItemBase</c> sin propagarse a la UI.</item>
///   <item><b>Reutilización entre clientes</b>: una futura app cliente (MAUI, API)
///         puede consumir esta misma proyección; <c>ItemBase</c> queda como modelo
///         interno del backend.</item>
/// </list>
///
/// <para>Inmutable y comparable por valor. Apto para colecciones reactivas y
/// tests de aserción exacta.</para>
/// </summary>
public sealed record ItemSearchHit(
    string  ItemId,
    string  BaseItemId,
    string? NameLocalizationKey,
    int?    Tier,
    int     EnchantmentLevel,
    int     CategoryValue,
    string  TierLabel);
