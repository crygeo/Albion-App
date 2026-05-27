using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibEvents.Entities;
using LibEvents.Services;
using Utilidades.Dialogs;

namespace Albion_App._5Events;

/// <summary>
/// Formulario de creación/edición de una plantilla de evento.
/// Solo gestiona: nombre, descripción y grupo de composición.
/// La fecha/hora se configura al activar el evento, no aquí.
/// </summary>
public partial class EventEditorVm : ObservableObject
{
    private readonly BuildService _buildService;
    private GuildEvent? _editingEvent;

    [ObservableProperty] private string      _name        = "";
    [ObservableProperty] private string?     _description;
    [ObservableProperty] private BuildGroup? _selectedGroup;

    public ObservableCollection<BuildGroup> AvailableGroups { get; } = [];

    public event Action? Saved;
    public event Action? Cancelled;

    public EventEditorVm(BuildService buildService)
        => _buildService = buildService;

    public async Task LoadAsync(GuildEvent? ev)
    {
        _editingEvent  = ev;
        Name           = ev?.Name        ?? "";
        Description    = ev?.Description;

        var groups = await _buildService.GetGroupsAsync();
        AvailableGroups.Clear();
        foreach (var g in groups)
            AvailableGroups.Add(g);

        SelectedGroup = ev?.BuildGroupId is int gid
            ? AvailableGroups.FirstOrDefault(g => g.Id == gid)
            : null;
    }

    [RelayCommand]
    private async Task Save()
    {
        if (string.IsNullOrWhiteSpace(Name)) return;

        var ev = _editingEvent ?? new GuildEvent { Status = EventStatus.Draft };

        ev.Name         = Name.Trim();
        ev.Description  = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();
        ev.BuildGroupId = SelectedGroup?.Id;

        if (_editingEvent is null)
            await _buildService.CreateEventAsync(ev);
        else
            await _buildService.UpdateEventAsync(ev);

        DialogService.Instance.MensajeQueue.Enqueue($"✅ Evento \"{ev.Name}\" guardado.");
        Saved?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke();
}
