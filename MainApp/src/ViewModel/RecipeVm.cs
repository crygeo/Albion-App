using Albion_App.Components.Market;
using AlbionApp.Domain.ItemSearch;
using ItemBaseVm = Albion_App.Components.Item.ItemBaseVm;

namespace Albion_App.ViewModel;

/// <summary>
/// ViewModel de una receta de crafteo para el carrusel (Card 2).
///
/// Implementa <see cref="IRecipe"/> del dominio para que el código de negocio
/// (calculadora, motor de crafteo) pueda consumirla sin conocer el tipo WPF.
///
/// Diseño de la colección de ingredientes:
///   La propiedad pública <see cref="Ingredients"/> expone <see cref="IngredientVm"/>
///   (tipo concreto) para que XAML y <c>CalculadoraSvm</c> accedan a <c>Item.Image</c>
///   y <c>Item.DisplayName</c> sin casts.
///   La implementación explícita <c>IRecipe.Ingredients</c> satisface el contrato del
///   dominio sin exponer <c>IReadOnlyList&lt;IIngredient&gt;</c> como API pública del VM,
///   evitando confusión en el lado de presentación.
///
/// Inmutable por diseño: se construye al seleccionar un ítem y no cambia
/// hasta que el usuario escoge otro.
/// </summary>
public sealed class RecipeVm : IRecipe
{
    /// <summary>Posición base-0 dentro de la colección de recetas del ítem.</summary>
    public int Index { get; init; }

    /// <summary>Etiqueta principal del carrusel: "Receta de Crafteo", "Receta de Mejora", etc.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Detalle secundario: "×1 producido por ciclo", "T4 → T4.1", etc.</summary>
    public string SubLabel { get; init; } = string.Empty;

    // ── IRecipe ───────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public bool IsTransmutation { get; init; }

    /// <inheritdoc/>
    public int AmountCrafted { get; init; } = 1;

    /// <inheritdoc/>
    public int? CraftingFocus { get; init; }

    /// <inheritdoc/>
    public decimal? Silver { get; init; }

    /// <summary>
    /// Ingredientes de la receta tipados como <see cref="IngredientVm"/> para
    /// acceso directo a <c>Item.Image</c> desde XAML y desde <c>CalculadoraSvm</c>.
    /// </summary>
    public IReadOnlyList<IngredientVm> Ingredients { get; init; } = [];

    /// <summary>
    /// Implementación explícita de <see cref="IRecipe.Ingredients"/>.
    /// Retorna la misma colección upcast — sin copia, sin overhead.
    /// </summary>
    IReadOnlyList<IIngredient> IRecipe.Ingredients => Ingredients;
}

/// <summary>
/// ViewModel de un ingrediente de receta.
///
/// Implementa <see cref="IIngredient"/> del dominio delegando <see cref="ItemId"/>
/// al <see cref="ItemBaseVm"/> ya construido, que porta imagen reactiva y nombre
/// localizado. No duplica datos: el ítem es la única fuente de verdad.
/// </summary>
public sealed class IngredientVm : IIngredient
{
    /// <summary>
    /// VM del ítem ingrediente: imagen reactiva + nombre localizado.
    /// Se construye vía <c>ItemSearchVM.BuildItemVm</c> con fire-and-forget de imagen.
    /// </summary>
    public ItemBaseVm Item { get; init; } = null!;

    // ── IIngredient ───────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public string ItemId => Item.ItemId;

    /// <inheritdoc/>
    public int Count { get; init; }

    /// <inheritdoc/>
    public bool ParticipatesInReturn { get; init; }

    // ── Shorthands para bindings XAML limpios ─────────────────────────────────

    public string DisplayName => Item.DisplayName;
}
