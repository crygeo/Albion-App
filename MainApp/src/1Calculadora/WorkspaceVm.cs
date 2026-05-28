using System.Collections.ObjectModel;
using Albion_App.Components.Item;
using Albion_App.Infrastructure;
using Albion_App.Models;
using Albion_App.ViewModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;

namespace Albion_App._1Calculadora;

/// <summary>
/// ViewModel del workspace tipo navegador.
///
/// Responsabilidades:
///   • Mantener la colección de pestañas abiertas (<see cref="Tabs"/>).
///   • Crear pestañas nuevas (en blanco o restauradas desde disco).
///   • Cerrar pestañas sin dejar el workspace vacío.
///   • Serializar el estado al cerrar la app y restaurarlo al abrir.
///
/// Decisiones de diseño:
///   • Cada pestaña es un <see cref="CalculadoraSvm"/> independiente con su
///     propio estado — ninguna pestaña comparte estado con otra.
///   • La fábrica <see cref="_tabFactory"/> desacopla la creación de pestañas
///     del wiring de dependencias (que vive en App.xaml.cs).
///   • Nunca deja <see cref="Tabs"/> vacío: si se cierra la última pestaña,
///     se abre automáticamente una nueva en blanco.
/// </summary>
public sealed partial class WorkspaceVm : ObservableObject, ISectionIcons
{
    private readonly Func<CalculadoraSvm>  _tabFactory;
    private readonly IWorkspacePersistence _persistence;
    private readonly ItemBaseVmFactory     _itemVmFactory;

    private readonly ObservableCollection<CalculadoraSvm> _tabs = [];

    // ═══════════════════════════════════════════════════════════════════════════
    // NAVEGACIÓN (sidebar)
    // ═══════════════════════════════════════════════════════════════════════════

    [ObservableProperty] private string       _header = "Calculadora";
    [ObservableProperty] private PackIconKind _icon   = PackIconKind.Calculator;

    // ═══════════════════════════════════════════════════════════════════════════
    // ESTADO
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Pestañas abiertas. Solo lectura hacia la vista.</summary>
    public ReadOnlyObservableCollection<CalculadoraSvm> Tabs { get; }

    /// <summary>Pestaña actualmente visible.</summary>
    [ObservableProperty]
    private CalculadoraSvm? _selectedTab;

    // ═══════════════════════════════════════════════════════════════════════════
    // COMANDOS
    // ═══════════════════════════════════════════════════════════════════════════

    public IRelayCommand                  NewTabCommand   { get; }
    public IRelayCommand<CalculadoraSvm>  CloseTabCommand { get; }

    // ═══════════════════════════════════════════════════════════════════════════
    // CONSTRUCCIÓN
    // ═══════════════════════════════════════════════════════════════════════════

    public WorkspaceVm(
        Func<CalculadoraSvm> tabFactory,
        IWorkspacePersistence persistence,
        ItemBaseVmFactory itemVmFactory)
    {
        _tabFactory    = tabFactory;
        _persistence   = persistence;
        _itemVmFactory = itemVmFactory;

        Tabs = new ReadOnlyObservableCollection<CalculadoraSvm>(_tabs);

        NewTabCommand   = new RelayCommand(AddBlankTab);
        CloseTabCommand = new RelayCommand<CalculadoraSvm>(CloseTab);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // API PÚBLICA
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Restaura el workspace desde disco. Si no hay estado guardado, abre una
    /// pestaña en blanco. Llamado una vez desde App.xaml.cs al iniciar.
    /// </summary>
    public async Task InitializeAsync()
    {
        var state = await _persistence.LoadAsync();

        if (state is null || state.Tabs.Count == 0)
        {
            AddBlankTab();
            return;
        }

        foreach (var tabState in state.Tabs)
        {
            var tab = CreateTab();
            tab.QuantityToCraft = tabState.Quantity;
            tab.UseFocus        = tabState.UseFocus;

            if (tabState.ItemId is not null)
            {
                var itemVm = _itemVmFactory.CreateById(tabState.ItemId);
                if (itemVm is not null)
                    await tab.LoadItemExternalAsync(itemVm);
            }
        }

        var safeIndex = Math.Clamp(state.SelectedTabIndex, 0, _tabs.Count - 1);
        SelectedTab = _tabs[safeIndex];
    }

    /// <summary>
    /// Serializa el estado actual a disco. Llamado desde App.xaml.cs al cerrar.
    /// </summary>
    public async Task SaveWorkspaceAsync()
    {
        var state = new CalculatorWorkspaceState
        {
            SelectedTabIndex = SelectedTab is null ? 0
                               : Math.Max(0, _tabs.IndexOf(SelectedTab)),
            Tabs = _tabs.Select(t => new CalculatorTabState
            {
                ItemId   = t.SelectedItem?.ItemId,
                Quantity = t.QuantityToCraft,
                UseFocus = t.UseFocus,
                Title    = t.TabTitle,
            }).ToList(),
        };

        await _persistence.SaveAsync(state);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // LÓGICA PRIVADA
    // ═══════════════════════════════════════════════════════════════════════════

    private void AddBlankTab()
    {
        var tab = CreateTab();
        SelectedTab = tab;
    }

    private CalculadoraSvm CreateTab()
    {
        var tab = _tabFactory();
        _tabs.Add(tab);
        return tab;
    }

    private void CloseTab(CalculadoraSvm? tab)
    {
        if (tab is null) return;

        var idx = _tabs.IndexOf(tab);
        _tabs.Remove(tab);

        if (_tabs.Count == 0)
        {
            // Nunca dejar el workspace vacío
            AddBlankTab();
            return;
        }

        if (SelectedTab == tab)
            SelectedTab = _tabs[Math.Clamp(idx, 0, _tabs.Count - 1)];
    }
}
