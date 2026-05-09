using AlbionApp.Domain.ItemSearch;

namespace AlbionApp.Domain.Interfaces;

/// <summary>
/// Contrato del servicio de categorías de tienda de Albion Online.
///
/// Expone el árbol de <see cref="Category"/> sin localización, listo para que
/// la capa de presentación construya el árbol de navegación localizado.
///
/// <see cref="TreeChanged"/> implementa semántica de replay: si el árbol ya está
/// disponible cuando alguien se suscribe, el handler se invoca inmediatamente.
/// Esto evita race conditions entre la carga asíncrona y la suscripción tardía del ViewModel.
/// </summary>
public interface ICategoryDataService
{
    /// <summary>
    /// Notifica que el árbol fue (re)construido — carga inicial o cambio de idioma.
    /// Implementa replay: el handler se invoca inmediatamente si el árbol ya existe.
    /// Puede dispararse desde hilos de thread pool.
    /// </summary>
    event EventHandler? TreeChanged;

    /// <summary>
    /// Retorna las categorías crudas del juego ordenadas por <see cref="Category.SortValue"/>.
    /// Los nombres son IDs de localización, no texto legible.
    /// Lista vacía hasta que el servicio esté activo.
    /// </summary>
    IReadOnlyList<Category> GetRawCategories();
}
