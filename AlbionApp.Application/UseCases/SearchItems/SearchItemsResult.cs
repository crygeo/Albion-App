namespace AlbionApp.Application.UseCases.SearchItems;

/// <summary>
/// Resultado inmutable del caso de uso <see cref="ISearchItemsUseCase"/>.
///
/// <para><b>Por qué un wrapper en lugar de retornar directamente la lista</b>:
/// permite extender el contrato (totales, paginación, telemetry, métricas) sin
/// romper a los consumidores. Hoy expone solo <see cref="Hits"/>; mañana podrá
/// añadir <c>TotalMatched</c>, <c>WasTruncated</c>, etc. de forma aditiva.</para>
/// </summary>
public sealed record SearchItemsResult(
    IReadOnlyList<ItemSearchHit> Hits)
{
    /// <summary>Resultado vacío reutilizable — sin allocations.</summary>
    public static readonly SearchItemsResult Empty = new([]);
}
