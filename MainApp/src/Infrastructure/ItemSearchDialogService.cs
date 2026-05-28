using Albion_App.Dialog;
using Albion_App.Features.DataStatic;
using AlbionApp.Domain.ItemSearch;
using CommunityToolkit.Mvvm.Input;
using Utilidades.Dialogs;
using ItemBaseVm = Albion_App.Components.Item.ItemBaseVm;
using ItemSearchVM = Albion_App.Dialog.ItemSearchVM;

namespace Albion_App.Infrastructure;

/// <summary>
/// Implementación de <see cref="IItemSearchService"/> que utiliza
/// <see cref="ItemSearchDialogV"/> y <see cref="DialogService"/>.
///
/// Es la única clase de la aplicación que instancia <c>ItemSearchDialogV</c>.
/// El ViewModel nunca toca Views directamente.
/// </summary>
public sealed class ItemSearchDialogService : IItemSearchService
{
    private readonly ItemSearchVM _itemSearchVm;

    public ItemSearchDialogService(ItemSearchVM itemSearchVm)
        => _itemSearchVm = itemSearchVm;

    public async Task<ItemBaseVm?> SearchAsync(
        SlotType? slotTypeFilter     = null,
        int?      defaultTier        = null,
        int?      defaultEnchantment = null)
    {
        // Aplicar filtro de slot ANTES de mostrar el diálogo.
        _itemSearchVm.Market.SetSlotTypeFilter(slotTypeFilter);

        // Aplicar nivel/encantamiento por defecto y guardar los valores previos
        // para restaurarlos al cerrar (no afectar otros usos del diálogo).
        var (prevLevel, prevEnchant) =
            _itemSearchVm.Market.ApplyDefaultFilters(defaultTier, defaultEnchantment);

        ItemBaseVm? selected = null;

        var dialog = new ItemSearchDialogV(_itemSearchVm)
        {
            DialogOpenIdentifier = DialogDefaults.Main,
            AceptarCommand = new AsyncRelayCommand<ItemBaseVm>(item =>
            {
                selected = item;
                return Task.CompletedTask;
            }),
            CancelarCommand = new AsyncRelayCommand(() => Task.CompletedTask)
        };

        await DialogService.Instance.MostrarDialogo(dialog);

        // Limpiar filtro de slot y restaurar nivel/encantamiento al cerrar.
        _itemSearchVm.Market.SetSlotTypeFilter(null);
        _itemSearchVm.Market.RestoreDefaultFilters(prevLevel, prevEnchant);

        return selected;
    }

    /// <inheritdoc/>
    public ItemBaseVm BuildItemVm(string itemId, CancellationToken ct)
        => _itemSearchVm.BuildItemVm(itemId, ct);
}
