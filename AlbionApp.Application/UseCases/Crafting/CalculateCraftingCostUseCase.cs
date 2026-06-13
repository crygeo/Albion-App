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
    // ── Constantes de dominio ─────────────────────────────────────────────────

    private const string FocusReductionBonusType    = "craftingfocuscostreduction";
    private const string ReturnRateAchievementType  = ReturnRateCalculator.AchievementReturnBonusType;

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
        var focus = CalculateFocus(request, qty);

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
        var rrr = CalculateReturnRate(request);

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

    // ── Cálculo de foco ───────────────────────────────────────────────────────

    private static FocusResult CalculateFocus(CraftingCostRequest request, int qty)
    {
        if (!request.UseFocus || request.Recipe.CraftingFocus is not > 0)
            return FocusResult.None;

        // ── Fórmula oficial (forum.albiononline.com/Thread/126568) ────────────
        //
        //   Focus = baseFocus × 0.5^(exponent)
        //
        //   exponent = sum(nivel × BonusValue) / 100
        //            = SumBonus / 100
        //
        // donde SumBonus acumula la contribución de cada nodo del Destiny Board.
        // AchievementBonus.Calculate(level) = level × BonusValue → ya normalizado.
        // SumBonus() ya divide por 100 internamente.
        //
        // Los dos cálculos son INDEPENDIENTES (floor vs ceil en distinto punto):
        //   focusPerCraft  = floor(baseFocus × factor)             ← display
        //   totalFocus     = ceil(baseFocus × qty × factor / amt)  ← cálculo real

        var dbExponent  = (double)SumBonus(request, FocusReductionBonusType);
        var focusFactor = Math.Pow(0.5, dbExponent);       // 0.5^(bonusDB)

        var amountPerCraft = Math.Max(1, request.Recipe.AmountCrafted);

        // baseFocus = craftingFocus del XML directamente.
        // El valor del XML ya es el coste base en pantalla.
        // Fórmula verificada: 503 × 0.5^4 = 31.42  (T8 planks, exponent=4)
        var baseFocus = (double)request.Recipe.CraftingFocus!.Value;
        var effective = baseFocus * focusFactor;

        var focusPerCraft = Math.Max(1, (int)Math.Floor(effective));
        var totalFocus    = Math.Max(1, (int)Math.Ceiling(effective * qty / amountPerCraft));

        // ── Bono especialista del hideout ─────────────────────────────────────
        // Se suma al exponente: focusFactor = 0.5^(dbExponent + specialistBonus)
        // Ejemplo nivel 9: 503 × 0.5^(4 + 0.30) = 503 × 0.5^4.30 ≈ 25 foco
        int? specialistSaving = null;
        if (request.HideoutSpecialistBonus > 0)
        {
            var specialistExponent = dbExponent + (double)request.HideoutSpecialistBonus;
            var specialistFactor   = Math.Pow(0.5, specialistExponent);
            var specialistPerCraft = Math.Max(1, (int)Math.Floor(baseFocus * specialistFactor));

            specialistSaving  = focusPerCraft - specialistPerCraft;
            focusPerCraft     = specialistPerCraft;
            totalFocus        = Math.Max(1, (int)Math.Ceiling(
                                    baseFocus * specialistFactor * qty / amountPerCraft));
        }

        return new FocusResult(
            PerCraft:          focusPerCraft,
            Total:             totalFocus,
            ReductionPercent:  (int)Math.Round((1.0 - focusFactor) * 100),
            SpecialistSaving:  specialistSaving);
    }

    // ── Tasa de retorno ───────────────────────────────────────────────────────

    private static decimal CalculateReturnRate(CraftingCostRequest request)
    {
        var achievementBonus = request.UseFocus
            ? SumBonus(request, ReturnRateAchievementType)
            : 0m;

        return ReturnRateCalculator.Calculate(
            request.City!,
            request.CraftingCategory,
            request.UseFocus,
            achievementBonus,
            request.JournalBonus,
            request.HideoutBonus);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Suma los bonuses del tipo dado y los convierte a fraccion decimal [0, 1].
    /// AggregatedBonus.Total devuelve puntos de porcentaje (ej: 43.5 = 43.5%),
    /// por lo que se divide entre 100 para obtener 0.435.
    /// </summary>
    private static decimal SumBonus(CraftingCostRequest request, string bonusType)
        => (decimal)request.AchievementBonuses
            .Where(b => b.Type == bonusType)
            .Sum(b => b.Total) / 100m;

    // ── Value objects internos ────────────────────────────────────────────────

    private sealed record FocusResult(
        int? PerCraft,
        int? Total,
        int  ReductionPercent,
        int? SpecialistSaving = null)
    {
        public static readonly FocusResult None = new(null, null, 0);
    }
}
