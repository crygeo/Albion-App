# Event Damage Report Dialog Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a "Ver Daño" button to finished events (`EventStatus.Finished`) that opens a dialog showing every combat snapshot saved during the event, with a master-detail layout: a list of rounds on the left and damage/heal bars (visually identical to the live DPS meter) for the selected round on the right.

**Architecture:** A new singleton `EventDamageReportVm` (constructed in `App.xaml.cs`, injected into `EventsVm` exactly like `EventHistoryVm`/`DpsMeterVm`) loads snapshots via the existing `BuildService.GetSnapshotsAsync(eventId)` and exposes them as lightweight, immutable display wrappers (`SnapshotSummaryVm`, `SnapshotEntryDisplayVm`). A new `EventDamageReportV` UserControl renders the master-detail layout, reusing the exact bar `DataTemplate` from `DpsMeterV.xaml`. A new `EventDamageReportDialogV` wraps it in the same `md:Card` header/body/footer pattern as `DpsMeterDialogV`.

**Tech Stack:** WPF (.NET 9), CommunityToolkit.Mvvm (`ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`), MaterialDesignThemes, existing `LibEvents` entities/services (no DB/entity changes).

**Reference spec:** `docs/superpowers/specs/2026-06-08-event-damage-report-design.md`

**No automated test infra exists for WPF VMs/Views in this project** (the `*.Tests` projects cover domain/application layers only — see `AlbionApp.Application.Tests`). Verification for each task is `dotnet build` plus, for the final task, manual verification by running the app. This mirrors how the existing `5Events` VMs (`DpsMeterVm`, `EventHistoryVm`) were built without unit tests.

---

### Task 1: Add `DialogDefaults.DamageReport` constant

**Files:**
- Modify: `MainApp/src/Models/DialogDefaults.cs:18-19`

- [ ] **Step 1: Add the new constant**

In `MainApp/src/Models/DialogDefaults.cs`, change:

```csharp
    public const string DpsMeter       = "dialogDpsMeter";
    public const string PriceSelection = "dialogPriceSelection";
```

to:

```csharp
    public const string DpsMeter       = "dialogDpsMeter";
    public const string DamageReport   = "dialogDamageReport";
    public const string PriceSelection = "dialogPriceSelection";
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build "MainApp/MainApp.csproj"`
Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add MainApp/src/Models/DialogDefaults.cs
git commit -m "Add DialogDefaults.DamageReport identifier for the damage report dialog"
```

---

### Task 2: Create `SnapshotSummaryVm`

**Files:**
- Create: `MainApp/src/5Events/SnapshotSummaryVm.cs`

This is an immutable wrapper around `EventCombatSnapshot` (defined in `LibEvents/Entities/EventCombatSnapshot.cs` — has `Id`, `EventId`, `StartedAt`, `TakenAt`, `Entries`) used as the `ListBox` row in the new dialog. `StartedAt`/`TakenAt` are stored as UTC (`DateTime.UtcNow`, see `LibSolutions/LibAlbionGame/Combat/CombatSession.cs:27,51`), so we convert to local time for display.

- [ ] **Step 1: Create the file**

```csharp
using LibEvents.Entities;

namespace Albion_App._5Events;

/// <summary>
/// Wrapper inmutable de <see cref="EventCombatSnapshot"/> para una fila de la
/// lista de rondas en <see cref="EventDamageReportV"/>.
/// </summary>
public sealed class SnapshotSummaryVm(EventCombatSnapshot snapshot, int roundNumber)
{
    public EventCombatSnapshot Snapshot    { get; } = snapshot;
    public int                 RoundNumber { get; } = roundNumber;

    public string Title    => $"Ronda {RoundNumber}";
    public string Subtitle => $"{Snapshot.StartedAt.ToLocalTime():HH:mm} · {Snapshot.TakenAt - Snapshot.StartedAt:mm\\:ss}";
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build "MainApp/MainApp.csproj"`
Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add MainApp/src/5Events/SnapshotSummaryVm.cs
git commit -m "Add SnapshotSummaryVm wrapper for damage report round list"
```

---

### Task 3: Create `SnapshotEntryDisplayVm`

**Files:**
- Create: `MainApp/src/5Events/SnapshotEntryDisplayVm.cs`

This mirrors `DpsEntryVm` (`MainApp/src/5Events/DpsEntryVm.cs`) but for **static, already-persisted** data — `EventCombatSnapshotEntry` (in `LibEvents/Entities/EventCombatSnapshotEntry.cs`) already has `Username`, `Damage`, `Heal`, `Dps`, `Hps`, `CombatTimeSeconds` precomputed and persisted, so there's no `Update()`, no timer, no `PropertyChanged` — everything is computed once in the constructor. The medal/format logic and field names are copied 1:1 from `DpsEntryVm` (`RankLabel`, `DpsDisplay`, `DamageDisplay`, `HpsDisplay`, `HealDisplay`, `CombatTimeDisplay`, `DamagePercent`, `HealPercent`, `Fmt`) so the existing bar `DataTemplate` can bind to it without changes to those binding paths.

- [ ] **Step 1: Create the file**

```csharp
using LibEvents.Entities;

namespace Albion_App._5Events;

/// <summary>
/// Wrapper estático e inmutable de <see cref="EventCombatSnapshotEntry"/> — una fila
/// de jugador en el panel de barras de <see cref="EventDamageReportV"/>.
/// Los datos de un snapshot ya finalizado no cambian, así que todo se calcula
/// una sola vez en el constructor (sin Update(), sin timers, sin PropertyChanged).
/// Nombres de propiedad y formato calcados de <see cref="DpsEntryVm"/> para reusar
/// el mismo DataTemplate de barras.
/// </summary>
public sealed class SnapshotEntryDisplayVm
{
    private static readonly string[] Medals = ["🥇", "🥈", "🥉"];

    public string Username          { get; }
    public string RankLabel         { get; }
    public string DpsDisplay        { get; }
    public string DamageDisplay     { get; }
    public string HpsDisplay        { get; }
    public string HealDisplay       { get; }
    public string CombatTimeDisplay { get; }
    public double DamagePercent     { get; }
    public double HealPercent       { get; }
    public bool   HasDamage         { get; }
    public bool   HasHeal           { get; }

    public SnapshotEntryDisplayVm(EventCombatSnapshotEntry entry, int rank, long maxDamage, long maxHeal)
    {
        Username  = entry.Username;
        RankLabel = rank <= Medals.Length ? Medals[rank - 1] : rank.ToString();
        HasDamage = entry.Damage > 0;
        HasHeal   = entry.Heal   > 0;

        DpsDisplay    = HasDamage ? Fmt(entry.Dps)    : "—";
        DamageDisplay = HasDamage ? Fmt(entry.Damage) : "—";
        HpsDisplay    = HasHeal   ? Fmt(entry.Hps)    : "";
        HealDisplay   = HasHeal   ? Fmt(entry.Heal)   : "";

        CombatTimeDisplay = TimeSpan.FromSeconds(entry.CombatTimeSeconds).ToString(@"mm\:ss");

        DamagePercent = maxDamage > 0 ? (double)entry.Damage / maxDamage : 0;
        HealPercent   = maxHeal   > 0 ? (double)entry.Heal   / maxHeal   : 0;
    }

    private static string Fmt(double v) => v >= 1000 ? $"{v / 1000:0.#}K"   : $"{v:0}";
    private static string Fmt(long   v) => v >= 1000 ? $"{v / 1000.0:0.#}K" : $"{v}";
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build "MainApp/MainApp.csproj"`
Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add MainApp/src/5Events/SnapshotEntryDisplayVm.cs
git commit -m "Add SnapshotEntryDisplayVm — static bar-display wrapper for snapshot entries"
```

---

### Task 4: Create `EventDamageReportVm`

**Files:**
- Create: `MainApp/src/5Events/EventDamageReportVm.cs`

This is the singleton VM behind the dialog. It follows the same shape as `EventHistoryVm` (`MainApp/src/5Events/EventHistoryVm.cs`): constructed with `BuildService`, exposes `[ObservableProperty]` collections/selection, loaded on demand via an async `LoadAsync`. Unlike `EventHistoryVm` it takes the target `GuildEvent` directly in `LoadAsync` (no `IDialogLifecycle`/`OnOpened` — the caller loads before opening the dialog, like `OpenDpsMeter` does in `EventsVm.cs:289-299`).

`GuildEvent` (`LibEvents/Entities/GuildEvent.cs`) has `Id`, `Name`, and `Snapshots`. `BuildService.GetSnapshotsAsync(int eventId)` (`LibSolutions/LibEvents/Services/BuildService.cs:407-415`) returns `List<EventCombatSnapshot>` ordered chronologically by `StartedAt`, with `Entries` already ordered by `Damage` descending.

- [ ] **Step 1: Create the file**

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LibEvents.Entities;
using LibEvents.Services;

namespace Albion_App._5Events;

/// <summary>
/// VM del diálogo "Ver Daño" para eventos finalizados — instancia única inyectada
/// desde App.xaml.cs en EventsVm (igual que EventHistoryVm/DpsMeterVm).
///
/// Carga todas las snapshots de combate guardadas durante un evento y expone
/// la ronda seleccionada como una lista de barras de daño/heal estáticas.
/// </summary>
public partial class EventDamageReportVm : ObservableObject
{
    private readonly BuildService _buildService;

    [ObservableProperty] private string _eventName = "";
    [ObservableProperty] private bool   _hasSnapshots;

    [ObservableProperty] private ObservableCollection<SnapshotSummaryVm> _snapshots = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedEntries))]
    private SnapshotSummaryVm? _selectedSnapshot;

    public EventDamageReportVm(BuildService buildService) => _buildService = buildService;

    /// <summary>Carga las snapshots del evento dado y selecciona la ronda más reciente.</summary>
    public async Task LoadAsync(GuildEvent ev)
    {
        EventName = ev.Name;

        var snapshots = await _buildService.GetSnapshotsAsync(ev.Id);
        Snapshots = new ObservableCollection<SnapshotSummaryVm>(
            snapshots.Select((s, i) => new SnapshotSummaryVm(s, i + 1)));

        HasSnapshots     = Snapshots.Count > 0;
        SelectedSnapshot = Snapshots.LastOrDefault();
    }

    /// <summary>Barras de daño/heal de la ronda seleccionada, con porcentajes relativos al máximo de esa ronda.</summary>
    public IEnumerable<SnapshotEntryDisplayVm> SelectedEntries
    {
        get
        {
            if (SelectedSnapshot is null) yield break;

            var entries = SelectedSnapshot.Snapshot.Entries.ToList();
            long maxDamage = entries.Count > 0 ? entries.Max(e => e.Damage) : 0;
            long maxHeal   = entries.Count > 0 ? entries.Max(e => e.Heal)   : 0;

            for (int i = 0; i < entries.Count; i++)
                yield return new SnapshotEntryDisplayVm(entries[i], i + 1, maxDamage, maxHeal);
        }
    }
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build "MainApp/MainApp.csproj"`
Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add MainApp/src/5Events/EventDamageReportVm.cs
git commit -m "Add EventDamageReportVm — loads and exposes event combat snapshots"
```

---

### Task 5: Create `EventDamageReportV` (master-detail UserControl)

**Files:**
- Create: `MainApp/src/5Events/EventDamageReportV.xaml`
- Create: `MainApp/src/5Events/EventDamageReportV.xaml.cs`

The right-hand bar `DataTemplate` is copied from `DpsMeterV.xaml:164-285` (the `<DataTemplate DataType="{x:Type local:DpsEntryVm}">` block), with the `IsOffline`-opacity `Grid.Style` and the `IsLocalPlayer` "(yo)" marker removed — `SnapshotEntryDisplayVm` has no such concepts (frozen historical data has no online/local-player state, and the user only asked for damage/heal/combat-time to be shown "tal cual"). All other bindings (`HasDamage`, `DamagePercent`, `RankLabel`, `Username`, `DpsDisplay`, `DamageDisplay`, `HasHeal`, `HealPercent`, `HpsDisplay`, `HealDisplay`, `CombatTimeDisplay`) keep identical names and gradient colors, so the visual result matches the live DPS meter exactly. `BoolToVis` is a `BooleanToVisibilityConverter` declared as an app-wide resource in `MainApp/src/Helpers/Template.xaml:14` (already used via `{StaticResource BoolToVis}` in `DpsMeterV.xaml` without a local declaration — same applies here).

The empty-state message follows the exact pattern of `DpsMeterV.xaml:144-159` ("Sin actividad de combate" — `Style.Triggers`/`DataTrigger` toggling `Visibility`, not a converter parameter, since `BooleanToVisibilityConverter` has no "invert" option).

- [ ] **Step 1: Create `EventDamageReportV.xaml`**

```xml
<UserControl x:Class="Albion_App._5Events.EventDamageReportV"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:md="http://materialdesigninxaml.net/winfx/xaml/themes"
             xmlns:local="clr-namespace:Albion_App._5Events"
             mc:Ignorable="d"
             d:DesignHeight="420" d:DesignWidth="640"
             d:DataContext="{d:DesignInstance local:EventDamageReportVm}">

    <Grid>

        <!-- ── Estado vacío ──────────────────────────────────────────────── -->
        <TextBlock Text="Sin snapshots registrados"
                   FontSize="13"
                   HorizontalAlignment="Center"
                   VerticalAlignment="Center"
                   Foreground="{DynamicResource MaterialDesign.Brush.BodyLight}">
            <TextBlock.Style>
                <Style TargetType="TextBlock">
                    <Setter Property="Visibility" Value="Collapsed"/>
                    <Style.Triggers>
                        <DataTrigger Binding="{Binding HasSnapshots}" Value="False">
                            <Setter Property="Visibility" Value="Visible"/>
                        </DataTrigger>
                    </Style.Triggers>
                </Style>
            </TextBlock.Style>
        </TextBlock>

        <!-- ── Maestro-detalle ───────────────────────────────────────────── -->
        <Grid Visibility="{Binding HasSnapshots, Converter={StaticResource BoolToVis}}">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="180"/>
                <ColumnDefinition Width="*"/>
            </Grid.ColumnDefinitions>

            <!-- Lista de rondas -->
            <ListBox Grid.Column="0"
                     Margin="0 0 8 0"
                     ItemsSource="{Binding Snapshots}"
                     SelectedItem="{Binding SelectedSnapshot}">
                <ListBox.ItemTemplate>
                    <DataTemplate DataType="{x:Type local:SnapshotSummaryVm}">
                        <StackPanel Margin="8 6">
                            <TextBlock Text="{Binding Title}"
                                       FontSize="13" FontWeight="Bold"/>
                            <TextBlock Text="{Binding Subtitle}"
                                       FontSize="11" Opacity="0.6"/>
                        </StackPanel>
                    </DataTemplate>
                </ListBox.ItemTemplate>
            </ListBox>

            <!-- Barras de daño/heal de la ronda seleccionada -->
            <ScrollViewer Grid.Column="1" VerticalScrollBarVisibility="Auto">
                <ItemsControl ItemsSource="{Binding SelectedEntries}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate DataType="{x:Type local:SnapshotEntryDisplayVm}">
                            <Grid>

                                <!-- Barra de daño -->
                                <Border Margin="0 1"
                                        Visibility="{Binding HasDamage, Converter={StaticResource BoolToVis}}">
                                    <Border.Background>
                                        <LinearGradientBrush StartPoint="0,0" EndPoint="1,0">
                                            <GradientStop Color="#33EF5350" Offset="{Binding DamagePercent}"/>
                                            <GradientStop Color="Transparent" Offset="{Binding DamagePercent}"/>
                                        </LinearGradientBrush>
                                    </Border.Background>
                                    <Grid Height="42"/>
                                </Border>

                                <!-- Barra de heal -->
                                <Border Margin="0 1"
                                        Visibility="{Binding HasHeal, Converter={StaticResource BoolToVis}}">
                                    <Border.Background>
                                        <LinearGradientBrush StartPoint="0,0" EndPoint="1,0">
                                            <GradientStop Color="#3366BB6A" Offset="{Binding HealPercent}"/>
                                            <GradientStop Color="Transparent" Offset="{Binding HealPercent}"/>
                                        </LinearGradientBrush>
                                    </Border.Background>
                                    <Grid Height="42"/>
                                </Border>

                                <!-- Contenido -->
                                <Grid Margin="10 0" Height="42">
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="28"/>
                                        <ColumnDefinition Width="*"/>
                                        <ColumnDefinition Width="56"/>
                                        <ColumnDefinition Width="52"/>
                                        <ColumnDefinition Width="36"/>
                                    </Grid.ColumnDefinitions>

                                    <!-- Medalla -->
                                    <TextBlock Grid.Column="0"
                                               Text="{Binding RankLabel}"
                                               FontFamily="Segoe UI Emoji"
                                               FontSize="14"
                                               VerticalAlignment="Center"
                                               HorizontalAlignment="Center"/>

                                    <!-- Nombre -->
                                    <TextBlock Grid.Column="1"
                                               Text="{Binding Username}"
                                               FontSize="13"
                                               TextTrimming="CharacterEllipsis"
                                               VerticalAlignment="Center"/>

                                    <!-- DPS o HPS -->
                                    <StackPanel Grid.Column="2" VerticalAlignment="Center" HorizontalAlignment="Right">
                                        <StackPanel Visibility="{Binding HasDamage, Converter={StaticResource BoolToVis}}">
                                            <TextBlock Text="{Binding DpsDisplay}"
                                                       FontSize="13" FontWeight="Bold"
                                                       Foreground="#EF5350"
                                                       HorizontalAlignment="Right"/>
                                            <TextBlock Text="DPS" FontSize="8" Opacity="0.5"
                                                       HorizontalAlignment="Right"/>
                                        </StackPanel>
                                        <StackPanel Visibility="{Binding HasHeal, Converter={StaticResource BoolToVis}}">
                                            <TextBlock Text="{Binding HpsDisplay}"
                                                       FontSize="13" FontWeight="Bold"
                                                       Foreground="#66BB6A"
                                                       HorizontalAlignment="Right"/>
                                            <TextBlock Text="HPS" FontSize="8" Opacity="0.5"
                                                       HorizontalAlignment="Right"/>
                                        </StackPanel>
                                    </StackPanel>

                                    <!-- Total daño / heal -->
                                    <StackPanel Grid.Column="3" VerticalAlignment="Center" HorizontalAlignment="Right">
                                        <TextBlock Text="{Binding DamageDisplay}"
                                                   FontSize="11" Opacity="0.7"
                                                   HorizontalAlignment="Right"
                                                   Visibility="{Binding HasDamage, Converter={StaticResource BoolToVis}}"/>
                                        <TextBlock Text="{Binding HealDisplay}"
                                                   FontSize="11" Opacity="0.7" Foreground="#66BB6A"
                                                   HorizontalAlignment="Right"
                                                   Visibility="{Binding HasHeal, Converter={StaticResource BoolToVis}}"/>
                                    </StackPanel>

                                    <!-- Tiempo de combate -->
                                    <TextBlock Grid.Column="4"
                                               Text="{Binding CombatTimeDisplay}"
                                               FontSize="10" FontFamily="Consolas"
                                               Opacity="0.4"
                                               VerticalAlignment="Center"
                                               HorizontalAlignment="Right"/>
                                </Grid>
                            </Grid>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </ScrollViewer>
        </Grid>
    </Grid>
</UserControl>
```

- [ ] **Step 2: Create `EventDamageReportV.xaml.cs`**

Copy the minimal code-behind pattern from `DpsMeterV.xaml.cs` (`MainApp/src/5Events/DpsMeterV.xaml.cs`):

```csharp
using System.Windows.Controls;

namespace Albion_App._5Events;

public partial class EventDamageReportV : UserControl
{
    public EventDamageReportV()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build "MainApp/MainApp.csproj"`
Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add MainApp/src/5Events/EventDamageReportV.xaml MainApp/src/5Events/EventDamageReportV.xaml.cs
git commit -m "Add EventDamageReportV — master-detail view of event combat snapshots"
```

---

### Task 6: Create `EventDamageReportDialogV`

**Files:**
- Create: `MainApp/src/5Events/EventDamageReportDialogV.xaml`
- Create: `MainApp/src/5Events/EventDamageReportDialogV.xaml.cs`

Copied structurally from `DpsMeterDialogV.xaml`/`.xaml.cs` (`MainApp/src/5Events/DpsMeterDialogV.xaml[.cs]`) — `md:Card` with header (`Primary.Dark` background + icon + title), body, footer with a "Cerrar" button. `PackIconKind.ChartBar` is already used elsewhere in this project (confirmed via grep on `MainApp/src`), so it's a valid icon kind.

- [ ] **Step 1: Create `EventDamageReportDialogV.xaml`**

```xml
<UserControl x:Class="Albion_App._5Events.EventDamageReportDialogV"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:md="http://materialdesigninxaml.net/winfx/xaml/themes"
             xmlns:local="clr-namespace:Albion_App._5Events"
             mc:Ignorable="d"
             d:DataContext="{d:DesignInstance local:EventDamageReportVm}">

    <md:Card Width="640" UniformCornerRadius="8">
        <Grid>
            <Grid.RowDefinitions>
                <RowDefinition Height="56"/>
                <RowDefinition Height="420"/>
                <RowDefinition Height="56"/>
            </Grid.RowDefinitions>

            <!-- ══ HEADER ══════════════════════════════════════════════════ -->
            <Border Grid.Row="0"
                    Background="{DynamicResource MaterialDesign.Brush.Primary.Dark}"
                    CornerRadius="8 8 0 0">
                <DockPanel Margin="16 0">
                    <md:PackIcon Kind="ChartBar"
                                 Foreground="White"
                                 Width="22" Height="22"
                                 VerticalAlignment="Center"
                                 Margin="0 0 10 0"/>
                    <TextBlock Text="{Binding EventName, StringFormat='Reporte de daño — {0}'}"
                               Foreground="White"
                               FontSize="17" FontWeight="Medium"
                               VerticalAlignment="Center"
                               TextTrimming="CharacterEllipsis"/>
                </DockPanel>
            </Border>

            <!-- ══ BODY ════════════════════════════════════════════════════ -->
            <local:EventDamageReportV Grid.Row="1"
                                      DataContext="{Binding}"
                                      Margin="12"/>

            <!-- ══ FOOTER ═══════════════════════════════════════════════════ -->
            <Border Grid.Row="2"
                    BorderBrush="{DynamicResource MaterialDesign.Brush.Separator}"
                    BorderThickness="0 1 0 0">
                <DockPanel Margin="16 0" LastChildFill="False">
                    <Button DockPanel.Dock="Right"
                            Content="Cerrar"
                            Style="{StaticResource MaterialDesignOutlinedButton}"
                            Click="OnClose"/>
                </DockPanel>
            </Border>

        </Grid>
    </md:Card>

</UserControl>
```

- [ ] **Step 2: Create `EventDamageReportDialogV.xaml.cs`**

```csharp
using System.Windows;
using System.Windows.Controls;
using Albion_App.Features.DataStatic;
using Utilidades.Dialogs;

namespace Albion_App._5Events;

public partial class EventDamageReportDialogV : UserControl, IDialogBase
{
    public string DialogNameIdentifier { get; set; } = DialogDefaults.DamageReport;
    public string DialogOpenIdentifier { get; set; } = DialogDefaults.Main;
    public string TextHeader           { get; set; } = "Reporte de daño";

    public EventDamageReportDialogV(EventDamageReportVm vm)
    {
        DataContext = vm;
        InitializeComponent();
    }

    private void OnClose(object sender, RoutedEventArgs e)
        => _ = DialogService.Instance.CerrarSiEstaAbiertoYEsperar(this);
}
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build "MainApp/MainApp.csproj"`
Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add MainApp/src/5Events/EventDamageReportDialogV.xaml MainApp/src/5Events/EventDamageReportDialogV.xaml.cs
git commit -m "Add EventDamageReportDialogV — Card dialog wrapper for the damage report"
```

---

### Task 7: Wire `EventDamageReportVm` into `EventsVm`

**Files:**
- Modify: `MainApp/src/5Events/EventsVm.cs:155-183` (constructor + properties)
- Modify: `MainApp/src/5Events/EventsVm.cs:289-299` (commands — add `OpenDamageReport` next to `OpenDpsMeter`)

`EventsVm` already injects `History`/`DpsMeter` as constructor-supplied singletons exposed as public properties (`MainApp/src/5Events/EventsVm.cs:155-157,164-183`). Add `DamageReport` the same way. The new command mirrors `OpenDpsMeter` (`EventsVm.cs:289-299`) but loads the report for the specific event first (the report dialog needs to know *which* finished event was clicked — `OpenDpsMeter` doesn't need this because `DpsMeterVm` already tracks the live session).

- [ ] **Step 1: Add the `DamageReport` property next to `DpsMeter`**

In `MainApp/src/5Events/EventsVm.cs`, change (around line 155-160):

```csharp
    public EventEditorVm     Editor     { get; }
    public EventHistoryVm    History    { get; }
    public DpsMeterVm?       DpsMeter   { get; }

    /// <summary>true cuando hay combate activo — muestra el botón "Ver Daño".</summary>
    public bool ShowDpsMeterButton => DpsMeter is not null && (IsRunning || IsPaused);
```

to:

```csharp
    public EventEditorVm        Editor       { get; }
    public EventHistoryVm       History      { get; }
    public DpsMeterVm?          DpsMeter     { get; }
    public EventDamageReportVm  DamageReport { get; }

    /// <summary>true cuando hay combate activo — muestra el botón "Ver Daño" del medidor en vivo.</summary>
    public bool ShowDpsMeterButton => DpsMeter is not null && (IsRunning || IsPaused);
```

- [ ] **Step 2: Add the constructor parameter and assignment**

Change the constructor (currently `EventsVm.cs:164-183`):

```csharp
    public EventsVm(
        BuildService      buildService,
        EventEditorVm     editor,
        DiscordBotService discordBot,
        AppConfigService  appConfig,
        EventHistoryVm    history,
        CombatSession?    combatSession = null,
        DpsMeterVm?       dpsMeter      = null)
    {
        _buildService    = buildService;
        _discordBot      = discordBot;
        _appConfig       = appConfig;
        _combatSession   = combatSession;
        Editor           = editor;
        History          = history;
        DpsMeter         = dpsMeter;   // instancia compartida desde App.xaml.cs

        Editor.Saved += OnEditorSaved;
        _discordBot.ParticipationChanged += OnParticipationChanged;
    }
```

to:

```csharp
    public EventsVm(
        BuildService         buildService,
        EventEditorVm        editor,
        DiscordBotService    discordBot,
        AppConfigService     appConfig,
        EventHistoryVm       history,
        EventDamageReportVm  damageReport,
        CombatSession?       combatSession = null,
        DpsMeterVm?          dpsMeter      = null)
    {
        _buildService    = buildService;
        _discordBot      = discordBot;
        _appConfig       = appConfig;
        _combatSession   = combatSession;
        Editor           = editor;
        History          = history;
        DpsMeter         = dpsMeter;       // instancia compartida desde App.xaml.cs
        DamageReport     = damageReport;   // instancia compartida desde App.xaml.cs

        Editor.Saved += OnEditorSaved;
        _discordBot.ParticipationChanged += OnParticipationChanged;
    }
```

- [ ] **Step 3: Add the `OpenDamageReport` command**

In `MainApp/src/5Events/EventsVm.cs`, immediately after the existing `OpenDpsMeter` command (currently lines 289-299):

```csharp
    [RelayCommand]
    private async Task OpenDpsMeter()
    {
        if (DpsMeter is null) return;
        var dialog = new DpsMeterDialogV(DpsMeter)
        {
            DialogOpenIdentifier = DialogDefaults.Main,
            DialogNameIdentifier = DialogDefaults.DpsMeter,
        };
        await DialogService.Instance.MostrarDialogo(dialog);
    }
```

add:

```csharp
    [RelayCommand]
    private async Task OpenDamageReport(GuildEvent ev)
    {
        await DamageReport.LoadAsync(ev);
        await DialogService.Instance.MostrarDialogo<EventDamageReportDialogV>(
            DamageReport,
            "Reporte de daño",
            DialogDefaults.Main,
            DialogDefaults.DamageReport);
    }
```

- [ ] **Step 4: Build to verify it compiles**

Run: `dotnet build "MainApp/MainApp.csproj"`
Expected: Build error — `App.xaml.cs:164` no longer matches the `EventsVm` constructor signature (missing the new required `damageReport` argument). This is expected; Task 8 fixes it.

- [ ] **Step 5: Commit**

```bash
git add MainApp/src/5Events/EventsVm.cs
git commit -m "Wire EventDamageReportVm into EventsVm with OpenDamageReportCommand"
```

---

### Task 8: Instantiate and inject `EventDamageReportVm` in `App.xaml.cs`

**Files:**
- Modify: `MainApp/App.xaml.cs:158-164`

This fixes the build error from Task 7 by constructing the singleton VM and passing it to `EventsVm`, exactly like `eventHistoryVm`/`dpsMeterVm` (`App.xaml.cs:159,163-164`).

- [ ] **Step 1: Instantiate the VM and pass it to `EventsVm`**

Change (currently `App.xaml.cs:158-164`):

```csharp
        var eventEditorVm   = new EventEditorVm(buildService);
        var eventHistoryVm  = new EventHistoryVm(buildService);
        var combatSession   = new CombatSession();
        var itemResolver    = new ItemIndexResolverAdapter(itemDataService);
        var partyTracker    = new PartyTracker(combatSession);
        var dpsMeterVm      = new DpsMeterVm(combatSession, partyTracker.Party);
        var eventsVm        = new EventsVm(buildService, eventEditorVm, discordBot, config, eventHistoryVm, combatSession, dpsMeterVm);
```

to:

```csharp
        var eventEditorVm      = new EventEditorVm(buildService);
        var eventHistoryVm     = new EventHistoryVm(buildService);
        var eventDamageReportVm = new EventDamageReportVm(buildService);
        var combatSession      = new CombatSession();
        var itemResolver       = new ItemIndexResolverAdapter(itemDataService);
        var partyTracker       = new PartyTracker(combatSession);
        var dpsMeterVm         = new DpsMeterVm(combatSession, partyTracker.Party);
        var eventsVm           = new EventsVm(buildService, eventEditorVm, discordBot, config, eventHistoryVm, eventDamageReportVm, combatSession, dpsMeterVm);
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build "MainApp/MainApp.csproj"`
Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add MainApp/App.xaml.cs
git commit -m "Instantiate EventDamageReportVm singleton in App.xaml.cs"
```

---

### Task 9: Add the "Ver Daño" button to the Finished actions block

**Files:**
- Modify: `MainApp/src/5Events/EventsV.xaml:580-588`

The `Finished` actions `StackPanel` currently only has "Cerrar evento". Add "Ver Daño" before it, bound to the new `OpenDamageReportCommand`. Style/colors copied from the existing live-DPS "Ver Daño" button (`EventsV.xaml:551-555`) — same red outline — since both represent "view damage", just for different event states (this one only shows when `IsFinished`, the other only when `Running`/`Paused`, so they never appear together).

- [ ] **Step 1: Add the button**

Change (currently `EventsV.xaml:580-588`):

```xml
                        <!-- ── Acciones: Finished ─────────────────────────────── -->
                        <StackPanel Orientation="Horizontal" Margin="0 12 0 0"
                                    Visibility="{Binding IsFinished, Converter={StaticResource BoolToVis}}">
                            <Button Command="{Binding CloseEventCommand}"
                                    CommandParameter="{Binding SelectedEvent}"
                                    Content="Cerrar evento"
                                    Style="{StaticResource MaterialDesignRaisedButton}"
                                    Background="#1B5E20" BorderBrush="#1B5E20"/>
                        </StackPanel>
```

to:

```xml
                        <!-- ── Acciones: Finished ─────────────────────────────── -->
                        <StackPanel Orientation="Horizontal" Margin="0 12 0 0"
                                    Visibility="{Binding IsFinished, Converter={StaticResource BoolToVis}}">
                            <Button Command="{Binding OpenDamageReportCommand}"
                                    CommandParameter="{Binding SelectedEvent}"
                                    Content="Ver Daño"
                                    Style="{StaticResource MaterialDesignOutlinedButton}"
                                    Foreground="#EF5350" BorderBrush="#EF5350"
                                    Margin="0 0 8 0"/>
                            <Button Command="{Binding CloseEventCommand}"
                                    CommandParameter="{Binding SelectedEvent}"
                                    Content="Cerrar evento"
                                    Style="{StaticResource MaterialDesignRaisedButton}"
                                    Background="#1B5E20" BorderBrush="#1B5E20"/>
                        </StackPanel>
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build "MainApp/MainApp.csproj"`
Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add MainApp/src/5Events/EventsV.xaml
git commit -m "Add Ver Daño button to finished-event actions, opening the damage report dialog"
```

---

### Task 10: Manual verification

**Files:** none (verification only)

There is no automated UI test harness for WPF dialogs in this project. Verify manually by running the app:

- [ ] **Step 1: Run the app**

Run: `dotnet run --project "MainApp/MainApp.csproj"`

- [ ] **Step 2: Produce a Finished event with snapshots**

In the Eventos section: activate a template event, advance it through Preparation → Running, use the live DPS meter's Stop button at least twice to save two snapshots (`DamageVm.OnSnapshotTaken` persists via `BuildService.SaveSnapshotAsync` whenever `_activeEventId` is set — confirm the event is the active one), then use "Finalizar InProgress" to bring it to `Finished`.

- [ ] **Step 3: Open the damage report**

Select the finished event, click "Ver Daño" in the `Finished` actions row.
Expected: dialog opens titled "Reporte de daño — {nombre del evento}", left list shows "Ronda 1"/"Ronda 2" with correct start time and duration, the most recent round is preselected, and the right panel shows damage/heal bars matching the persisted snapshot data (compare totals against what the live DPS meter showed before each Stop).

- [ ] **Step 4: Verify selection updates the bars**

Click "Ronda 1" in the list.
Expected: the right panel recalculates — bars, percentages and rankings reflect round 1's data, not round 2's.

- [ ] **Step 5: Verify the empty state**

Open the report for a `Finished` event that has no saved snapshots (e.g. one that was never started via the live DPS meter).
Expected: centered "Sin snapshots registrados" message; no list, no bars.

- [ ] **Step 6: Close the dialog**

Click "Cerrar".
Expected: dialog closes cleanly, returns to the Eventos panel.

No commit for this task — it's verification only. If any step fails, fix the underlying code in the relevant earlier task and re-commit there (don't bundle fixes into this verification task).

---

## Self-review notes

- **Spec coverage:** All sections of `2026-06-08-event-damage-report-design.md` are covered — singleton injection (Task 8), `LoadAsync(GuildEvent)` entry point (Task 4/7), `SnapshotSummaryVm`/`SnapshotEntryDisplayVm` (Tasks 2-3), master-detail layout reusing `DpsMeterV` bars (Task 5), `Card` dialog wrapper (Task 6), button placement in the `Finished` block (Task 9), empty-state handling (Task 5), manual verification plan (Task 10).
- **Type/name consistency checked:** `SnapshotSummaryVm.Title`/`Subtitle` (Task 2) match the `ListBox.ItemTemplate` bindings in Task 5. `SnapshotEntryDisplayVm` property names (Task 3) match every binding in the bar `DataTemplate` (Task 5) and the constructor signature `(entry, rank, maxDamage, maxHeal)` matches the call site in `EventDamageReportVm.SelectedEntries` (Task 4). `EventDamageReportVm.LoadAsync(GuildEvent ev)` (Task 4) matches the call in `OpenDamageReportCommand` (Task 7). Constructor parameter order in `EventsVm` (Task 7) matches the call site in `App.xaml.cs` (Task 8).
- **No placeholders:** every step shows complete, copy-ready code — no "add error handling"/"TBD"/"similar to Task N" shortcuts.
