using Albion_App.Components.Market;
using Albion_App.View.Dialog;
using AlbionApp.Domain.Interfaces;
using AlbionApp.Domain.ItemSearch;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Albion_App.ViewModel.Dialog;

/// <summary>
/// ViewModel del diálogo de búsqueda de ítems.
///
/// Responsabilidad única: envolver <see cref="MarketVm"/> con el contrato de diálogo
/// (Confirm / Cancel). Toda la lógica de filtrado y selección vive en <see cref="Market"/>.
///
/// El code-behind de <see cref="ItemSearchDialogV"/> escucha <see cref="ConfirmRequested"/>
/// y <see cref="CancelRequested"/> para cerrar el DialogHost y propagar el resultado.
/// </summary>
public sealed partial class ItemSearchVM : ObservableObject
{
    // ─── Mercado (lógica de filtrado y selección) ─────────────────────────────

    /// <summary>ViewModel del mercado: gestiona texto, categoría, nivel, encantamiento y selección.</summary>
    public MarketVm Market { get; }

    // ─── Eventos de resultado ─────────────────────────────────────────────────

    /// <summary>
    /// El diálogo escucha este evento para invocar su AceptarCommand y cerrarse.
    /// Se pasa el <see cref="ItemBaseVm"/> completo (con imagen ya cargada) en lugar
    /// del <see cref="ItemBase"/> del dominio para evitar re-descargar la imagen en el
    /// VM llamador.
    /// </summary>
    public event EventHandler<ItemBaseVm>? ConfirmRequested;

    /// <summary>El diálogo escucha este evento para cerrarse sin resultado.</summary>
    public event EventHandler? CancelRequested;

    // ─── Fábrica de ítems ─────────────────────────────────────────────────────

    /// <summary>
    /// Construye un <see cref="ItemBaseVm"/> para el <paramref name="itemId"/> dado,
    /// incluyendo carga de imagen en fire-and-forget.
    ///
    /// Centraliza la creación de VMs de ítem para que ningún VM externo necesite
    /// inyectar <c>ILocalizationService</c> ni <c>IItemImageService</c> directamente.
    /// Delega en <see cref="MarketVm.BuildItemVm"/>, que ya posee todos los servicios.
    /// </summary>
    public ItemBaseVm BuildItemVm(string itemId, CancellationToken ct = default)
        => Market.BuildItemVm(itemId, ct);

    // ─── Comandos ─────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm() => ConfirmRequested?.Invoke(this, Market.SelectedItem!);

    private bool CanConfirm() => Market.SelectedItem is not null;

    [RelayCommand]
    private void Cancel() => CancelRequested?.Invoke(this, EventArgs.Empty);

    // ─── Constructor ──────────────────────────────────────────────────────────

    public ItemSearchVM(MarketVm marketVm)
    {
        Market = marketVm;

        // ConfirmCommand.CanExecute depende de Market.SelectedItem — invalidar cuando cambie.
        // Se usa PropertyChanged en lugar de exponer un campo privado para mantener el desacoplamiento.
        Market.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MarketVm.SelectedItem) or nameof(MarketVm.HasSelectedItem))
                ConfirmCommand.NotifyCanExecuteChanged();
        };
    }
}
