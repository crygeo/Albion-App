# Eventos: Diálogo "Ver Daño" para eventos finalizados

**Date:** 2026-06-08
**Project:** AlbionApp (MainApp/src/5Events)
**Status:** Approved

---

## Overview

Cuando un `GuildEvent` pasa a estado `Finished`, hoy el panel de detalle solo ofrece el botón "Cerrar evento". Se añade un nuevo botón **"Ver Daño"** (distinto del que ya existe para Running/Paused, que abre el medidor de DPS en vivo) que abre un diálogo mostrando **todas las snapshots de combate guardadas durante ese evento**, junto con el resultado gráfico (barras de daño/heal) de la ronda seleccionada — visualmente igual al medidor de DPS en vivo (`DpsMeterV`), mostrando tiempo de combate, daño y heal.

El modelo de datos y la persistencia ya existen y no requieren cambios:
- `GuildEvent.Snapshots` → `ICollection<EventCombatSnapshot>`
- `EventCombatSnapshot` (`Id`, `EventId`, `StartedAt`, `TakenAt`, `Entries`)
- `EventCombatSnapshotEntry` (`Username`, `Damage`, `Heal`, `TakenDamage`, `Overhealed`, `Dps`, `Hps`, `CombatTimeSeconds`)
- `BuildService.GetSnapshotsAsync(eventId)` → trae snapshots ordenadas por `StartedAt` con `Entries` ordenadas por `Damage` descendente.

**Files changed:**
- `MainApp/src/5Events/EventsVm.cs` (nuevo comando + propiedad `DamageReport`)
- `MainApp/src/5Events/EventsV.xaml` (nuevo botón en bloque de acciones `IsFinished`)
- `MainApp/src/Models/DialogDefaults.cs` (nueva constante `DamageReport`)
- `MainApp/App.xaml.cs` (instanciar e inyectar `EventDamageReportVm`)

**Files created:**
- `MainApp/src/5Events/EventDamageReportVm.cs`
- `MainApp/src/5Events/SnapshotSummaryVm.cs`
- `MainApp/src/5Events/SnapshotEntryDisplayVm.cs`
- `MainApp/src/5Events/EventDamageReportV.xaml` (+ `.xaml.cs`)
- `MainApp/src/5Events/EventDamageReportDialogV.xaml` (+ `.xaml.cs`)

**No changes to:** entidades, esquema de BD, migraciones, `BuildService` (los métodos de snapshot ya existen).

---

## Componentes y arquitectura

```
EventsVm
 └─ OpenDamageReportCommand(GuildEvent ev)
     └─ DamageReport.LoadAsync(ev)
         └─ BuildService.GetSnapshotsAsync(eventId)
     └─ DialogService → EventDamageReportDialogV (Card, patrón de DpsMeterDialogV)
         └─ EventDamageReportV (UserControl)
             ├─ ListBox de SnapshotSummaryVm        (columna izquierda)
             └─ ItemsControl de SnapshotEntryDisplayVm  (columna derecha — barras)
```

### `EventDamageReportVm`

Inyectado como instancia única desde `App.xaml.cs` y pasado por constructor a `EventsVm`, igual que `EventHistoryVm`/`DpsMeterVm`:

```csharp
var eventDamageReportVm = new EventDamageReportVm(buildService);
var eventsVm = new EventsVm(buildService, eventEditorVm, discordBot, config,
                            eventHistoryVm, combatSession, dpsMeterVm, eventDamageReportVm);
```

Expuesto en `EventsVm` como `public EventDamageReportVm DamageReport { get; }`.

Propiedades y comportamiento:
- `string EventName` — nombre del evento (para el header del diálogo).
- `ObservableCollection<SnapshotSummaryVm> Snapshots` — lista de rondas, en orden cronológico.
- `SnapshotSummaryVm? SelectedSnapshot` — `[ObservableProperty]`; al cambiar (`partial void OnSelectedSnapshotChanged`) dispara `OnPropertyChanged(nameof(SelectedEntries))`.
- `IEnumerable<SnapshotEntryDisplayVm> SelectedEntries` — propiedad calculada (getter), reconstruye la lista de entradas de la ronda seleccionada con sus porcentajes relativos al máximo de esa ronda.
- `bool HasSnapshots` — para alternar entre la lista y el mensaje de estado vacío.

```csharp
public async Task LoadAsync(GuildEvent ev)
{
    EventName = ev.Name;
    var snapshots = await _buildService.GetSnapshotsAsync(ev.Id);
    Snapshots = new ObservableCollection<SnapshotSummaryVm>(
        snapshots.Select((s, i) => new SnapshotSummaryVm(s, i + 1)));
    HasSnapshots   = Snapshots.Count > 0;
    SelectedSnapshot = Snapshots.LastOrDefault();   // ronda más reciente por defecto
}
```

No implementa `IDialogLifecycle`: la carga ocurre antes de abrir el diálogo (igual que `DpsMeterDialogV`, no como `EventHistoryDialogV`/`OnOpened`), porque el comando ya tiene la referencia al `GuildEvent` y no hay necesidad de un paso intermedio `SetEvent` + `OnOpened`.

### `SnapshotSummaryVm`

Wrapper ligero e inmutable de `EventCombatSnapshot` para la fila de lista:

```csharp
public sealed class SnapshotSummaryVm(EventCombatSnapshot snapshot, int roundNumber)
{
    public EventCombatSnapshot Snapshot    { get; } = snapshot;
    public int                 RoundNumber { get; } = roundNumber;
    public string              Title       => $"Ronda {RoundNumber}";
    public string              StartedLabel => Snapshot.StartedAt.ToLocalTime().ToString("HH:mm");
    public string              DurationLabel => (Snapshot.TakenAt - Snapshot.StartedAt).ToString(@"mm\:ss");
}
```

Cada ítem de la lista muestra: `Ronda N · HH:mm · mm:ss` (número, hora de inicio local, duración).

### `SnapshotEntryDisplayVm`

Wrapper estático (sin `PropertyChanged`, sin timers — los datos de un snapshot ya finalizado no cambian) de `EventCombatSnapshotEntry`, calculado una sola vez en el constructor, replicando los campos de `DpsEntryVm`:

```csharp
public sealed class SnapshotEntryDisplayVm
{
    public string Username          { get; }
    public string RankLabel         { get; }   // 🥇🥈🥉 o "{n}"
    public string DpsDisplay        { get; }
    public string DamageDisplay     { get; }
    public string HpsDisplay        { get; }
    public string HealDisplay       { get; }
    public string CombatTimeDisplay { get; }
    public double DamagePercent     { get; }   // relativo al máx. del snapshot
    public double HealPercent       { get; }
    public bool   HasDamage         { get; }
    public bool   HasHeal           { get; }

    public SnapshotEntryDisplayVm(EventCombatSnapshotEntry e, int rank, long maxDmg, long maxHeal)
    {
        // cálculo directo desde los campos persistidos (Damage, Heal, Dps, Hps, CombatTimeSeconds)
        // mismo formato que DpsEntryVm.Fmt (K para miles)
    }
}
```

`EventDamageReportVm.SelectedEntries` reconstruye esta lista al vuelo desde `SelectedSnapshot.Snapshot.Entries` (ya vienen ordenadas por `Damage` descendente desde `BuildService`):

```csharp
public IEnumerable<SnapshotEntryDisplayVm> SelectedEntries
{
    get
    {
        if (SelectedSnapshot is null) yield break;
        var entries = SelectedSnapshot.Snapshot.Entries.ToList();
        long maxDmg  = entries.Count > 0 ? entries.Max(e => e.Damage) : 0;
        long maxHeal = entries.Count > 0 ? entries.Max(e => e.Heal)   : 0;
        for (int i = 0; i < entries.Count; i++)
            yield return new SnapshotEntryDisplayVm(entries[i], i + 1, maxDmg, maxHeal);
    }
}
```

No se cachea: cada snapshot tiene como mucho el tamaño del grupo del evento (decenas de entradas), por lo que recalcular al cambiar de selección es trivial.

---

## Comando y punto de entrada en `EventsVm`

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

Nuevo botón en el bloque de acciones `Visibility="{Binding IsFinished, ...}"` de `EventsV.xaml` (línea ~580), junto a "Cerrar evento":

```xml
<Button Command="{Binding OpenDamageReportCommand}"
        CommandParameter="{Binding SelectedEvent}"
        Content="Ver Daño"
        Style="{StaticResource MaterialDesignOutlinedButton}"
        Foreground="#EF5350" BorderBrush="#EF5350"
        Margin="0 0 8 0"/>
```

Nota: este botón coexiste con el "Ver Daño" existente (`OpenDpsMeterCommand`/`ShowDpsMeterButton`), pero son mutuamente excluyentes por estado — el del medidor en vivo solo aparece en `Running`/`Paused`, este nuevo solo en `Finished`. El mismo texto es intencional (el usuario lo especificó así); el comportamiento difiere según el contexto del evento.

`DialogDefaults.DamageReport = "dialogDamageReport"` se añade junto a las constantes existentes.

---

## UI — `EventDamageReportDialogV` / `EventDamageReportV`

Diálogo (`md:Card`) con el mismo patrón estructural que `DpsMeterDialogV`/`EventHistoryDialogV`: header (`Primary.Dark` + ícono + título "Reporte de daño — {EventName}"), body, footer con botón "Cerrar".

Layout del body (`EventDamageReportV`, dos columnas):

```
┌──────────────────┬───────────────────────────────────────────┐
│  Rondas          │   (barras — idéntico DataTemplate         │
│ ┌──────────────┐ │    que DpsMeterV: medalla, nombre,        │
│ │ Ronda 1      │ │    barra de daño/heal con gradiente,      │
│ │ 20:14 · 04:32│ │    DPS/HPS, total daño/heal,              │
│ ├──────────────┤ │    tiempo de combate)                     │
│ │ Ronda 2      │◄┤                                           │
│ │ 20:31 · 06:10│ │   🥇 Fulano   12.3K DPS   850K   04:32    │
│ └──────────────┘ │   🥈 Mengano   9.1K DPS   620K   04:32    │
└──────────────────┴───────────────────────────────────────────┘
```

- **Columna izquierda**: `ListBox` (`ItemsSource="{Binding Snapshots}"`, `SelectedItem="{Binding SelectedSnapshot}"`), ancho fijo (~170px), cada ítem muestra `Title` / `StartedLabel · DurationLabel`.
- **Columna derecha**: `ItemsControl` (`ItemsSource="{Binding SelectedEntries}"`) reutilizando el `DataTemplate` de barras de `DpsMeterV` (líneas 162-287: `LinearGradientBrush` para daño/heal, medalla, nombre, DPS/HPS, totales, tiempo de combate), adaptado a `SnapshotEntryDisplayVm`.
- **Estado vacío**: si `HasSnapshots == false`, mensaje centrado "Sin snapshots registrados" (mismo estilo que "Sin actividad de combate" en `DpsMeterV`); se oculta la lista y el panel de barras.

---

## Casos límite

- **Sin snapshots**: mensaje de estado vacío; no se muestra ni la lista ni las barras.
- **Reapertura con otro evento**: `LoadAsync(ev)` reemplaza `Snapshots` por completo y reselecciona el último — no queda estado residual del evento anterior.
- **Una sola ronda**: la lista muestra un único ítem, ya seleccionado por defecto.

---

## Pruebas

No existe infraestructura de tests para VMs/UI WPF en este proyecto (los proyectos `*.Tests` cubren capas de dominio/aplicación). Verificación manual:
1. Levantar la app, llevar un evento a `Finished` con al menos dos snapshots guardados (rondas Running→Stop).
2. Pulsar "Ver Daño" en el panel de acciones de `Finished`.
3. Confirmar: lista de rondas correcta (número/hora/duración), ronda más reciente preseleccionada, barras de daño/heal coinciden con los datos persistidos, seleccionar otra ronda actualiza las barras.
4. Probar con un evento `Finished` sin snapshots → mensaje de estado vacío.
