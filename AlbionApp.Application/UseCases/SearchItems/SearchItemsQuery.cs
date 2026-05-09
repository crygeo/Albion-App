namespace AlbionApp.Application.UseCases.SearchItems;

/// <summary>
/// Entrada inmutable del caso de uso <see cref="ISearchItemsUseCase"/>.
///
/// <para><b>Decisión arquitectónica</b>: la query es un value object (record struct
/// readonly) — pasa por valor sin allocations, expone únicamente lo que el caller
/// puede controlar y es trivialmente testeable. El caso de uso decide internamente
/// cómo combinar tier/encantamiento provenientes del texto vs. los dropdowns
/// (precedencia, parseo, etc.) — el caller no necesita saberlo.</para>
///
/// <para><b>Por qué aceptar el código de idioma como parámetro</b>: la búsqueda es
/// idempotente para una entrada dada. Capturar el idioma activo al momento del
/// ejecutor (en lugar de leerlo dinámicamente del servicio durante la ejecución)
/// hace al caso de uso libre de estado oculto y reproducible.</para>
/// </summary>
public readonly record struct SearchItemsQuery(
    /// <summary>Texto raw del cuadro de búsqueda. Null o vacío = sin filtro de texto.</summary>
    string? RawText,

    /// <summary>Tier seleccionado en el dropdown. Tiene precedencia sobre el del texto.</summary>
    int? TierFilter,

    /// <summary>Encantamiento del dropdown. Tiene precedencia sobre el del texto.</summary>
    int? EnchantmentFilter,

    /// <summary>Límite inferior del rango de categoría (incluyente). Null = sin filtro.</summary>
    int? MinCategoryValue,

    /// <summary>Límite superior del rango de categoría (incluyente). Null = sin filtro.</summary>
    int? MaxCategoryValue,

    /// <summary>Código de idioma activo (ej. "ES-ES", "EN-US"). Determina el índice consultado.</summary>
    string LanguageCode,

    /// <summary>Cantidad máxima de resultados a retornar. 0 o negativo = sin límite.</summary>
    int MaxResults);
