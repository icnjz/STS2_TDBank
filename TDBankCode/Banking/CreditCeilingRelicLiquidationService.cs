using System.Reflection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
using TDBank.TDBankCode.Compatibility;

namespace TDBank.TDBankCode.Banking;

public sealed record CreditCeilingRelicLiquidationResult(
   int DebtCleared,
   int RelicsRequested,
   IReadOnlyList<string> RemovedRelicIds)
{
    public static CreditCeilingRelicLiquidationResult None { get; } =
        new(0, 0, Array.Empty<string>());

    public int RelicsRemoved => RemovedRelicIds.Count;
}

public static class CreditCeilingRelicLiquidationService
{
    private sealed record Candidate(
        RelicModel Relic,
        int InventoryIndex);

    public static int CalculateRelicsRequested(int debt)
   => CalculateRelicsRequested(debt, ascensionLevel: 0);

    public static int CalculateRelicsRequested(
        int debt,
        IRunState runState)
    {
        ArgumentNullException.ThrowIfNull(runState);
        return CalculateRelicsRequested(
            debt,
            runState.AscensionLevel);
    }

    public static int CalculateRelicsRequested(
   int debt,
   int ascensionLevel)
    {
        if (debt <= 0)
        {
            return 0;
        }

        AscensionBankBenefits benefits =
            AscensionBankBenefits.ForAscension(ascensionLevel);
        int goldPerRelic =
            benefits.RelicLiquidationGoldPerRelic;
        int rounded = Math.Max(
            1,
            (int)(((long)debt + goldPerRelic / 2L)
                / goldPerRelic));
        return Math.Min(
            rounded,
            benefits.RelicLiquidationMaximumRelics);
    }

    public static CreditCeilingRelicLiquidationResult
   LiquidatePendingCreditCeiling(
       Player player,
       IRunState runState)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(runState);

        int debt =
            BankService.GetPendingRelicLiquidationDebt(player);
        int requested = CalculateRelicsRequested(debt, runState);
        if (requested == 0)
        {
            return CreditCeilingRelicLiquidationResult.None;
        }

        List<Candidate> candidates = player.Relics
            .Select(static (relic, index) => new Candidate(relic, index))
            .Where(candidate => IsSafelySeizable(candidate.Relic))
            .OrderBy(
                candidate => candidate.Relic.Id.ToString(),
                StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Relic.FloorAddedToDeck)
            .ThenBy(candidate => candidate.InventoryIndex)
            .ToList();




        foreach (Candidate candidate in candidates)
        {
            if (!ReferenceEquals(candidate.Relic.Owner, player)
                || !player.Relics.Contains(candidate.Relic))
            {
                throw new InvalidOperationException(
                    $"TD Bank cannot seize relic {candidate.Relic.Id}: "
                    + "the candidate no longer belongs to the quoted player.");
            }
        }

        Rng rng = GameApiCompatibility.CreateRng(
            GameApiCompatibility.GetRunSeed(runState.Rng),
            $"td_bank_credit_ceiling_liquidation_v1_act_{runState.CurrentActIndex}"
            + $"_floor_{runState.TotalFloor}"
            + $"_slot_{runState.GetPlayerSlotIndex(player)}");
        rng.Shuffle(candidates);

        int take = Math.Min(requested, candidates.Count);
        var removedIds = new List<string>(take);
        for (var index = 0; index < take; index++)
        {
            RelicModel relic = candidates[index].Relic;
            RelicCmd.Remove(relic).GetAwaiter().GetResult();
            removedIds.Add(relic.Id.ToString());
        }

        BankOperationResult settlement =
            BankService.CompletePendingRelicLiquidation(
                player,
                debt);
        if (!settlement.Success || settlement.Amount != debt)
        {
            throw new InvalidOperationException(
                "TD Bank relic seizure completed but the quoted debt "
                + $"could not be acknowledged (expected {debt}, "
                + $"actual {settlement.Amount}, error {settlement.Error}).");
        }

        return new CreditCeilingRelicLiquidationResult(
            debt,
            requested,
            removedIds);
    }

    public static bool IsSafelySeizable(RelicModel relic)
    {
        ArgumentNullException.ThrowIfNull(relic);
        if (!relic.IsTradable)
        {
            return false;
        }

        MethodInfo? afterRemoved = relic.GetType().GetMethod(
            nameof(RelicModel.AfterRemoved),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return afterRemoved?.DeclaringType == typeof(RelicModel);
    }
}
