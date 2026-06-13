using AlbionApp.Domain.Crafting;

namespace AlbionApp.Application.UseCases.Crafting;

/// <summary>
/// Calcula el costo completo de un ciclo de crafteo/refinado.
///
/// Responsabilidades:
///   • Costo de foco (por craft y total), con reducción del Destiny Board.
///   • Tasa de retorno (RRR) a partir de ciudad, categoría, focus y bono de diario.
///   • Cantidad neta de cada material: bruto − retornado − inventario propio,
///     garantizando el mínimo físico para iniciar al menos 1 batch.
///   • Costo total de materiales.
///
/// Sin dependencias de WPF — 100 % testeable en xUnit.
/// </summary>
public sealed class CalculateCraftingCostUseCase
{
    // ── API pública ───────────────────────────────────────────────────────────

    /// <summary>
    /// Ejecuta el cálculo y retorna un resultado inmutable.
    /// Si <see cref="CraftingCostRequest.City"/> es null, solo se calcula el foco.
    /// </summary>
    public CraftingCostResult Execute(CraftingCostRequest request)
    {
        var qty = Math.Max(1, request.Quantity);

        // Transmutación: sin foco, sin RRR, sin ciudad — solo plata + ingredientes.
        if (request.Recipe.IsTransmutation)
        {
            var silverPerUnit = request.TransmutationSilverPerUnit ?? request.Recipe.Silver ?? 0m;
            var silverCost    = silverPerUnit * qty;
            var kernelResult  = CraftingKernel.Compute(new KernelRequest(
                Recipe:      request.Recipe,
                Quantity:    qty,
                ReturnRate:  0m,
                Stock:       request.OwnedStock,
                BuyLocation: request.City?.Name ?? string.Empty));
            return new CraftingCostResult
            {
                TransmutationSilverCost = silverCost,
                Lines                   = kernelResult.Lines,
                TotalCost               = silverCost + kernelResult.TotalCost
            };
        }

        // 1. Foco ─────────────────────────────────────────────────────────────
        var focus = CraftingEngineHelpers.ComputeFocus(
            request.Recipe, request.UseFocus, qty,
            request.AchievementBonuses, request.HideoutSpecialistBonus);

        // 2. Sin ciudad: devolver resultado parcial (solo foco)
        if (request.City is null)
        {
            return new CraftingCostResult
            {
                FocusPerCraft          = focus.PerCraft,
                TotalFocusCost         = focus.Total,
                FocusReductionPercent  = focus.ReductionPercent,
                FocusSpecialistSaving  = focus.SpecialistSaving
            };
        }

        // 3. Tasa de retorno ───────────────────────────────────────────────────
        var rrr = CraftingEngineHelpers.ComputeReturnRate(
            request.City, request.CraftingCategory, request.UseFocus,
            request.JournalBonus, request.HideoutBonus, request.AchievementBonuses);

        // 4. Materiales ────────────────────────────────────────────────────────
        var kernelResult2 = CraftingKernel.Compute(new KernelRequest(
            Recipe:      request.Recipe,
            Quantity:    qty,
            ReturnRate:  rrr,
            Stock:       request.OwnedStock,
            BuyLocation: request.City!.Name));

        return new CraftingCostResult
        {
            ReturnRate            = rrr,
            FocusPerCraft         = focus.PerCraft,
            TotalFocusCost        = focus.Total,
            FocusReductionPercent = focus.ReductionPercent,
            Lines                 = kernelResult2.Lines,
            TotalCost             = kernelResult2.TotalCost
        };
    }
}
