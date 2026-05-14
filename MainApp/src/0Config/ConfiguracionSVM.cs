using Albion_App.Models;
using AlbionApp.Domain.Interfaces;
using AlbionApp.Domain.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;

namespace Albion_App._0Config;

/// <summary>
/// ViewModel de la sección de Configuración (sidebar inferior).
///
/// Responsabilidad: selección de idioma y recarga del catálogo cuando cambia.
/// Extraído de MainViewModel para aplicar SRP — la configuración no es responsabilidad
/// del coordinador principal.
/// </summary>
public sealed partial class ConfiguracionSvm : ObservableObject, ISectionIcons
{
    private readonly ILocalizationService  _languageService;

    // Navegacion
    [ObservableProperty]
    private string _header = "Configuracion";
    
    [ObservableProperty]
    private PackIconKind _icon = PackIconKind.AboutCircle;
    
    // ─── Idioma ───────────────────────────────────────────────────────────────

    [ObservableProperty]
    private SupportedLanguage _selectedLanguage ;

    public IReadOnlyList<SupportedLanguage> AvailableLanguages => _languageService.AvailableLanguages;

    // ─── Estado de carga ──────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotLoading))]
    private bool _isLoading;

    public bool IsNotLoading => !IsLoading;

    // ─── Constructor ──────────────────────────────────────────────────────────

    public ConfiguracionSvm(ILocalizationService languageService)
    {
        _languageService = languageService;
        _selectedLanguage = languageService.CurrentSupportedLanguage;
    }

    [RelayCommand]
    private Task SaveConfiguration()
    {
        ChangedLanguage();
        
        return Task.CompletedTask;
    }
    
    private Task ChangedLanguage()
    {
        _languageService.SetLanguage(SelectedLanguage);
        return Task.CompletedTask;
    }    


    
}
