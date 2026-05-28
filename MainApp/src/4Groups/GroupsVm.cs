using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using Albion_App._4Groups.Export;
using Albion_App.Features.DataStatic;
using Albion_App.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibEvents.Entities;
using LibEvents.Services;
using MaterialDesignThemes.Wpf;
using Utilidades.Dialogs;

namespace Albion_App._4Groups;

public partial class GroupsVm : ObservableObject, ISectionIcons
{
    private readonly BuildService            _buildService;
    private readonly BuildGroupImageExporter _exporter;

    // ── ISectionIcons ─────────────────────────────────────────────────────────

    [ObservableProperty] private string       _header = "Grupos";
    [ObservableProperty] private PackIconKind _icon   = PackIconKind.AccountGroup;

    // ── Estado ────────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private ObservableCollection<BuildGroup> _groups = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private BuildGroup? _selectedGroup;

    public bool HasSelection => SelectedGroup is not null;

    public GroupEditorVm Editor { get; }

    // ── Constructor ───────────────────────────────────────────────────────────

    public GroupsVm(BuildService buildService, GroupEditorVm editor, BuildGroupImageExporter exporter)
    {
        _buildService = buildService;
        _exporter     = exporter;
        Editor        = editor;

        Editor.Saved += OnEditorSaved;
        // Cancelled lo escucha el propio diálogo para cerrarse.
    }

    // ── Carga ─────────────────────────────────────────────────────────────────

    public async Task LoadAsync()
    {
        var list = await _buildService.GetGroupsAsync();
        Groups = new ObservableCollection<BuildGroup>(list);
    }

    // ── Comandos ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task NewGroup()
    {
        await Editor.LoadGroupAsync(null);
        await DialogService.Instance.MostrarDialogo<GroupEditorDialogV>(
            Editor,
            "Nuevo grupo",
            DialogDefaults.Main,
            DialogDefaults.GroupEditor);
    }

    [RelayCommand]
    private async Task EditGroup(BuildGroup group)
    {
        await Editor.LoadGroupAsync(group);
        await DialogService.Instance.MostrarDialogo<GroupEditorDialogV>(
            Editor,
            "Editar grupo",
            DialogDefaults.Main,
            DialogDefaults.GroupEditor);
    }

    [RelayCommand]
    private async Task DeleteGroup(BuildGroup group)
    {
        var confirmDialog = new ConfirmDialog
        {
            TextHeader           = "Eliminar grupo",
            Message              = $"¿Eliminar \"{group.Name}\"?",
            AceptarCommand       = new AsyncRelayCommand(async () =>
            {
                await _buildService.DeleteGroupAsync(group.Id);
                Groups.Remove(group);
                DialogService.Instance.MensajeQueue.Enqueue($"🗑️ Grupo \"{group.Name}\" eliminado.");
                if (ReferenceEquals(SelectedGroup, group))
                    SelectedGroup = null;
            }),
            DialogNameIdentifier = DialogDefaults.Confirm,
            DialogOpenIdentifier = DialogDefaults.Main,
        };
        await DialogService.Instance.MostrarDialogo(confirmDialog);
    }

    [RelayCommand]
    private async Task ExportImage()
    {
        if (SelectedGroup is null) return;

        BitmapSource bitmap;
        try
        {
            bitmap = await _exporter.RenderAsync(SelectedGroup);
        }
        catch (Exception ex)
        {
            DialogService.Instance.MensajeQueue.Enqueue($"Error al generar imagen: {ex.Message}");
            return;
        }

        var vm = new ExportDialogVm(bitmap, SelectedGroup.Name);
        await DialogService.Instance.MostrarDialogo<ExportDialogV>(
            vm,
            "Exportar imagen",
            DialogDefaults.Main,
            DialogDefaults.Export);
    }

    // ── Callback del editor ───────────────────────────────────────────────────

    private async void OnEditorSaved()
    {
        await LoadAsync();
        SelectedGroup = null;
    }
}
