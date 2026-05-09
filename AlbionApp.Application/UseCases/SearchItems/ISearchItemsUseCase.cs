namespace AlbionApp.Application.UseCases.SearchItems;

/// <summary>
/// Contrato del caso de uso de búsqueda de ítems.
///
/// <para><b>Decisión arquitectónica</b>: la búsqueda es expuesta como un caso de
/// uso explícito (no como una colección de servicios sueltos) por tres razones:</para>
/// <list type="bullet">
///   <item><b>Reutilización real</b>: una futura UI (MAUI, web), un endpoint de
///         API o un comando CLI consumen exactamente la misma operación con la
///         misma firma — sin duplicar parsing/filtros/orden.</item>
///   <item><b>Testabilidad</b>: la unidad bajo test es la operación completa
///         (entrada → salida), no un encadenamiento manual de helpers.</item>
///   <item><b>Frontera de transacción</b>: facilita añadir cross-cutting concerns
///         (caching, logging, metrics, cancellation) en un único punto en el
///         futuro sin tocar la UI.</item>
/// </list>
///
/// <para><b>Asíncrono</b>: aunque la implementación actual es CPU-bound, exponer
/// la operación como <see cref="Task"/> evita romper el contrato cuando una
/// futura implementación añada IO (precios live, búsqueda federada, etc.).</para>
/// </summary>
public interface ISearchItemsUseCase
{
    /// <summary>
    /// Ejecuta la búsqueda. La operación es cancelable; al cancelarse retorna
    /// inmediatamente sin propagar excepciones específicas (responsabilidad
    /// del caller manejarlo).
    /// </summary>
    Task<SearchItemsResult> ExecuteAsync(SearchItemsQuery query, CancellationToken ct = default);
}
