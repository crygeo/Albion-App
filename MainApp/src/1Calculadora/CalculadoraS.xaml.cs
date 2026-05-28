using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Albion_App._4Groups.Export;
using Utilidades.Dialogs;

namespace Albion_App._1Calculadora;

/// <summary>
/// Code-behind de CalculadoraS.
/// Solo maneja lo que requiere acceso al árbol visual (export PNG).
/// Toda la lógica de negocio vive en <see cref="CalculadoraSvm"/>.
/// </summary>
public partial class CalculadoraS : UserControl
{
    public CalculadoraS()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private CalculadoraSvm? _vm;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
            _vm.ExportRequested -= OnExportRequested;

        _vm = e.NewValue as CalculadoraSvm;

        if (_vm is not null)
            _vm.ExportRequested += OnExportRequested;
    }

    private async void OnExportRequested()
    {
        if (_vm is null) return;

        // ── Render off-screen — mismo patrón que BuildGroupImageExporter ──────
        // CalculadoraExportControl NO está en el árbol visual: Measure+Arrange
        // resuelven el layout sin las restricciones de la columna en pantalla.
        var control = new CalculadoraExportControl { DataContext = _vm };

        control.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        control.Arrange(new Rect(control.DesiredSize));
        control.UpdateLayout();

        var width  = (int)Math.Ceiling(Math.Max(control.DesiredSize.Width,  1));
        var height = (int)Math.Ceiling(Math.Max(control.DesiredSize.Height, 1));
        if (width <= 0 || height <= 0) return;

        // DPI: tomado de cualquier ventana abierta; fallback a 96.
        var anyWindow = Application.Current.Windows.OfType<Window>().FirstOrDefault();
        var source    = anyWindow is not null ? PresentationSource.FromVisual(anyWindow) : null;
        var dpiX      = source?.CompositionTarget?.TransformToDevice.M11 * 96.0 ?? 96.0;
        var dpiY      = source?.CompositionTarget?.TransformToDevice.M22 * 96.0 ?? 96.0;

        var rtb = new RenderTargetBitmap(width, height, dpiX, dpiY, PixelFormats.Pbgra32);
        rtb.Render(control);
        rtb.Freeze();

        // ── Abrir diálogo con preview + copiar / guardar ──────────────────────
        var fileName = _vm.SelectedItem?.ItemId ?? "crafting";
        var dialogVm = new ExportDialogVm(rtb, fileName);
        var dialog   = new ExportDialogV(dialogVm);
        await DialogService.Instance.MostrarDialogo(dialog);
    }
}
