using System.Collections.ObjectModel;
using Albion_App._1Calculadora;
using Albion_App.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibNavegation;
using LibNavegation.Interfaces;
using ConfiguracionSvm = Albion_App._0Config.ConfiguracionSvm;

namespace Albion_App;

/// <summary>
/// ViewModel principal de la aplicación.
///
/// Coordina:
///   • Estado del sidebar colapsable.
///   • Navegación entre secciones mediante <see cref="INavSections{TSection}"/>.
///   • Historial de navegación tipo browser (<see cref="INavHistory"/>).
///
/// Patrón de historial:
///   - <see cref="NavigateTo"/> es el único punto de entrada que actualiza
///     <see cref="CurrentSection"/>. Toda ruta de navegación pasa por él.
///   - Navegación orgánica (usuario elige sección) → pushHistory=true →
///     guarda entrada actual en _backStack y limpia _forwardStack.
///   - GoBack / GoForward → pushHistory=false → mutan las pilas sin crear
///     nuevas entradas. Antes de navegar hacia atrás se guarda la posición
///     actual en _forwardStack (y viceversa).
///
/// No contiene lógica de negocio — delega en <see cref="CalculadoraSvm"/>
/// y <see cref="ConfiguracionSvm"/>.
/// </summary>
public sealed partial class MainVm : ObservableObject, INavSections<ISectionIcons>, INavHistory
{
    // ─── Historial de navegación ──────────────────────────────────────────────

    private readonly Stack<ISectionIcons?> _backStack    = new();
    private readonly Stack<ISectionIcons?> _forwardStack = new();

    /// <inheritdoc/>
    public bool CanGoBack    => _backStack.Count    > 0;

    /// <inheritdoc/>
    public bool CanGoForward => _forwardStack.Count > 0;

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    public void GoBack()
    {
        var previous = _backStack.Pop();
        _forwardStack.Push(_sectionIconsSelected);   // guarda posición actual
        NavigateTo(previous, pushHistory: false);
    }

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    public void GoForward()
    {
        var next = _forwardStack.Pop();
        _backStack.Push(_sectionIconsSelected);       // guarda posición actual
        NavigateTo(next, pushHistory: false);
    }

    // ─── Sidebar colapsable ───────────────────────────────────────────────────

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(SidebarWidth))]
    private bool _isSidebarExpanded = true;

    /// <summary>
    /// Ancho en píxeles del sidebar. Binding directo como <c>double</c> sobre
    /// <c>Border.Width</c> — no requiere conversión a <see cref="System.Windows.GridLength"/>.
    /// </summary>
    public double SidebarWidth => IsSidebarExpanded ? 220 : 56;

    [RelayCommand]
    private void ToggleSidebar() => IsSidebarExpanded = !IsSidebarExpanded;

    // ─── Sección de configuración (fija, fuera de la lista) ──────────────────

    [ObservableProperty] private ISectionIcons _configSection;

    [RelayCommand]
    private void SelectConfigSection() => NavigateTo(ConfigSection, pushHistory: true);

    // ─── Búsqueda de secciones ────────────────────────────────────────────────

    [ObservableProperty] private string _sectionSearch = string.Empty;

    // ─── Sección seleccionada (ListBox nav) ───────────────────────────────────

    /// <summary>
    /// Ítem seleccionado en el ListBox del sidebar.
    /// El setter delega en <see cref="NavigateTo"/> para mantener el historial.
    /// </summary>
    public ISectionIcons? SectionSelected
    {
        get => _sectionIconsSelected;
        set
        {
            // WPF puede enviar null al deseleccionar; ignoramos esos casos para
            // que GoBack/GoForward puedan forzar null (Config abierta) sin
            // disparar una entrada de historial inesperada.
            if (value is null) return;
            if (SetProperty(ref _sectionIconsSelected, value, nameof(SectionSelected)))
                NavigateTo(value, pushHistory: true);
        }
    }

    private ISectionIcons? _sectionIconsSelected;

    [ObservableProperty] private IReadOnlyList<ISectionIcons> _sections;

    [ObservableProperty] private ISection? _currentSection;

    // ─── Navegación central ───────────────────────────────────────────────────

    /// <summary>
    /// Único punto de entrada que actualiza <see cref="CurrentSection"/>.
    /// </summary>
    /// <param name="section">Sección destino (puede ser null → ninguna).</param>
    /// <param name="pushHistory">
    ///   <c>true</c>: navegación orgánica → empuja historial atrás y limpia adelante.
    ///   <c>false</c>: navegación por historial → las pilas ya fueron mutadas por el llamador.
    /// </param>
    private void NavigateTo(ISectionIcons? section, bool pushHistory)
    {
        if (pushHistory)
        {
            _backStack.Push(_sectionIconsSelected);
            _forwardStack.Clear();
        }

        // Sincroniza la selección del ListBox sin re-entrar en SectionSelected.setter
        if (!ReferenceEquals(_sectionIconsSelected, section))
            SetProperty(ref _sectionIconsSelected, section, nameof(SectionSelected));

        CurrentSection = section;

        // Notifica CanExecute de los comandos de historial
        GoBackCommand.NotifyCanExecuteChanged();
        GoForwardCommand.NotifyCanExecuteChanged();
    }

    // ─── Constructor ──────────────────────────────────────────────────────────

    public MainVm(ConfiguracionSvm configSection, IEnumerable<ISectionIcons> sections)
    {
        _configSection = configSection;
        _sections      = new ObservableCollection<ISectionIcons>(sections);
    }
}
