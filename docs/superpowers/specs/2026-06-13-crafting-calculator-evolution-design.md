# Crafting Calculator Evolution — Design Spec

**Date:** 2026-06-13  
**Status:** Approved  
**Scope:** Evolve the crafting calculator from a single-mode material calculator into a 3-mode production planning tool with a unified calculation engine.

---

## 1. Problem Statement

The current `CalculadoraSvm` (1,100+ lines) answers only one question: "Quiero fabricar X unidades, ¿qué necesito?" The user needs to answer two additional questions from the same tool:

- "Tengo estos materiales, ¿cuánto puedo fabricar?"
- "Tengo esta plata, ¿cuánto puedo fabricar?"

Additionally, the current UI lacks ROI%, margin%, focus comparison (with vs. without), and a visible purchase summary.

---

## 2. Goals

- Add 3 calculation modes with distinct UIs sharing a single engine
- Add ROI, margin, and with/without focus comparison to all modes
- Add sticky results bar always visible at the top
- Reduce `CalculadoraSvm` to a thin coordinator (~150 lines) by extracting state into sub-VMs
- Keep the engine 100% independent of WPF — testable in xUnit
- Preserve all existing functionality exactly

---

## 3. Architecture: Layer Overview

```
┌─────────────────────────────────────────────────────┐
│  PRESENTATION  (MainApp / WPF)                      │
│                                                     │
│  CalculadoraSvm  — coordinator only (~150 lines)    │
│  ├── CraftingSharedStateVm   (item, recipe, city)   │
│  ├── QuantityModeVm                                 │
│  ├── InventoryModeVm                                │
│  ├── BudgetModeVm                                   │
│  └── StickyResultsBarVm                             │
│                                                     │
│  CalculadoraS.xaml + QuantityModeV, InventoryModeV, │
│  BudgetModeV, StickyResultsBarV                     │
└────────────────┬────────────────────────────────────┘
                 │  immutable DTOs
┌────────────────▼────────────────────────────────────┐
│  APPLICATION  (AlbionApp.Application)               │
│                                                     │
│  CalculateByQuantityUseCase                         │
│  CalculateByInventoryUseCase                        │
│  CalculateByBudgetUseCase                           │
│       all call → CraftingKernel (internal)          │
└────────────────┬────────────────────────────────────┘
                 │
┌────────────────▼────────────────────────────────────┐
│  DOMAIN  (AlbionApp.Domain)                         │
│                                                     │
│  ReturnRateCalculator, FameCalculator  (no change)  │
│  IRecipe, CraftingCityData, etc.       (no change)  │
└─────────────────────────────────────────────────────┘

Note: CraftingKernel lives in AlbionApp.Application.UseCases.Crafting as an
internal sealed class — it uses Application-layer records (KernelRequest,
KernelResult) and is not promoted to Domain.
```

---

## 4. Calculation Engine

### 4.1 CraftingKernel (extraction)

The pure math currently inside `CalculateCraftingCostUseCase` (focus, RRR, BuildLines) is extracted into an internal static class. No new public API — the 3 UseCases call it directly.

```csharp
internal static class CraftingKernel
{
    internal static KernelResult Compute(KernelRequest r);
}

internal sealed record KernelRequest(
    IRecipe Recipe,
    int Quantity,                          // already resolved by the UseCase
    decimal ReturnRate,                    // already computed
    IReadOnlyList<IngredientStock> Stock); // price per ingredient

internal sealed record KernelResult(
    IReadOnlyList<MaterialCostLine> Lines,
    decimal TotalCost);
// MaterialCostLine is the existing record — unchanged
```

### 4.2 Mode: TargetQuantity

**Input:** explicit quantity from user.  
**Resolves:** nothing — quantity is the input.  
**Calls:** kernel with that quantity.

```csharp
record QuantityRequest(
    IRecipe Recipe, int Quantity, CraftingCityData? City,
    bool UseFocus, decimal JournalBonus, decimal HideoutBonus,
    decimal HideoutSpecialistBonus, IReadOnlyList<AggregatedBonus> AchievementBonuses,
    IReadOnlyList<IngredientStock> Stock,
    decimal? TransmutationSilverPerUnit);

record QuantityResult(
    IReadOnlyList<MaterialCostLine> Lines,
    decimal TotalCost, decimal ReturnRate,
    int? FocusPerCraft, int? TotalFocusCost, int FocusReductionPercent,
    int? FocusSpecialistSaving,
    // NEW fields:
    decimal ProfitLoss, decimal Roi, decimal Margin,
    decimal ProfitLossNoFocus, decimal RoiNoFocus,
    double FameBase, double FamePremium);
```

`Roi = ProfitLoss / TotalInvestment`. `Margin = ProfitLoss / SalePrice`.  
`ProfitLossNoFocus` / `RoiNoFocus`: same calculation with `UseFocus = false` (second kernel call, cheap).

### 4.3 Mode: AvailableInventory

**Input:** owned quantity per ingredient.  
**Resolves:** `MaxCraftable = floor( min_i( owned[i] / effectiveRequired[i] ) )`  
where `effectiveRequired[i]` accounts for RRR.  
**Calls:** kernel with `MaxCraftable` to get consumed quantities.

```csharp
record InventoryRequest(
    IRecipe Recipe, IReadOnlyList<IngredientStock> Owned,
    CraftingCityData? City, bool UseFocus, decimal JournalBonus,
    decimal HideoutBonus, decimal HideoutSpecialistBonus,
    IReadOnlyList<AggregatedBonus> AchievementBonuses,
    decimal SalePrice);   // needed to compute profit

record InventoryResult(
    int MaxCraftable,
    string BottleneckItemId,
    IReadOnlyList<InventoryLine> Lines,
    // Same financial outputs as QuantityResult:
    decimal TotalCost, decimal ReturnRate,
    decimal ProfitLoss, decimal Roi, decimal Margin,
    decimal ProfitLossNoFocus, decimal RoiNoFocus,
    int? FocusPerCraft, int? TotalFocusCost,
    double FameBase, double FamePremium);

record InventoryLine(
    string ItemId,
    int OwnedQty, int ConsumedQty, int LeftoverQty,
    decimal UnitPrice);
```

`BottleneckItemId` = ingredient whose `owned[i] / required[i]` is smallest.

### 4.4 Mode: Budget

**Input:** total budget in silver.  
**Resolves:** `MaxCraftable = floor( budget / CostPerItem )` where `CostPerItem` comes from a single kernel call with `qty=1`.  
**Calls:** kernel a second time with `MaxCraftable`.

```csharp
record BudgetRequest(
    IRecipe Recipe, decimal Budget,
    IReadOnlyList<IngredientStock> Prices,
    CraftingCityData? City, bool UseFocus, decimal JournalBonus,
    decimal HideoutBonus, decimal HideoutSpecialistBonus,
    IReadOnlyList<AggregatedBonus> AchievementBonuses,
    decimal SalePrice);

record BudgetResult(
    int MaxCraftable,
    IReadOnlyList<MaterialCostLine> Lines,
    decimal InvestmentUsed, decimal RemainingBudget,
    decimal ProfitLoss, decimal Roi, decimal Margin,
    decimal ProfitLossNoFocus, decimal RoiNoFocus,
    int? FocusPerCraft, int? TotalFocusCost,
    double FameBase, double FamePremium);
```

---

## 5. ViewModel Architecture

### 5.1 CraftingSharedStateVm

Owns all state that is identical across the 3 modes. Raises a single `InputChanged` event when any property mutates. The 3 ModeVms subscribe to it.

**Properties (migrated from current `CalculadoraSvm`):**
- `SelectedItem`, `ItemBonuses`, `_rawItemBonuses`
- `RecipeOptions`, `RecipeIndex`, `CurrentRecipe`, carousel computed properties
- `AvailableCities`, `SelectedCityOption`
- `UseFocus`, `UsePremium`
- `SelectedHideoutLevel`, `UseSpecialistBonus`
- `SelectedJournalBonus`
- `TransmutationSilverPerUnit`
- `_rawRecipes`, `_ingredientCts`

**Commands:** `BuscarItemAsync`, `LimpiarItem`, `PreviousRecipe`, `NextRecipe`, `CargarRecursos`

**Persistence:** same `[Persist]` / `RestoreTabState` logic as today, moved here.

### 5.2 QuantityModeVm

**Input:** `QuantityToCraft` (editable int).  
**Ingredient table columns:** Necesito (computed) | Precio (editable) | Total (computed).  
**Also owns:** `CraftItemRow`, `JournalRow`, `EffectiveReturnRate`, `HideoutFocusSaving`.  
**New:** `Roi`, `Margin`, `FocusComparisonVm` (exposes with/without focus side-by-side), `PurchaseSummary` (consolidated buy list).

Recalculates when: `QuantityToCraft` changes, any ingredient price changes, `SharedState.InputChanged` fires.

### 5.3 InventoryModeVm

**Input:** `OwnedQty` per ingredient row (editable int per row).  
**Ingredient table columns:** Tengo (editable) | Precio (editable) | Se consume (computed) | Sobra (computed).  
**Results:** `MaxCraftable` (computed from owned quantities), `BottleneckItemId`, plus same financial block as Quantity mode.

Recalculates when: any `OwnedQty` changes, any price changes, `SharedState.InputChanged` fires.

### 5.4 BudgetModeVm

**Input:** `Budget` (editable decimal).  
**Ingredient table columns:** Necesito (computed) | Precio (editable) | Total (computed) — same as Quantity.  
**Results:** `MaxCraftable` (computed from budget ÷ cost/item), `InvestmentUsed`, `RemainingBudget`, plus same financial block.

Recalculates when: `Budget` changes, any price changes, `SharedState.InputChanged` fires.

### 5.5 StickyResultsBarVm

Observes `CalculadoraSvm.ActiveMode` and exposes the headline numbers of the active mode:

| Mode | Metric 1 | Metric 2 | Metric 3 | Metric 4 |
|------|----------|----------|----------|----------|
| Quantity | Ganancia neta | ROI | Cantidad | Fama base |
| Inventory | Ganancia neta | ROI | Máx. fabricables | Fama base |
| Budget | Ganancia neta | ROI | Máx. fabricables | Fama base |

### 5.6 CalculadoraSvm (coordinator)

After refactor, owns only:
- `ActiveMode` (enum: `Quantity`, `Inventory`, `Budget`)
- References to `SharedState`, `QuantityModeVm`, `InventoryModeVm`, `BudgetModeVm`, `StickyResultsBarVm`
- `ExportRequested` event (PNG export stays here — requires visual tree access)
- `TabTitle` (delegates to `SharedState.SelectedItem`)
- `RestoreTabState` / persistence coordination

---

## 6. UI Layout

### 6.1 Structure (top to bottom)

```
┌──────────────────────────────────────────┐
│  STICKY RESULTS BAR  (always visible)    │
│  Ganancia | ROI | Cantidad | Fama base   │
├──────────────────────────────────────────┤
│  SHARED PARAMS (same in all 3 modes)     │
│  Card 1: Item selector + DB bonuses      │
│  Card 2a: Recipe carousel                │
│  Card 2b: City, Journal, Focus, Premium, │
│           Hideout level/specialist,      │
│           Transmutation silver           │
├──────────────────────────────────────────┤
│  MODE SELECTOR  📦 Cantidad | 🎒 Inv | 💰 Pres │
├──────────────────────────────────────────┤
│  MODE-SPECIFIC CONTENT (scrollable)      │
│  Input field (qty / owned / budget)      │
│  Ingredient table (mode-specific cols)   │
│  Results block (same in all 3 modes)     │
│  Focus comparison panel                  │
└──────────────────────────────────────────┘
```

### 6.2 Ingredient table columns by mode

| Mode | Col 1 | Col 2 | Col 3 | Col 4 |
|------|-------|-------|-------|-------|
| Quantity | Necesito (computed) | Precio (edit) | Total (computed) | — |
| Inventory | Tengo (edit, purple) | Precio (edit) | Se consume (computed) | Sobra (computed, green) |
| Budget | Necesito (computed) | Precio (edit) | Total (computed) | — |

### 6.3 Results block (identical in all 3 modes)

```
Inversión total      173.7M ₪
Valor de venta       410M ₪
─────────────────────────────
Ganancia neta        +12.5M ₪   ← green
ROI                  34%         ← green
Margen               7.2%
─────────────────────────────
Fama base / premium  1.2M / 1.8M
Foco total           48,000
```

### 6.4 Focus comparison panel

Collapsible, below the results block. Shows two columns side by side:

```
[ Sin foco ]          [ ✓ Con foco ]  ← border highlight
Ganancia  +8.2M       Ganancia  +12.5M
ROI       22%         ROI       34%

El foco aporta +4.3M ₪ · diferencia +52%
```

Active column is highlighted when `UseFocus = true`.

### 6.5 Sticky bar adaptation

The sticky bar shows the same 4 metrics in all modes. In Inventory and Budget modes, "Cantidad" slot shows `MaxCraftable` instead of `QuantityToCraft`.

### 6.6 UX improvements (all modes)

- No horizontal dividers between every row — only before section headers
- More whitespace between sections
- ROI and Margen displayed with color indicators (green/red)
- Bottleneck highlighted in orange in Inventory mode

---

## 7. Bidirectionality

No circular bindings. Each ModeVm owns a distinct input field. When that field changes, the ModeVm recalculates its own outputs. The 3 ModeVms do not write to each other.

`SharedState.InputChanged` → all 3 ModeVms recalculate independently (fan-out, no loop).

The existing `_suppressSave` pattern in `RestoreTabState` is preserved in `CraftingSharedStateVm`.

---

## 8. Persistence

- Per-tab state: `QuantityToCraft` (Quantity mode), `Budget` (Budget mode), `OwnedQty` per ingredient (Inventory mode — optional, can start as session-only).
- Global state: `UsePremium` (unchanged).
- Ingredient prices: shared across modes (same `AppConfigService.Calculator.IngredientPrices`).

---

## 9. What Does NOT Change

- `ReturnRateCalculator` — no changes
- `FameCalculator` — no changes
- `MaterialCostLine` record — no changes
- `IngredientStock` record — no changes
- `CraftingCityData`, `IRecipe`, `RecipeVm` — no changes
- `ItemBaseVm`, `ItemBaseVmFactory` — no changes
- `WorkspaceVm` / `WorkspacePersistenceService` — minimal: add `ActiveMode` to `CalculatorTabState`
- Export PNG flow — no changes
- Auto-price and price dialog — migrated to `QuantityModeVm` (or shared utility)

---

## 10. Risks

| Risk | Mitigation |
|------|-----------|
| SharedState event fan-out triggers 3 simultaneous recalculations on every keystroke | Debounce `InputChanged` in SharedStateVm (50ms) using a CancellationToken pattern already used for image loads |
| Budget solver loops if `CostPerItem = 0` (no prices set) | Guard: if `TotalCost == 0` after first kernel call, `MaxCraftable = 0` |
| InventoryModeVm persisting owned quantities can grow unbounded | Session-only in v1 — add optional persistence in v2 |
| `CalculadoraSvm` split breaks `WorkspacePersistenceService` | `RestoreTabState` signature stays on `CalculadoraSvm` — it delegates internally to `SharedState` and `QuantityModeVm` |
| Existing `[Persist]` attribute on `UseFocus` / `UseSpecialistBonus` moves to SharedStateVm | Same `LoadPersistedProperties(_store)` call — just moves to SharedStateVm constructor |

---

## 11. Phased Implementation

### Phase 1 — Engine extraction + Quantity mode improvements
1. Extract `CraftingKernel` from `CalculateCraftingCostUseCase`
2. Rename / wrap existing UseCase as `CalculateByQuantityUseCase` with new `QuantityResult` (adds `Roi`, `Margin`, `ProfitLossNoFocus`, `RoiNoFocus`)
3. Add `StickyResultsBarVm` and `StickyResultsBarV` (Quantity mode only)
4. Add focus comparison panel to `QuantityModeVm`
5. Existing `CalculadoraSvm` stays mostly intact — just consumes new result fields

### Phase 2 — ViewModel split
6. Extract `CraftingSharedStateVm` from `CalculadoraSvm`
7. Extract `QuantityModeVm` from `CalculadoraSvm`
8. `CalculadoraSvm` becomes coordinator
9. All tests and existing behavior preserved

### Phase 3 — Inventory mode
10. `CalculateByInventoryUseCase` + `InventoryResult`
11. `InventoryModeVm` + `InventoryModeV`
12. Mode selector tabs wired up (Quantity / Inventory)

### Phase 4 — Budget mode
13. `CalculateByBudgetUseCase` + `BudgetResult`
14. `BudgetModeVm` + `BudgetModeV`
15. Mode selector extended to 3 tabs
16. `ActiveMode` added to `CalculatorTabState` persistence
