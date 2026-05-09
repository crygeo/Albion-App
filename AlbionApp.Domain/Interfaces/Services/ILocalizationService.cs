using System.Collections.Frozen;
using AlbionApp.Domain.Localization;
using AlbionApp.Domain.Search;

namespace AlbionApp.Domain.Interfaces;

/// <summary>
/// Contrato de localización de la aplicación.
///
/// Diseño orientado a VMs:
///   • <see cref="GetText"/> recibe sólo la clave — el idioma activo es responsabilidad
///     interna del servicio. Los VMs no necesitan conocer el idioma actual.
///   • <see cref="LanguageChanged"/> notifica a los VMs suscritos que deben actualizar
///     sus propiedades observables (llamar de nuevo a GetText con las mismas claves).
///   • Cambiar el idioma activo es responsabilidad de <see cref="SetLanguage"/>.
///
/// Garantías:
///   • <see cref="GetText"/> siempre retorna un string no-null:
///       1. Texto en el idioma activo.
///       2. Fallback a EN-US.
///       3. Fallback al propio <paramref name="key"/> si no hay traducción.
///   • <see cref="LanguageChanged"/> puede dispararse desde cualquier hilo —
///     los suscriptores son responsables de despachar al hilo UI si lo necesitan.
/// </summary>
public interface ILocalizationService
{
    /// <summary>Idioma activo (e.g., "ES-ES", "EN-US").</summary>
    SupportedLanguage CurrentSupportedLanguage { get; }

    /// <summary>Idiomas disponibles según el store cargado.</summary>
    IReadOnlyList<SupportedLanguage> AvailableLanguages { get; }

    /// <summary>
    /// Índice de búsqueda por texto preconstruido.
    /// Keyed por código de idioma; cada valor es el array de <see cref="SearchEntry"/>
    /// para ese idioma. Consumido por <c>MarketVm</c> y el pipeline de búsqueda.
    /// </summary>
    FrozenDictionary<string, SearchEntry[]> ItemSearchIndex { get; }

    /// <summary>
    /// Notifica que el idioma activo cambió.
    /// Los VMs suscritos deben refrescar sus propiedades localizadas.
    /// </summary>
    event EventHandler? LanguageChanged;

    /// <summary>
    /// Retorna el texto localizado para <paramref name="key"/> en el idioma activo.
    /// Nunca lanza — retorna la clave si no hay traducción disponible.
    /// </summary>
    string GetText(string key);

    /// <summary>
    /// Cambia el idioma activo y dispara <see cref="LanguageChanged"/>.
    /// Es idempotente: si el idioma ya es el activo, no dispara el evento.
    /// </summary>
    void SetLanguage(SupportedLanguage supportedLanguageCode);
}
