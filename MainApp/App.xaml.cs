using System.Windows;
using Albion_App._2Player;
using Albion_App._3Builds;
using Albion_App._4Groups;
using Albion_App._5Events;
using Albion_App.Components.Achievement;
using Albion_App.Components.Destinyboard;
using Albion_App.Components.Item;
using Albion_App.Components.Market;
using Albion_App.Infrastructure;
using Albion_App.Infrastructure.Handlers;
using AlbionApp.Application.UseCases.Player;
using AlbionApp.Application.UseCases.SearchItems;
using AlbionApp.Domain.Localization;
using AlbionApp.Application.UseCases.Crafting;
using AlbionApp.Infrastructure.Persistence;
using AlbionApp.Infrastructure.Services;
using LibAlbionData;
using LibAlbionDebug;
using LibAlbionProtocol.Parsing;
using LibAlbionRouting.Handlers;
using Albion_App._0Config;
using LibEvents.Discord;
using LibEvents.Services;
using Utilidades.Dialogs;
using LibNetWork.Interfaces;
using LibNetWork.Models;
using LibNetWork.Networking;
using LibNetWork.PortResolution;
using LibServiceLifecycle;
using LibServices.AppConfig;
using LibServices.PlayerState;
using CalculadoraSvm = Albion_App._1Calculadora.CalculadoraSvm;
using WorkspaceVm    = Albion_App._1Calculadora.WorkspaceVm;
using ConfiguracionSvm = Albion_App._0Config.ConfiguracionSvm;
using ItemSearchVM = Albion_App.Dialog.ItemSearchVM;

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
///        LocalizationService → ItemDataService → CategoryDataService → AchievementDataService
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

        // ── Splash: lo primero visible, cubre todo el startup ─────────────────
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var splashVm = new SplashVm();
        var splash   = new SplashWindow { DataContext = splashVm };
        splash.Show();

        var config      = new AppConfigService();
        var playerState = new PlayerStateService();

        // ── Infraestructura raíz ──────────────────────────────────────────────
        var albionData = new AlbionData();

        try
        {
            await albionData.StartAsync();
        }
        catch (Exception ex)
        {
            splash.Close();
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
        var localizationService     = new LocalizationService(albionData);
        var itemDataService         = new ItemDataService(albionData);
        var categoryDataService     = new CategoryDataService(albionData);
        var achievementService      = new AchievementDataService(albionData);
        var craftingLocationService = new CraftingLocationService(albionData, localizationService);

        // PreloadService arranca en secuencia: Localization → Items → Categories → Achievements → CraftingLocations.
        IService[] dataServices =
            [localizationService, itemDataService, categoryDataService, achievementService, craftingLocationService];

        var preloader = new PreloadService(dataServices);
        preloader.ProgressChanged += (_, _) => splashVm.UpdateProgress(preloader.Progress ?? 0);

        await preloader.StartAsync();

        var imageService = new AlbionImageService();

        // ── Caso de uso de búsqueda + fábrica de VMs ──────────────────────────
        // El caso de uso encapsula parsing, índice, filtros y orden — testeable y
        // reutilizable. La fábrica centraliza la creación de ItemBaseVm para que
        // ningún VM tenga que conocer la combinación exacta de servicios.
        var searchItemsUseCase = new SearchItemsUseCase(itemDataService, localizationService);
        var processPlayerUseCase = new ProcessPlayerUseCase(achievementService);
        var itemVmFactory = new ItemBaseVmFactory(itemDataService, localizationService, imageService);
        var archievemenVmFactory = new AchievementVmFactory(localizationService, processPlayerUseCase, imageService);
        var clipboardService = new ClipboardService();

        var marketVm = new MarketVm(searchItemsUseCase, localizationService, categoryDataService, itemVmFactory, clipboardService);
        var itemSearchVm = new ItemSearchVM(marketVm);
        var itemSearchService = new ItemSearchDialogService(itemSearchVm);
        var calculateCraftingUseCase = new CalculateCraftingCostUseCase();
        var destinyBoard = new DestinyBoardVm(processPlayerUseCase, archievemenVmFactory, playerState);

        // ── Builds / Groups / Events (persistencia SQLite) ────────────────────
        var eventsDbFactory = EventsDbContextFactory.Create();
        var buildService    = new BuildService(eventsDbFactory);

        var buildEditorVm   = new BuildEditorVm(buildService, itemSearchService, itemDataService, searchItemsUseCase, itemVmFactory);
        var buildsVm        = new BuildsVm(buildService, buildEditorVm);
        await buildsVm.LoadAsync();

        var groupEditorVm   = new GroupEditorVm(buildService);
        var groupsVm        = new GroupsVm(buildService, groupEditorVm);
        await groupsVm.LoadAsync();

        var discordBot      = new DiscordBotService(eventsDbFactory);
        discordBot.ErrorOccurred += msg => DialogService.Instance.MensajeQueue.Enqueue(msg);
        var discordConfigVm = new DiscordConfigVm(discordBot, config);

        var eventEditorVm   = new EventEditorVm(buildService);
        var eventHistoryVm  = new EventHistoryVm(buildService);
        var eventsVm        = new EventsVm(buildService, eventEditorVm, discordBot, config, eventHistoryVm);
        await eventsVm.LoadAsync();

        // Reconectar Discord automáticamente si hay token guardado
        if (!string.IsNullOrWhiteSpace(config.DiscordBotToken))
        {
            if (Enum.TryParse<DiscordInteractionMode>(config.DiscordInteractionMode, out var mode))
                discordBot.InteractionMode = mode;
            _ = discordBot.StartAsync(config.DiscordBotToken);
        }

        // ── ViewModels de sección ─────────────────────────────────────────────
        var configuracion = new ConfiguracionSvm(localizationService, config, discordBot, discordConfigVm);
        var player        = new PlayerVm(destinyBoard, playerState);

        // Fábrica de pestañas: cada llamada crea un CalculadoraSvm independiente.
        CalculadoraSvm TabFactory() => new(
            itemSearchService,
            itemDataService,
            processPlayerUseCase,
            localizationService,
            craftingLocationService,
            calculateCraftingUseCase);

        var workspace = new WorkspaceVm(
            TabFactory,
            new WorkspacePersistenceService(),
            itemVmFactory);

        await workspace.InitializeAsync();

        // ── ViewModel principal ───────────────────────────────────────────────
        var mainVm = new MainVm(configuracion, [player, workspace, buildsVm, groupsVm, eventsVm]);

        // ── Stack de red + debug packet logger ───────────────────────────────
        var albionParser = new AlbionParser();
        var eventHandler = new GenericEventHandler();
        var requestHandler = new GenericRequestHandler();
        var responseHandler = new GenericResponseHandler();

        new EventHandlerRegistry()
            .Add(new AchievementHandler(processPlayerUseCase, achievementService, playerState))
            .RegisterAll(eventHandler);

        new ResponseHandlerRegistry()
            .Add(new JoinHandler(player))
            .RegisterAll(responseHandler);

        new RequestHandlerRegistry()
            .RegisterAll(requestHandler);

        // Registrar handlers en el parser (debug loggers AL FINAL = llamados PRIMERO
        // por HandlersCollection, que invierte el orden del array).
        albionParser.AddHandler(eventHandler);
        albionParser.AddHandler(requestHandler);
        albionParser.AddHandler(responseHandler);

        PacketProvider packetProvider = new SharpPcapPacketProvider(
            albionParser,
            new AlbionServerFilter());

        // OPCIÓN B — Raw sockets (legacy, requiere admin):
        // var portResolver = new AlbionPortResolver(
        //     @"C:\Program Files (x86)\Albion Online\game\Albion-Online.exe");
        // PacketProvider packetProvider = new SocketsPacketProvider(
        //     albionParser, new ResolvedPortFilter(portResolver));

        var networkManager = new NetworkManager(packetProvider);

        // Arrancar captura en background — no bloquear el UI thread.
        // Si falla (sin permisos), el logger ya habrá escrito el header del archivo.
        Task.Run(() =>
        {
            try
            {
                networkManager.Start();
            }
            catch (Exception ex)
            {
                // Sin admin o Albion no instalado en la ruta por defecto.
                System.Diagnostics.Debug.WriteLine($"[NetworkManager] Start falló: {ex.Message}");
            }
        });

        // ── Ventana ───────────────────────────────────────────────────────────
        var window = new MainWindow { DataContext = mainVm };
        window.Show();

        // El splash cierra DESPUÉS de que MainWindow ya es visible — sin hueco en blanco.
        splash.Close();
        ShutdownMode = ShutdownMode.OnLastWindowClose;

        // Guardar workspace y liberar recursos al cerrar la app.
        window.Closed += async (_, _) =>
        {
            await workspace.SaveWorkspaceAsync();
            networkManager.Stop();
            await discordBot.DisposeAsync();
        };
    }

    
}
