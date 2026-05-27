using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibEvents.Entities;
using LibEvents.Services;
using Utilidades.Dialogs;

namespace Albion_App._4Groups;

public partial class GroupEditorVm : ObservableObject
{
    private readonly BuildService _buildService;
    private BuildGroup? _editingGroup;

    [ObservableProperty] private string  _name      = "";
    [ObservableProperty] private string? _saveError;

    public ObservableCollection<GroupSlotVm> Slots { get; } = [];

    private IReadOnlyList<Build> _availableBuilds = [];

    public event Action? Saved;
    public event Action? Cancelled;

    public GroupEditorVm(BuildService buildService)
        => _buildService = buildService;

    // ── Carga ─────────────────────────────────────────────────────────────────

    public async Task LoadGroupAsync(BuildGroup? group)
    {
        _editingGroup = group;
        Name          = group?.Name ?? "";
        SaveError     = null;

        foreach (var slot in Slots) UnsubscribeSlot(slot);
        Slots.Clear();

        _availableBuilds = await _buildService.GetBuildsAsync();

        if (group is not null)
        {
            foreach (var slot in group.Slots.OrderBy(s => s.SortOrder))
            {
                var build  = _availableBuilds.FirstOrDefault(b => b.Id == slot.BuildId);
                var slotVm = new GroupSlotVm(_availableBuilds, build, slot.Quantity, slot.Emoji ?? "");
                SubscribeSlot(slotVm);
                Slots.Add(slotVm);
            }
        }

        RefreshAvailableBuilds();
    }

    // ── Comandos ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private void AddSlot()
    {
        var taken          = Slots.Select(s => s.Build).ToHashSet();
        var firstAvailable = _availableBuilds.FirstOrDefault(b => !taken.Contains(b));
        var slot = new GroupSlotVm(_availableBuilds, firstAvailable, 1, "");
        SubscribeSlot(slot);
        Slots.Add(slot);
        RefreshAvailableBuilds();
    }

    [RelayCommand]
    private void RemoveSlot(GroupSlotVm slot)
    {
        UnsubscribeSlot(slot);
        Slots.Remove(slot);
        RefreshAvailableBuilds();
    }

    [RelayCommand]
    private async Task Save()
    {
        if (string.IsNullOrWhiteSpace(Name)) return;
        SaveError = null;

        // Validar que no haya emojis duplicados
        var usedEmojis = Slots
            .Where(s => s.Build is not null && !string.IsNullOrWhiteSpace(s.Emoji))
            .Select(s => s.Emoji.Trim())
            .ToList();

        if (usedEmojis.Count != usedEmojis.Distinct(StringComparer.Ordinal).Count())
        {
            SaveError = "Cada slot debe tener un emoji único dentro del grupo.";
            return;
        }

        var group  = _editingGroup ?? new BuildGroup();
        group.Name = Name.Trim();

        var slots = Slots
            .Where(s => s.Build is not null)
            .Select((s, i) => new BuildGroupSlot
            {
                BuildId   = s.Build!.Id,
                Quantity  = s.Quantity,
                SortOrder = i,
                Emoji     = string.IsNullOrWhiteSpace(s.Emoji) ? null : s.Emoji.Trim(),
            })
            .ToList();

        await _buildService.SaveGroupWithSlotsAsync(group, slots);
        DialogService.Instance.MensajeQueue.Enqueue($"✅ Grupo \"{group.Name}\" guardado.");
        Saved?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke();

    // ── Available-builds dinámicos ────────────────────────────────────────────

    private void SubscribeSlot(GroupSlotVm slot)
        => slot.PropertyChanged += OnSlotPropertyChanged;

    private void UnsubscribeSlot(GroupSlotVm slot)
        => slot.PropertyChanged -= OnSlotPropertyChanged;

    private void OnSlotPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GroupSlotVm.Build))
            RefreshAvailableBuilds();
    }

    private void RefreshAvailableBuilds()
    {
        var taken = Slots.Select(s => s.Build).ToList();
        foreach (var slot in Slots)
            slot.UpdateAvailableBuilds(taken);
    }
}
