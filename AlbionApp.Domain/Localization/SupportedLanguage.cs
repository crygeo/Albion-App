namespace AlbionApp.Domain.Localization;

/// <summary>
/// Idioma soportado por la aplicación.
///
/// <para><b>Decisión arquitectónica</b>: este tipo vive en el dominio porque la
/// localización es una preocupación transversal del modelo (los servicios de
/// localización, búsqueda y presentación lo consumen) y no debe estar atada a
/// ninguna librería externa concreta.</para>
///
/// <para>Inmutable y comparable por valor (<see cref="System.Collections.Generic.IEqualityComparer{T}"/>
/// nativo de records). Apto para usarse como clave en colecciones, mensajes
/// inmutables y reactividad.</para>
///
/// <para><b>Catálogo estático</b>: el conjunto de idiomas que el cliente del
/// juego soporta es fijo y conocido en compile-time. Exponerlo como
/// <see cref="All"/> evita IO innecesario y permite resolver códigos a instancias
/// canónicas (igualdad referencial cuando se quiere optimizar).</para>
/// </summary>
public sealed record SupportedLanguage(
    string Code,
    string DisplayName,
    string FlagEmoji)
{
    /// <summary>Representación compacta para UI: bandera + nombre.</summary>
    public string Label => $"{FlagEmoji} {DisplayName}";

    public override string ToString() => Label;

    // ── Catálogo canónico ─────────────────────────────────────────────────────

    /// <summary>
    /// Idiomas soportados por el cliente del juego. Las claves son los códigos
    /// usados en los archivos TMX del juego (ej. "EN-US", "ES-ES").
    /// </summary>
    public static readonly IReadOnlyDictionary<string, SupportedLanguage> All =
        new Dictionary<string, SupportedLanguage>(StringComparer.OrdinalIgnoreCase)
        {
            ["EN-US"] = new("EN-US", "English",          "🇺🇸"),
            ["ES-ES"] = new("ES-ES", "Español",          "🇪🇸"),
            ["DE-DE"] = new("DE-DE", "Deutsch",          "🇩🇪"),
            ["FR-FR"] = new("FR-FR", "Français",         "🇫🇷"),
            ["RU-RU"] = new("RU-RU", "Русский",          "🇷🇺"),
            ["PL-PL"] = new("PL-PL", "Polski",           "🇵🇱"),
            ["PT-BR"] = new("PT-BR", "Português (BR)",   "🇧🇷"),
            ["ZH-CN"] = new("ZH-CN", "中文 (简体)",       "🇨🇳"),
            ["ZH-TW"] = new("ZH-TW", "中文 (繁體)",       "🇹🇼"),
            ["KO-KR"] = new("KO-KR", "한국어",            "🇰🇷"),
            ["JA-JP"] = new("JA-JP", "日本語",            "🇯🇵"),
            ["TR-TR"] = new("TR-TR", "Türkçe",           "🇹🇷"),
            ["AR-SA"] = new("AR-SA", "العربية",          "🇸🇦"),
            ["IT-IT"] = new("IT-IT", "Italiano",         "🇮🇹"),
            ["CS-CZ"] = new("CS-CZ", "Čeština",          "🇨🇿"),
            ["ID-ID"] = new("ID-ID", "Bahasa Indonesia", "🇮🇩"),
        };

    /// <summary>
    /// Busca un idioma por código (case-insensitive).
    /// Retorna <c>null</c> si el código es inválido o no soportado.
    /// </summary>
    public static SupportedLanguage? Find(string? code)
        => string.IsNullOrWhiteSpace(code)
            ? null
            : All.GetValueOrDefault(code);
}
