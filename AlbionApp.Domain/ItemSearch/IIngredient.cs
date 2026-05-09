namespace AlbionApp.Domain.ItemSearch;

/// <summary>
/// Contrato de un ingrediente de receta de crafteo.
///
/// Implementado por:
/// - <see cref="Ingredient"/> (record inmutable del dominio)
/// - Future: <c>ItemIngredientVm</c> (wrapper WPF con imagen reactiva)
///
/// Separado de <see cref="IItemBase"/> para que un mismo ítem pueda actuar
/// como ingrediente sin duplicar datos (Item es la referencia al ítem fuente).
/// </summary>
public interface IIngredient
{
    /// <summary>Ítem que actúa como ingrediente (imagen, nombre, tier…).</summary>
    string ItemId { get; }

    /// <summary>Cantidad requerida por ciclo de crafteo.</summary>
    int Count { get; }

    /// <summary>
    /// True si este ingrediente participa en el cálculo de retorno de materiales.
    /// En Albion: los recursos base participan; el ítem principal no.
    /// </summary>
    bool ParticipatesInReturn { get; }
}
