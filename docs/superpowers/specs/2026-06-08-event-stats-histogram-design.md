# Eventos: Diálogo de Estadísticas — Histograma de actividad

**Date:** 2026-06-08
**Project:** AlbionApp (MainApp/src/5Events)
**Status:** Approved

---

## Overview

Se añade un nuevo diálogo **"Estadísticas de eventos"**, accesible desde un botón en el header de la sección Eventos, que muestra un **histograma de la cantidad de eventos del historial agrupados por período** (día / semana / mes), con un filtro para incluir o no los eventos cancelados.

El objetivo es dar una vista rápida de cuán activo ha sido el gremio a lo largo del tiempo — sin agregar nuevos campos al modelo de datos ni nuevas consultas a `BuildService`: se reutiliza `GetEventHistoryAsync()`, que ya devuelve los eventos en estado `Closed` o `Cancelled`, y se agrupa/cuenta del lado del cliente según `ScheduledAt`.

**Files changed:**
- `MainApp/src/5Events/EventsVm.cs` (nuevo comando `OpenStatsCommand` + propiedad `Stats`)
- `MainApp/src/5Events/EventsV.xaml` (nuevo botón "Estadísticas" en el header)
- `MainApp/src/Models/DialogDefaults.cs` (nueva constante `EventStats`)
- `MainApp/App.xaml.cs` (instanciar e inyectar `EventStatsVm`)

**Files created:**
- `MainApp/src/5Events/EventStatsVm.cs`
- `MainApp/src/5Events/HistogramBucketVm.cs`
- `MainApp/src/5Events/EventStatsV.xaml` (+ `.xaml.cs`)
- `MainApp/src/5Events/EventStatsDialogV.xaml` (+ `.xaml.cs`)

**No changes to:** entidades, esquema de BD, migraciones, `BuildService` (se reutiliza `GetEventHistoryAsync` existente).

---

## Componentes y arquitectura

```
EventsVm
 └─ OpenStatsCommand()
     └─ Stats.LoadAsync()
         └─ BuildService.GetEventHistoryAsync()   (Closed + Cancelled)
     └─ DialogService → EventStatsDialogV (Card, patrón de EventDamageReportDialogV)
         └─ EventStatsV (UserControl)
             ├─ Filtro: chips Día / Semana / Mes  +  toggle "Solo completados"
             └─ ItemsControl de HistogramBucketVm  (barras verticales, scroll horizontal)
```

### `EventStatsVm`

Inyectado como instancia única desde `App.xaml.cs` y pasado por constructor a `EventsVm`, igual que `EventDamageReportVm`/`EventHistoryVm`:

```csharp
var eventStatsVm = new EventStatsVm(buildService);
var eventsVm     = new EventsVm(buildService, eventEditorVm, discordBot, config,
                                eventHistoryVm, eventDamageReportVm, eventStatsVm,
                                combatSession, dpsMeterVm);
```

Expuesto en `EventsVm` como `public EventStatsVm Stats { get; }`.

Propiedades y comportamiento:

- `HistogramGranularity Granularity` — `[ObservableProperty]`, enum `{ Day, Week, Month }`, por defecto `Day`. Al cambiar, `[NotifyPropertyChangedFor(nameof(Buckets))]` recalcula el histograma.
- `bool OnlyCompleted` — `[ObservableProperty]`, por defecto `false` (muestra `Closed` + `Cancelled`). `[NotifyPropertyChangedFor(nameof(Buckets))]`.
- `IEnumerable<HistogramBucketVm> Buckets` — propiedad calculada (getter): filtra `_allHistory` según `OnlyCompleted`, agrupa por `Granularity`, genera el rango continuo de períodos (incluyendo huecos en cero) y produce los `HistogramBucketVm` con su altura normalizada.
- `bool HasData` — `Buckets.Any(b => b.Count > 0)`, para alternar entre el histograma y el mensaje de estado vacío.

```csharp
private List<GuildEvent> _allHistory = [];

public async Task LoadAsync()
{
    _allHistory = await _buildService.GetEventHistoryAsync();
    OnPropertyChanged(nameof(Buckets));
    OnPropertyChanged(nameof(HasData));
}

[RelayCommand]
private void SelectGranularity(HistogramGranularity g) => Granularity = g;

public IEnumerable<HistogramBucketVm> Buckets
{
    get
    {
        var filtered = _allHistory
            .Where(e => e.ScheduledAt.HasValue)
            .Where(e => !OnlyCompleted || e.Status == EventStatus.Closed)
            .ToList();

        if (filtered.Count == 0)
            return [];

        var periods = BuildPeriodRange(filtered, Granularity);
        var counts  = filtered
            .GroupBy(e => PeriodKey(e.ScheduledAt!.Value, Granularity))
            .ToDictionary(g => g.Key, g => g.Count());

        var maxCount = periods.Select(p => counts.GetValueOrDefault(p.Key, 0)).DefaultIfEmpty(0).Max();

        return periods.Select(p => new HistogramBucketVm(
            p.Label, counts.GetValueOrDefault(p.Key, 0), maxCount));
    }
}
```

`OnOpened` no es necesario: igual que `EventDamageReportVm`, la carga se dispara explícitamente desde el comando que abre el diálogo, no desde el lifecycle del diálogo (el VM no implementa `IDialogLifecycle`).

### Lógica de agrupación — `BuildPeriodRange` / `PeriodKey`

Funciones privadas (helpers) en `EventStatsVm` que generan el **rango continuo** de períodos entre el `ScheduledAt` mínimo y máximo del conjunto filtrado, con una clave de agrupación y una etiqueta de presentación por granularidad:

| Granularidad | Clave de agrupación | Paso del rango | Etiqueta |
|---|---|---|---|
| `Day`   | `date.Date` | +1 día | `"08 jun"` (`dd MMM`) |
| `Week`  | lunes de la semana de `date` | +7 días | `"Sem 08–14 jun"`, o `"Sem 29 may–04 jun"` si la semana cruza de mes (se incluye el mes en ambas fechas solo cuando difieren) |
| `Month` | primer día del mes de `date` | +1 mes | `"jun 2026"` (`MMM yyyy`) |

El rango se genera siempre de forma continua (incluye huecos con conteo 0), iterando desde el primer período hasta el último inclusive. Esto es deliberado: el usuario quiere ver los períodos sin actividad como parte del patrón temporal, no que desaparezcan.

### `HistogramBucketVm`

Record inmutable (mismo estilo que `SnapshotEntryDisplayVm`): toda la presentación se computa una vez en el constructor, sin convertidores en XAML.

```csharp
public sealed record HistogramBucketVm
{
    public string Label  { get; }
    public int    Count  { get; }
    public double BarHeight { get; }   // píxeles, ya escalado contra MaxBarHeight

    private const double MaxBarHeight = 140;

    public HistogramBucketVm(string label, int count, int maxCount)
    {
        Label  = label;
        Count  = count;
        BarHeight = maxCount == 0 ? 0 : MaxBarHeight * count / maxCount;
    }
}
```

Los buckets en cero quedan con `BarHeight = 0`; la vista les dibuja una marca de línea base (ver más abajo) para que el hueco sea visualmente distinguible de "no renderizado".

---

## Vista — `EventStatsV`

Estructura (similar a `EventDamageReportV`, sin maestro-detalle ya que aquí no hay selección, solo visualización):

```
Grid
 ├─ Fila de filtros (chips Día/Semana/Mes a la izquierda, toggle "Solo completados" a la derecha)
 ├─ Estado vacío: "Sin eventos en el historial para este filtro" (Visibility ligado a !HasData)
 └─ ScrollViewer horizontal
     └─ ItemsControl ItemsSource="{Binding Buckets}" (StackPanel horizontal como ItemsPanel)
         └─ por cada HistogramBucketVm:
             Grid (columna, ancho fijo ~56px)
              ├─ TextBlock Count (arriba, centrado)
              ├─ Border barra: Height="{Binding BarHeight}", VerticalAlignment="Bottom",
              │    Background = MaterialDesign.Brush.Primary (o similar tono neutro azul/violeta,
              │    NO los colores rojo/verde de daño-heal)
              │    — si Count == 0, se dibuja una línea base delgada en su lugar
              └─ TextBlock Label (abajo, centrado, FontSize pequeño, posible TextTrimming si es angosto)
```

Los chips Día/Semana/Mes son tres `ToggleButton` (o `Button` con estilo de chip) que llaman a `SelectGranularityCommand` con el valor de enum correspondiente; el chip activo se resalta comparando `Granularity` con su parámetro (vía `DataTrigger`/`Style`, mismo patrón que otros toggles de la app). El toggle "Solo completados" es un `CheckBox`/`ToggleButton` ligado directamente a `OnlyCompleted`.

## Diálogo — `EventStatsDialogV`

Wrapper delgado, mismo patrón que `EventDamageReportDialogV`: `md:Card`, header con icono `ChartHistogram` y texto "Estadísticas de eventos", cuerpo = `EventStatsV`, footer con botón "Cerrar". Implementa `IDialogBase` (no `IDialogLifecycle`, ya que la carga se hace antes de abrir, igual que `EventDamageReportDialogV`).

```csharp
public partial class EventStatsDialogV : UserControl, IDialogBase
{
    public string DialogNameIdentifier { get; set; } = DialogDefaults.EventStats;
    public string DialogOpenIdentifier { get; set; } = DialogDefaults.Main;
    public string TextHeader           { get; set; } = "Estadísticas de eventos";
    ...
}
```

---

## Comando en `EventsVm`

```csharp
public EventStatsVm Stats { get; }

[RelayCommand]
private async Task OpenStats()
{
    await Stats.LoadAsync();
    await DialogService.Instance.MostrarDialogo<EventStatsDialogV>(
        Stats,
        "Estadísticas de eventos",
        DialogDefaults.Main,
        DialogDefaults.EventStats);
}
```

`EventsV` no es un diálogo (es un panel principal), así que `DialogDefaults.Main` es el host correcto — igual que `OpenDamageReportCommand`.

## Botón en `EventsV.xaml`

Nuevo `Button` tipo `MaterialDesignIconButton` en el `DockPanel` del header, junto a "Nueva plantilla de evento" y "Ver historial":

```xml
<Button DockPanel.Dock="Right"
        Command="{Binding OpenStatsCommand}"
        Style="{StaticResource MaterialDesignIconButton}"
        ToolTip="Estadísticas de eventos">
    <md:PackIcon Kind="ChartHistogram" Width="18" Height="18"/>
</Button>
```

---

## Casos límite

- **Sin historial / filtro vacío**: `Buckets` devuelve una colección vacía; `HasData = false`; se muestra el mensaje de estado vacío en lugar del histograma.
- **Un solo evento**: el rango de períodos tiene un único elemento; `maxCount = 1`; la barra ocupa el 100% de `MaxBarHeight`.
- **Eventos sin `ScheduledAt`**: se excluyen del conteo (`Where(e => e.ScheduledAt.HasValue)`) — en la práctica, todo evento en `GetEventHistoryAsync` (Closed/Cancelled) debería tener `ScheduledAt`, pero se filtra defensivamente por si un evento fue cancelado antes de programarse.
- **Cambio de filtro/granularidad**: recalcula `Buckets` vía `[NotifyPropertyChangedFor]`, sin nueva consulta a la base de datos — los datos ya están en `_allHistory`.
- **Reapertura del diálogo**: como el VM es singleton, `LoadAsync()` se vuelve a ejecutar cada vez que se abre (refresca `_allHistory` por si hubo cambios desde la última vez), preservando la `Granularity`/`OnlyCompleted` seleccionados previamente — mismo comportamiento que `EventDamageReportVm`.

## Testing strategy

- Pruebas unitarias sobre la lógica de agrupación de `EventStatsVm` (la parte no trivial): dado un conjunto de `GuildEvent` con distintas `ScheduledAt`/`Status`, verificar que:
  - `Buckets` genera el rango continuo correcto para cada granularidad (incluyendo huecos en cero)
  - el filtro `OnlyCompleted` excluye correctamente los `Cancelled`
  - `HistogramBucketVm.BarHeight` se escala correctamente contra el máximo del conjunto
- Verificación manual en UI: abrir el diálogo desde el botón nuevo, alternar entre Día/Semana/Mes y el filtro, confirmar que el histograma se redibuja y que el estado vacío aparece cuando corresponde.
