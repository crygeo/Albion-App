using System.Windows.Controls;

namespace Albion_App._1Calculadora;

/// <summary>
/// Control off-screen para renderizar los resultados de la calculadora como imagen PNG.
/// Solo se instancia durante el proceso de exportación — no se muestra en pantalla.
/// DataContext debe ser <see cref="CalculadoraSvm"/>.
/// </summary>
public partial class CalculadoraExportControl : UserControl
{
    public CalculadoraExportControl() => InitializeComponent();
}
