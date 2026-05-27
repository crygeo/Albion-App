using System.Collections.ObjectModel;
using Albion_App.Features.DataStatic;
using Albion_App.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibEvents.Entities;
using LibEvents.Services;
using MaterialDesignThemes.Wpf;
using Utilidades.Dialogs;

namespace Albion_App._3Builds;

public partial class BuildsVm : ObservableObject, ISectionIcons
{
    private readonly BuildService _buildService;

    // ── ISectionIcons ─────────────────────────────────────────────────────────

    [ObservableProperty] private string       _header = "Builds";
    [ObservableProperty] private PackIconKind _icon   = PackIconKind.ShieldSword;

    // ── Estado ────────────────────────────────────────────────────────────────

    [ObservableProperty] private ObservableCollection<Build> _builds = [];
    [ObservableProperty] private Build?                      _selectedBuild;
    [ObservableProperty] private bool                        _isEditing;

    public BuildEditorVm Editor { get; }

    // ── Constructor ───────────────────────────────────────────────────────────

    public BuildsVm(BuildService buildService, BuildEditorVm editor)
    {
        _buildService = buildService;
        Editor        = editor;

        Editor.Saved     += OnEditorSaved;
        Editor.Cancelled += OnEditorCancelled;
    }

    // ── Carga inicial ─────────────────────────────────────────────────────────

    public async Task LoadAsync()
    {
        var list = await _buildService.GetBuildsAsync();
        Builds = new ObservableCollection<Build>(list);
    }

    // ── Comandos ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private void NewBuild()
    {
        SelectedBuild = null;
        Editor.LoadBuild(null);
        IsEditing = true;
    }

    [RelayCommand]
    private void EditBuild(Build build)
    {
        SelectedBuild = build;
        Editor.LoadBuild(build);
        IsEditing = true;
    }

    [RelayCommand]
    private async Task DeleteBuild(Build build)
    {
        var confirmDialog = new ConfirmDialog
        {
            TextHeader           = "Eliminar build",
            Message              = $"¿Eliminar \"{build.Name}\"?",
            AceptarCommand       = new AsyncRelayCommand(async () =>
            {
                await _buildService.DeleteBuildAsync(build.Id);
                Builds.Remove(build);
                if (ReferenceEquals(SelectedBuild, build))
                {
                    SelectedBuild = null;
                    IsEditing     = false;
                }
            }),
            DialogNameIdentifier = DialogDefaults.Confirm,
            DialogOpenIdentifier = DialogDefaults.Main,
        };
        await DialogService.Instance.MostrarDialogo(confirmDialog);
    }

    // ── Callbacks del editor ──────────────────────────────────────────────────

    private async void OnEditorSaved()
    {
        IsEditing     = false;
        SelectedBuild = null;
        await LoadAsync();
    }

    private void OnEditorCancelled()
    {
        IsEditing     = false;
        SelectedBuild = null;
    }
}
