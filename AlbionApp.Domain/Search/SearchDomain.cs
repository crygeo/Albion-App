namespace AlbionApp.Domain.Search;

/// <summary>
/// Dominio semántico del índice de búsqueda.
///
/// Cada valor corresponde a un prefijo de clave en el archivo TMX del juego:
/// <list type="bullet">
///   <item><see cref="Items"/>  → prefijo <c>@ITEMS_</c></item>
///   <item><see cref="Market"/> → prefijo <c>@MARKETPLACEGUI_ROLLOUT_</c></item>
///   <item><see cref="Destinyboard"/> → prefijo <c>@DESTINYBOARD_TITLE_</c></item>
/// </list>
///
/// El dominio determina qué índice secundario consulta <c>ILocalizationService.GetTexts</c>.
/// Extender con nuevos dominios solo requiere agregar el valor aquí y construir
/// el índice correspondiente en <c>LocalizationService.OnStartAsync</c> — sin cambios en el motor.
/// </summary>
public enum SearchDomain
{
    /// <summary>
    /// Catálogo completo de ítems del juego.
    /// Claves con prefijo <c>@ITEMS_</c> en el TMX.
    /// Retorna ItemIds para lookup directo en ItemDataService.
    /// </summary>
    Items,

    /// <summary>
    /// Categorías de la interfaz de mercado.
    /// Claves con prefijo <c>@MARKETPLACEGUI_ROLLOUT_</c> en el TMX.
    /// </summary>
    Market,

    /// <summary>
    /// Titles del destiny board y achievements.
    /// Claves con prefijo <c>@DESTINYBOARD_TITLE_</c>.
    /// </summary>
    Destinyboard
}
