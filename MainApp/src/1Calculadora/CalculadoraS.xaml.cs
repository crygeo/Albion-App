using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

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

    private void OnExportRequested()
    {
        if (_vm is null) return;

        // ── Layout actualizado antes de medir ─────────────────────────────────
        Card4Root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Card4Root.Arrange(new Rect(Card4Root.DesiredSize));
        Card4Root.UpdateLayout();

        var dpi    = VisualTreeHelper.GetDpi(this);
        var w      = Card4Root.ActualWidth;
        var h      = Card4Root.ActualHeight;
        var pw     = (int)Math.Ceiling(w * dpi.DpiScaleX);
        var ph     = (int)Math.Ceiling(h * dpi.DpiScaleY);

        if (pw <= 0 || ph <= 0) return;

        // ── Renderizar con fondo sólido ───────────────────────────────────────
        // RenderTargetBitmap produce fondo transparente por defecto.
        // Usamos un DrawingVisual intermedio para pintar el fondo primero.
        var background = Card4Root.TryFindResource("MaterialDesign.Brush.CardBackground") as Brush
                         ?? new SolidColorBrush(Color.FromRgb(39, 43, 51));

        var visual = new DrawingVisual();
        using (var ctx = visual.RenderOpen())
        {
            ctx.DrawRectangle(background, null, new Rect(0, 0, w, h));
            ctx.DrawRectangle(new VisualBrush(Card4Root) { Stretch = Stretch.None },
                              null,
                              new Rect(0, 0, w, h));
        }

        var bmp = new RenderTargetBitmap(pw, ph, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bmp.Render(visual);

        // ── Nombre del archivo: {ItemId}_C{Cantidad} ─────────────────────────
        var itemId   = _vm.SelectedItem?.ItemId ?? "ITEM";
        var cantidad = _vm.QuantityToCraft;
        var fileName = $"{itemId}_C{cantidad}.png";

        var dialog = new SaveFileDialog
        {
            Title        = "Exportar Materiales como PNG",
            Filter       = "Imagen PNG|*.png",
            FileName     = fileName,
            DefaultExt   = ".png",
            AddExtension = true,
        };

        if (dialog.ShowDialog() != true) return;

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));

        using var stream = File.OpenWrite(dialog.FileName);
        encoder.Save(stream);
    }
}
