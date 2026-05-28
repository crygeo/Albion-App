using System.Collections.ObjectModel;
using System.Windows.Threading;
using Albion_App.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;
using Utilidades.Dialogs;
using ConfiguracionSvm = Albion_App._0Config.ConfiguracionSvm;

namespace Albion_App;

/// <summary>
/// ViewModel principal de la aplicación.
///
/// Coordina:
///   • Estado del sidebar colapsable.
///   • Navegación entre secciones.
/// </summary>
public sealed partial class MainVm : ObservableObject
{
    // ─── Reloj UTC ────────────────────────────────────────────────────────────

    [ObservableProperty] private string _utcTime = DateTime.UtcNow.ToString("HH:mm:ss");

    public SnackbarMessageQueue MessageQueue => DialogService.Instance.MensajeQueue;

    private void StartUtcClock()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => UtcTime = DateTime.UtcNow.ToString("HH:mm:ss");
        timer.Start();
    }

    // ─── Sidebar colapsable ───────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SidebarWidth))]
    private bool _isSidebarExpanded = true;

    public double SidebarWidth => IsSidebarExpanded ? 220 : 56;

    [RelayCommand]
    private void ToggleSidebar() => IsSidebarExpanded = !IsSidebarExpanded;

    // ─── Sección de configuración (fija) ─────────────────────────────────────

    [ObservableProperty] private ISectionIcons _configSection;

    [RelayCommand]
    private void SelectConfigSection() => NavigateTo(ConfigSection);

    // ─── Búsqueda de secciones ────────────────────────────────────────────────

    [ObservableProperty] private string _sectionSearch = string.Empty;

    // ─── Sección seleccionada ─────────────────────────────────────────────────

    public ISectionIcons? SectionSelected
    {
        get => _sectionIconsSelected;
        set
        {
            if (value is null) return;
            if (SetProperty(ref _sectionIconsSelected, value, nameof(SectionSelected)))
                NavigateTo(value);
        }
    }

    private ISectionIcons? _sectionIconsSelected;

    [ObservableProperty] private IReadOnlyList<ISectionIcons> _sections;

    [ObservableProperty] private ISectionIcons? _currentSection;

    // ─── Navegación ───────────────────────────────────────────────────────────

    private void NavigateTo(ISectionIcons? section)
    {
        if (!ReferenceEquals(_sectionIconsSelected, section))
            SetProperty(ref _sectionIconsSelected, section, nameof(SectionSelected));
        CurrentSection = section;
    }

    // ─── Constructor ──────────────────────────────────────────────────────────

    public MainVm(ConfiguracionSvm configSection, IEnumerable<ISectionIcons> sections)
    {
        _configSection = configSection;
        _sections      = new ObservableCollection<ISectionIcons>(sections);

        // Auto-navega al workspace (primera sección registrada).
        NavigateTo(_sections.FirstOrDefault());

        StartUtcClock();
    }
}
