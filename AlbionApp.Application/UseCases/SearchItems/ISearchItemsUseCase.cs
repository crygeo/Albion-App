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
    /// Ejecuta la búsqueda y devuelve TODOS los resultados al completar.
    /// La operación es cancelable; al cancelarse retorna inmediatamente sin
    /// propagar excepciones específicas (responsabilidad del caller manejarlo).
    ///
    /// <para>Útil para callers que necesitan el resultado completo antes de
    /// procesar (tests, futuras APIs/CLI). Para UIs interactivas preferí
    /// <see cref="StreamAsync"/>.</para>
    /// </summary>
    Task<SearchItemsResult> ExecuteAsync(SearchItemsQuery query, CancellationToken ct = default);

    /// <summary>
    /// Ejecuta la búsqueda y emite cada hit a medida que se proyecta.
    ///
    /// <para><b>Por qué streaming</b>: con resultados grandes (cientos de hits),
    /// devolver toda la lista de golpe obliga al consumidor a procesarla en un
    /// solo bloque — lo que en una UI se traduce en un freeze visible al volcar
    /// VMs y notificaciones. Con <see cref="IAsyncEnumerable{T}"/> el consumidor
    /// puede materializar y mostrar resultados en lotes pequeños, distribuyendo
    /// el costo y mejorando la latencia percibida del primer resultado.</para>
    ///
    /// <para><b>Garantías</b>:</para>
    /// <list type="bullet">
    ///   <item>El parsing, resolución de candidatos, filtrado y ordenamiento se
    ///         ejecutan en el thread pool antes del primer yield. Esto preserva
    ///         el orden global (no se mezclan resultados parcialmente ordenados).</item>
    ///   <item>La cancelación se honra entre yields y aborta inmediatamente.</item>
    ///   <item>El consumidor puede romper el await foreach con normalidad — la
    ///         enumeración termina sin efectos colaterales.</item>
    /// </list>
    /// </summary>
    IAsyncEnumerable<ItemSearchHit> StreamAsync(SearchItemsQuery query, CancellationToken ct = default);
}
