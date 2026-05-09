namespace Albion_App.Models;

/// <summary>
/// Opción de bono por diario de rendimiento para el ComboBox de la Card 2.
///
/// <para>
/// Record inmutable con override de <see cref="ToString"/> para que WPF pueda
/// renderizarlo directamente en el ComboBox sin necesidad de un ItemTemplate
/// adicional (display-path implícito).
/// </para>
/// </summary>
/// <param name="Value">Factor decimal de bonificación (0.00, 0.10, 0.20).</param>
/// <param name="Label">Texto legible para el usuario.</param>
public sealed record JournalBonusOptionM(decimal Value, string Label)
{
    public override string ToString() => Label;
}
