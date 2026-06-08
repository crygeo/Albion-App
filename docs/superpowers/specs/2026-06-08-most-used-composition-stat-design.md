# Eventos: Diálogo de Estadísticas — Composición más usada

**Date:** 2026-06-08
**Project:** AlbionApp (MainApp/src/5Events)
**Status:** Approved

---

## Overview

Se añade una nueva tarjeta destacada **"Composición más usada"** dentro del diálogo existente **"Estadísticas de eventos"** (`EventStatsDialogV`/`EventStatsV`), junto al histograma ya implementado. Muestra, en formato top-1, la `BuildGroup` (plantilla de composición, p. ej. "AVA x10") que más veces fue usada en eventos activados, junto con el conteo de usos — p. ej. **"AVA x10 — usada 14 veces"**.

El objetivo es responder "¿qué composición usamos más?" de un vistazo, contando solo eventos que llegaron a activarse (se excluyen las plantillas en estado `Draft` que nunca se programaron). La estadística reutiliza el filtro existente "Solo completados"/"Todos" de `EventStatsVm` (`OnlyCompleted`), pero —dado que aquí "uso" incluye eventos en curso, no solo cerrados— la rama "Todos" se interpreta de forma más amplia que en el histograma: incluye también los eventos activos en progreso (`Preparation`/`Running`/`Paused`/`Finished`), no solo `Closed`+`Cancelled`.

Para soportar esto sin una segunda consulta a la base de datos, `EventStatsVm.LoadAsync()` cambia su fuente de datos de `GetEventHistoryAsync()` (solo `Closed`/`Cancelled`) a `GetEventsAsync()` (todos los eventos, incl. `Draft`, con `BuildGroup` precargado). Ambas estadísticas —histograma y composición más usada— se derivan de esta única lista mediante filtros distintos; el comportamiento del histograma no cambia.

**Files changed:**
- `MainApp/src/5Events/EventStatsVm.cs` (cambia la fuente de datos de `LoadAsync`; añade `MostUsedComposition`/`HasMostUsedComposition`)
- `MainApp/src/5Events/EventStatsV.xaml` (nueva fila con la tarjeta de composición más usada)

**Files created:**
- `MainApp/src/5Events/CompositionUsageVm.cs`

**No changes to:** entidades, esquema de BD, migraciones, comandos/diálogos/registro existentes (`EventsVm`, `DialogDefaults`, `App.xaml.cs`, `EventStatsDialogV` permanecen igual — la estadística vive dentro del mismo diálogo y VM ya conectados).

---

## Componentes y arquitectura

```
EventStatsVm
 ├─ LoadAsync()
 │   └─ BuildService.GetEventsAsync()         (TODOS los eventos, incl. Draft, BuildGroup precargado)
 │       └─ _allEvents
 ├─ Buckets            ← filtra _allEvents a Closed/Cancelled, luego aplica OnlyCompleted (sin cambios de comportamiento)
 └─ MostUsedComposition ← filtra _allEvents a "no-Draft" (u "Closed" si OnlyCompleted),
                          agrupa por BuildGroup.Name, toma el top-1 por conteo
```

### Cambio en `EventStatsVm.LoadAsync` — fuente de datos compartida

```csharp
private List<GuildEvent> _allEvents = [];

public async Task LoadAsync()
{
    _allEvents = await _buildService.GetEventsAsync();
    OnPropertyChanged(nameof(Buckets));
    OnPropertyChanged(nameof(HasData));
    OnPropertyChanged(nameof(MostUsedComposition));
    OnPropertyChanged(nameof(HasMostUsedComposition));
}
```

`_allHistory` se renombra/reemplaza por `_allEvents`. El campo ya no se limita a `Closed`/`Cancelled`: contiene **todos** los estados, incluyendo `Draft` y los activos en curso.

### `Buckets` — ajuste para preservar el comportamiento actual

El histograma debe seguir contando exactamente lo mismo que antes (universo `Closed`+`Cancelled`, igual que devolvía `GetEventHistoryAsync`). Como ahora `_allEvents` incluye más estados, se añade un filtro explícito al inicio de la cadena:

```csharp
var filtered = _allEvents
    .Where(e => e.Status == EventStatus.Closed || e.Status == EventStatus.Cancelled)
    .Where(e => e.ScheduledAt.HasValue)
    .Where(e => !OnlyCompleted || e.Status == EventStatus.Closed)
    .ToList();
```

(Se añadió únicamente la primera línea `.Where(e => e.Status == EventStatus.Closed || e.Status == EventStatus.Cancelled)`; el resto de `Buckets`, `BuildPeriodRange`, `PeriodKey`, etc. permanece sin cambios.)

### `MostUsedComposition` — nueva propiedad calculada

```csharp
public CompositionUsageVm? MostUsedComposition
{
    get
    {
        var counted = _allEvents
            .Where(e => OnlyCompleted ? e.Status == EventStatus.Closed
                                       : e.Status != EventStatus.Draft)
            .Where(e => e.BuildGroup is not null)
            .GroupBy(e => e.BuildGroup!.Name)
            .Select(g => new CompositionUsageVm(g.Key, g.Count()))
            .OrderByDescending(c => c.UsageCount)
            .ThenBy(c => c.Name)
            .FirstOrDefault();

        return counted;
    }
}

public bool HasMostUsedComposition => MostUsedComposition is not null;
```

**Semántica del filtro `OnlyCompleted` para esta estadística** (deliberadamente más amplia que para el histograma):

| Valor de `OnlyCompleted` | Universo contado |
|---|---|
| `true` ("Solo completados") | Solo `Closed` — igual criterio que el histograma |
| `false` ("Todos") | Cualquier estado salvo `Draft`: `Preparation`, `Running`, `Paused`, `Finished`, `Closed`, `Cancelled` — más amplio que el histograma (que en "Todos" se limita a `Closed`+`Cancelled`) |

Esto es intencional: la etiqueta del filtro es compartida ("Solo completados" / "Todos"), pero cada visualización la aplica al universo que tiene sentido para lo que mide. Para el histograma, "actividad histórica" son los eventos que ya terminaron (cerrados o cancelados). Para "composición más usada", un evento en curso ya cuenta como un "uso" de esa composición, así que "Todos" lo incluye.

Se añade `[NotifyPropertyChangedFor(nameof(MostUsedComposition))]` y `[NotifyPropertyChangedFor(nameof(HasMostUsedComposition))]` al `[ObservableProperty] OnlyCompleted` existente (junto a las notificaciones ya presentes para `Buckets`/`HasData`). **No** depende de `Granularity` — no se añade notificación ahí.

**Desempate:** si dos o más composiciones tienen el mismo conteo máximo, se elige la primera alfabéticamente (`ThenBy(c => c.Name)`). Esto garantiza un resultado determinista y estable entre recargas/cambios de filtro — sin necesidad de mostrar un top-N ni indicar empates en la UI.

### `CompositionUsageVm`

VM de presentación inmutable, mismo estilo que `HistogramBucketVm` (constructor primario C# 12, propiedades `get`-only calculadas una sola vez):

```csharp
namespace Albion_App._5Events;

public sealed class CompositionUsageVm(string name, int usageCount)
{
    public string Name        { get; } = name;
    public int    UsageCount  { get; } = usageCount;
    public string UsageDisplay => UsageCount == 1 ? "usada 1 vez" : $"usada {UsageCount} veces";
}
```

---

## Vista — `EventStatsV`

Se inserta una nueva fila `Auto` entre la fila de filtros y la fila del histograma. Los `Grid.Row` de los elementos existentes (`TextBlock` de estado vacío y `ScrollViewer` del histograma) pasan de `1` a `2`:

```
Grid.RowDefinitions: Auto (filtros) | Auto (composición más usada) | * (histograma / estado vacío)
```

La tarjeta — `Border` compacto con icono, nombre y conteo, visible solo cuando hay datos:

```xml
<Border Grid.Row="1"
        Margin="0 0 0 12" Padding="12 8"
        Background="{DynamicResource MaterialDesign.Brush.Card.Background}"
        BorderBrush="{DynamicResource MaterialDesign.Brush.Separator}"
        BorderThickness="1" CornerRadius="6">
    <Border.Style>
        <Style TargetType="Border">
            <Style.Triggers>
                <DataTrigger Binding="{Binding HasMostUsedComposition}" Value="False">
                    <Setter Property="Visibility" Value="Collapsed"/>
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </Border.Style>
    <DockPanel>
        <md:PackIcon Kind="Crown"
                     Width="22" Height="22"
                     Foreground="{DynamicResource MaterialDesign.Brush.Primary}"
                     VerticalAlignment="Center"
                     Margin="0 0 12 0"/>
        <StackPanel VerticalAlignment="Center">
            <TextBlock Text="Composición más usada" FontSize="11" Opacity="0.6"/>
            <TextBlock Text="{Binding MostUsedComposition.Name}" FontSize="15" FontWeight="Bold"/>
        </StackPanel>
        <TextBlock Text="{Binding MostUsedComposition.UsageDisplay}"
                   DockPanel.Dock="Right"
                   VerticalAlignment="Center"
                   Opacity="0.7"/>
    </DockPanel>
</Border>
```

No se requieren cambios en las alturas fijas del diálogo (`56/420/56` en `EventStatsDialogV`): la tarjeta es compacta (~48px con márgenes) y la fila `*` del histograma absorbe el espacio restante sin problema — el contenido renderizado del histograma (texto de conteo + barra de 140px + etiqueta) ocupa ~190px, muy por debajo de los ~370px disponibles tras restar la nueva fila.

---

## Casos límite

- **Ningún evento contado tiene `BuildGroup` asignado**: `MostUsedComposition` es `null`, `HasMostUsedComposition` es `false`, la tarjeta queda oculta (`Visibility = Collapsed`). No se muestra ningún mensaje de "sin datos" para esta tarjeta — simplemente no aparece, manteniendo el diálogo limpio mientras no hay nada que mostrar (el histograma ya tiene su propio estado vacío independiente).
- **Empate en el conteo de usos**: se resuelve alfabéticamente por nombre de composición (`ThenBy(c => c.Name)`), garantizando un resultado estable y determinista entre recargas.
- **Singular vs. plural**: `CompositionUsageVm.UsageDisplay` distingue "usada 1 vez" de "usada N veces".
- **Cambio del filtro "Solo completados"/"Todos"**: recalcula `MostUsedComposition` vía `[NotifyPropertyChangedFor]`, sin nueva consulta — los datos ya están en `_allEvents`. La tarjeta puede aparecer/desaparecer (mostrarse u ocultarse) si el cambio de universo hace que `MostUsedComposition` pase de tener valor a `null` o viceversa (p. ej., si solo hay composiciones usadas en eventos activos pero ninguna en eventos `Closed`, la tarjeta se oculta al activar "Solo completados").
- **Cambio de granularidad (Día/Semana/Mes)**: no afecta a esta tarjeta — `MostUsedComposition` es independiente de `Granularity` (no recibe `[NotifyPropertyChangedFor]` desde esa propiedad).
- **Reapertura del diálogo**: el VM es singleton; `LoadAsync()` se reejecuta cada vez que se abre el diálogo (refresca `_allEvents`), preservando `OnlyCompleted`/`Granularity` seleccionados previamente — mismo comportamiento ya establecido para el histograma.

## Testing strategy

- Pruebas unitarias sobre la lógica de agregación de `MostUsedComposition` (la parte no trivial): dado un conjunto de `GuildEvent` con distintos `Status`/`BuildGroup`, verificar que:
  - se excluyen correctamente los eventos `Draft` y los que no tienen `BuildGroup` asignado
  - con `OnlyCompleted = true`, solo se cuentan eventos `Closed`
  - con `OnlyCompleted = false`, se cuentan todos los estados salvo `Draft` (incluyendo `Preparation`/`Running`/`Paused`/`Finished`/`Cancelled`)
  - el desempate selecciona la composición con nombre alfabéticamente menor
  - `CompositionUsageVm.UsageDisplay` produce "usada 1 vez" vs. "usada N veces" correctamente
  - cuando no hay eventos calificados con `BuildGroup`, `MostUsedComposition` es `null` y `HasMostUsedComposition` es `false`
- Verificación manual en UI: abrir el diálogo de estadísticas, confirmar que la tarjeta "Composición más usada" muestra el nombre y conteo correctos, alternar "Solo completados"/"Todos" y verificar que el valor (y la visibilidad de la tarjeta) se actualiza coherentemente, y confirmar que el histograma sigue funcionando exactamente igual que antes del cambio de fuente de datos.
