using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Albion_App.Helpers;
using AlbionApp.Application.UseCases.SearchItems;
using AlbionApp.Domain.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Albion_App.Components.Item;

/// <summary>
/// Proyección UI sobre <see cref="ItemSearchHit"/>.
///
/// <para><b>Decisión arquitectónica</b>: este VM envuelve la proyección de
/// <i>búsqueda</i> (Application DTO), no la entidad de dominio. Esto cumple la
/// regla "un modelo, un propósito":</para>
/// <list type="bullet">
///   <item><c>ItemBase</c> (Domain) → entidad rica, fuente de verdad.</item>
///   <item><c>ItemSearchHit</c> (Application) → proyección minimal de búsqueda.</item>
///   <item><c>ItemBaseVm</c> (UI) → wrapper observable + carga de imagen.</item>
/// </list>
///
/// <para><b>Responsabilidades</b>:</para>
/// <list type="bullet">
///   <item>Resolver y mantener sincronizado <see cref="Components.Market.ItemBaseVm.DisplayName"/> con el idioma activo.</item>
///   <item>Cargar la imagen de forma asíncrona y publicarla en el hilo UI.</item>
///   <item>Implementar <see cref="IDisposable"/> para liberar la suscripción a
///         <see cref="ILocalizationService.LanguageChanged"/> y evitar leaks en
///         vistas con alta tasa de creación/descarte de VMs.</item>
/// </list>
///
/// <para><b>Thread-safety</b>: <c>BitmapSource</c> queda Frozen tras la decodificación —
/// inmutable y compartible entre hilos. <c>SetProperty</c> de CommunityToolkit
/// se invoca en el hilo UI para que los bindings reciban notificación correcta.</para>
/// </summary>
public sealed partial class ItemBaseVm : ObservableObject, IDisposable
{
    private readonly ItemSearchHit _hit;
    private readonly ILocalizationService _localization;

    // ── Propiedades observables ───────────────────────────────────────────────

    /// <summary>
    /// Nombre localizado del ítem en el idioma activo. Se actualiza automáticamente
    /// cuando el servicio de localización dispara <see cref="ILocalizationService.LanguageChanged"/>.
    /// </summary>
    [ObservableProperty] private string _displayName;

    /// <summary>
    /// Imagen del ítem. <c>null</c> mientras se carga o si la CDN no la sirve.
    /// </summary>
    [ObservableProperty] private BitmapSource? _image;

    /// <summary>
    /// Imagen del item sin procesar
    /// </summary>
    public byte[]? ImageBytes { get; private set; }

    // ── Constructor ───────────────────────────────────────────────────────────

    public ItemBaseVm(ItemSearchHit hit, ILocalizationService localization)
    {
        _hit = hit;
        _localization = localization;
        _displayName = ResolveDisplayName();

        _localization.LanguageChanged += OnLanguageChanged;
    }

    // ── Acceso al hit subyacente (lectura) ────────────────────────────────────

    public string ItemId => _hit.ItemId;
    public string BaseItemId => _hit.BaseItemId;
    public int? Tier => _hit.Tier;
    public int EnchantmentLevel => _hit.EnchantmentLevel;
    public int CategoryValue => _hit.CategoryValue;
    public string TierLabel => _hit.TierLabel;

    // ── Propiedades de presentación derivadas ─────────────────────────────────

    /// <summary>Etiqueta compacta para chips/tooltips: "T4.2 Espada Ancha".</summary>
    public string ShortLabel => string.IsNullOrEmpty(TierLabel)
        ? DisplayName
        : $"{TierLabel} {DisplayName}";

    // ── Carga de imagen ───────────────────────────────────────────────────────

    /// <summary>
    /// Descarga (o lee de caché) los bytes PNG y los decodifica a una
    /// <see cref="BitmapSource"/> frozen. Asigna <see cref="Components.Market.ItemBaseVm.Image"/> en el hilo UI.
    ///
    /// <para>Si <paramref name="ct"/> se cancela antes de completar, retorna
    /// silenciosamente sin modificar <see cref="Components.Market.ItemBaseVm.Image"/>.</para>
    /// </summary>
    public async Task LoadImageAsync(IAlbionImageService imageService, CancellationToken ct)
    {
        if (Image is not null)
            return;
        
        try
        {
            var bytes = await imageService.GetImageBytesAsync(ItemId, AlbionRenderType.Item, ct).ConfigureAwait(false);
            ImageBytes = bytes;
            
            if (bytes is not { Length: > 0 } || ct.IsCancellationRequested) return;

            var bitmap = bytes.DecodeToBitmapSource();

            await Application.Current.Dispatcher.InvokeAsync(() => Image = bitmap);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // Error inesperado: dejar Image=null (placeholder visible).
        }
    }

    // ── Reacción a cambio de idioma ───────────────────────────────────────────

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        DisplayName = ResolveDisplayName();
        OnPropertyChanged(nameof(ShortLabel));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string ResolveDisplayName()
        => _localization.GetText(_hit.NameLocalizationKey ?? _hit.ItemId);

    
    
    public void Dispose()
    {
        _localization.LanguageChanged -= OnLanguageChanged;
    }
}