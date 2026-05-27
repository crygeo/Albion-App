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

    // Guards para OnSelectedBuildChanged
    private Build? _previousBuild;
    private bool   _isReverting;
    private bool   _suppressDirtyCheck;

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

    partial void OnSelectedBuildChanged(Build? value)
    {
        if (_isReverting || _suppressDirtyCheck || value is null) return;

        if (IsEditing && Editor.IsDirty)
        {
            var requested = value;
            _isReverting  = true;
            SelectedBuild = _previousBuild;   // revert visual selection
            _isReverting  = false;
            _ = ConfirmAndSwitch(requested);
            return;
        }

        _previousBuild = value;
        Editor.LoadBuild(value);
        IsEditing = true;
    }

    [RelayCommand]
    private async Task NewBuild()
    {
        if (!await ConfirmDiscardIfDirty()) return;
        _suppressDirtyCheck = true;
        SelectedBuild       = null;
        _suppressDirtyCheck = false;
        _previousBuild      = null;
        Editor.LoadBuild(null);
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

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task ConfirmAndSwitch(Build target)
    {
        if (!await ConfirmDiscardIfDirty()) return;

        _suppressDirtyCheck = true;
        SelectedBuild       = target;
        _suppressDirtyCheck = false;
        _previousBuild      = target;
        Editor.LoadBuild(target);
        IsEditing = true;
    }

    private async Task<bool> ConfirmDiscardIfDirty()
    {
        if (!IsEditing || !Editor.IsDirty) return true;

        var confirmed = false;
        var dialog = new ConfirmDialog
        {
            TextHeader           = "Cambios sin guardar",
            Message              = "Tienes cambios sin guardar. ¿Descartar y continuar?",
            AceptarCommand       = new AsyncRelayCommand(async () => { confirmed = true; }),
            DialogNameIdentifier = DialogDefaults.Confirm,
            DialogOpenIdentifier = DialogDefaults.Main,
        };
        await DialogService.Instance.MostrarDialogo(dialog);
        return confirmed;
    }
}
