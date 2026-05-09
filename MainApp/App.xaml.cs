using System.Windows;
using Albion_App.Components.Market;
using Albion_App.ViewModel.Dialog;
using Albion_App.ViewModel.Seccion;
using AlbionApp.Application.UseCases.SearchItems;
using AlbionApp.Infrastructure.Services;
using LibAlbionData;
using LibServices;

namespace Albion_App;

/// <summary>
/// Raíz de composición de la aplicación.
///
/// Responsabilidad: construir el grafo completo de dependencias y arrancar la ventana
/// principal. No contiene lógica de negocio.
///
/// Orden de arranque:
///   1. AlbionData.StartAsync()     — valida la ruta del juego y carga los archivos en caché.
///   2. PreloadService.StartAsync() — arranca los servicios de datos en orden:
///        LocalizationService → ItemDataService → CategoryDataService
///      (Secuencial: las categorías necesitan que la localización esté lista para nombrar el árbol.)
///      Background — la ventana se muestra de inmediato.
///   3. MainWindow.Show()           — UI visible; los datos llegan mientras el usuario
///                                    explora la interfaz inicial.
///
/// Adaptadores de compatibilidad:
///   Los VMs heredados consumen interfaces antiguas (ILanguageSelector, IItemCatalog,
///   IShopCategoryTreeProvider). Los adapters puentean hacia los nuevos servicios.
///   TODO: eliminar cada adapter cuando el VM correspondiente migre a los nuevos contratos.
/// </summary>
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // ── Infraestructura raíz ──────────────────────────────────────────────
        var albionData = new AlbionData();

        try
        {
            await albionData.StartAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"No se pudo localizar Albion Online:\n\n{ex.Message}\n\n" +
                "Asegúrate de que el juego está instalado o configura la ruta manualmente.",
                "Error de inicio",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
            return;
        }

        // ── Servicios de datos ────────────────────────────────────────────────
        var localizationService = new LocalizationService(albionData);
        var itemDataService     = new ItemDataService(albionData);
        var categoryDataService = new CategoryDataService(albionData);

        // PreloadService arranca en secuencia: Localization → Items → Categories.
        var preloader = new PreloadService([localizationService, itemDataService, categoryDataService]);
        await preloader.StartAsync();


        var imageService = new ItemImageService();

        // ── Caso de uso de búsqueda + fábrica de VMs ──────────────────────────
        // El caso de uso encapsula parsing, índice, filtros y orden — testeable y
        // reutilizable. La fábrica centraliza la creación de ItemBaseVm para que
        // ningún VM tenga que conocer la combinación exacta de servicios.
        var searchItemsUseCase = new SearchItemsUseCase(itemDataService, localizationService);
        var itemVmFactory      = new ItemBaseVmFactory(itemDataService, localizationService, imageService);

        var marketVm   = new MarketVm(searchItemsUseCase, localizationService, categoryDataService, itemVmFactory);
        var itemSearch = new ItemSearchVM(marketVm);

        // ── ViewModels de sección ─────────────────────────────────────────────
        var configuracion = new ConfiguracionSvm(localizationService);
        var calculadora   = new CalculadoraSvm(itemSearch, itemDataService);

        // ── ViewModel principal ───────────────────────────────────────────────
        var mainVm = new MainVm(configuracion, [calculadora]);

        // ── Ventana ───────────────────────────────────────────────────────────
        var window = new MainWindow { DataContext = mainVm };
        window.Show();
    }
}
