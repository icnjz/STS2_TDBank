using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Random;
using TDBank.TDBankCode.Compatibility;

namespace TDBank.TDBankCode.Banking;

public enum ButtRiskOutcome
{
    Normal = 0,
    Unpaid = 1,
    Hemorrhage = 2,
}

public readonly record struct ButtRiskProfile(
    int UnpaidPercent,
    int HemorrhagePercent)
{
    public int NormalPercent =>
        100 - UnpaidPercent - HemorrhagePercent;
}

public static class KkCompoundService
{
    public const int KidneyHpCost = 10;
    public const int KidneyGoldValue = 200;
    public const int ButtHpCost = 5;
    public const int ButtGoldValue = 50;

    public static int GetKidneyHpCost(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        return AscensionBankBenefits.For(player).KidneyHpCost;
    }

    public static int GetKidneyGoldValue(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        return AscensionBankBenefits.For(player).KidneyGoldValue;
    }

    public static int GetButtHpCost(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        return AscensionBankBenefits.For(player).ButtHpCost;
    }

    public static int GetButtGoldValue(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        return AscensionBankBenefits.For(player).ButtGoldValue;
    }

    public static int GetButtGoldValueForNextSale(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        return CalculateButtGoldValue(
            GetButtGoldValue(player),
            BankStateStore.Get(player).ButtSalesCount);
    }

    public static int GetButtHpCostForNextSale(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        return CalculateButtHpCost(
            GetButtHpCost(player),
            BankStateStore.Get(player).ButtSalesCount);
    }

    public static int CalculateButtHpCost(
        int baseHpCost,
        int completedSales)
    {
        if (baseHpCost <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(baseHpCost));
        }
        if (completedSales < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(completedSales));
        }

        if (completedSales < 3)
        {
            return baseHpCost;
        }
        if (completedSales < 6)
        {
            return checked(baseHpCost + 3);
        }
        if (completedSales < 9)
        {
            return checked(baseHpCost + 7);
        }

        return checked(baseHpCost + 12 + (completedSales - 9) * 2);
    }

    public static int CalculateButtGoldValue(
        int baseGoldValue,
        int completedSales)
    {
        if (baseGoldValue <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(baseGoldValue));
        }
        if (completedSales < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(completedSales));
        }

        if (completedSales < 3)
        {
            return baseGoldValue;
        }

        int percent = completedSales switch
        {
            < 6 => 60,
            < 9 => 35,
            _ => 15,
        };
        return Math.Max(10, checked(baseGoldValue * percent / 100));
    }

    public static int GetMaximumSafeKidneyCount(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        Creature creature = player.Creature;
        int hpCost = GetKidneyHpCost(player);
        int goldValue = GetKidneyGoldValue(player);
        return Math.Max(
            0,
            Math.Min(
                Math.Min(
                    (creature.CurrentHp - 1) / hpCost,
                    (creature.MaxHp - 1) / hpCost),
                int.MaxValue / goldValue));
    }

    public static bool CanSafelySellButt(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        int hpCost = GetButtHpCostForNextSale(player);
        int maximumCost = BankStateStore.Get(player).ButtSalesCount >= 3
            ? checked(hpCost * 2)
            : hpCost;
        return player.Creature.CurrentHp > maximumCost;
    }

    public static ButtRiskProfile GetButtRiskProfile(
   int completedSales,
   int ascensionLevel)
    {
        if (completedSales < 3)
        {
            return default;
        }

        int riskIndex = Math.Min(completedSales - 3, 3);
        int unpaidPercent = riskIndex switch
        {
            0 => 20,
            1 => 30,
            2 => 40,
            _ => 50,
        };
        int baseHemorrhagePercent = riskIndex switch
        {
            0 => 10,
            1 => 15,
            2 => 20,
            _ => 25,
        };
        int multiplierPercent = Math.Clamp(ascensionLevel, 0, 10) switch
        {
            <= 2 => 100,
            <= 4 => 85,
            <= 6 => 75,
            <= 8 => 65,
            _ => 50,
        };
        int hemorrhagePercent =
            (baseHemorrhagePercent * multiplierPercent + 50) / 100;
        return new ButtRiskProfile(
            unpaidPercent,
            hemorrhagePercent);
    }

    public static ButtRiskOutcome ResolveButtRiskOutcome(
   int completedSales,
   int ascensionLevel,
   int roll)
    {
        if (roll is < 0 or >= 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(roll),
                "The TD Bank risk roll must be in [0, 100).");
        }

        ButtRiskProfile profile = GetButtRiskProfile(
            completedSales,
            ascensionLevel);
        if (roll < profile.UnpaidPercent)
        {
            return ButtRiskOutcome.Unpaid;
        }
        if (roll
            < profile.UnpaidPercent + profile.HemorrhagePercent)
        {
            return ButtRiskOutcome.Hemorrhage;
        }

        return ButtRiskOutcome.Normal;
    }

    public static ButtRiskOutcome GetButtRiskOutcomeForNextSale(
   Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        return ResolveButtRiskOutcome(
            player,
            BankStateStore.Get(player).ButtSalesCount);
    }

    public static async Task<BankOperationResult> SellKidneys(
        Player player,
        int quantity)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (quantity <= 0)
        {
            return BankOperationResult.Fail(BankErrorCode.InvalidAmount);
        }

        int hpCost;
        int proceeds;
        try
        {
            hpCost = checked(
                quantity * GetKidneyHpCost(player));
            proceeds = checked(
                quantity * GetKidneyGoldValue(player));
        }
        catch (OverflowException)
        {
            return BankOperationResult.Fail(
                BankErrorCode.ArithmeticOverflow);
        }

        Creature creature = player.Creature;
        int oldCurrentHp = creature.CurrentHp;
        int oldMaxHp = creature.MaxHp;
        if (oldCurrentHp <= hpCost || oldMaxHp <= hpCost)
        {
            return BankOperationResult.Fail(
                BankErrorCode.InsufficientHealth);
        }

        BankOperationResult preview = BankService.PreviewDepositGold(
            player,
            proceeds,
            GoldIncomeSource.OrganSale);
        if (!preview.Success)
        {
            return preview;
        }

        try
        {
            await SetCurrentAndMaxHpWithoutGameplayTriggers(
                creature,
                oldCurrentHp - hpCost,
                oldMaxHp - hpCost);
        }
        catch
        {
            await RestoreHealthBestEffort(
                creature,
                oldCurrentHp,
                oldMaxHp);
            throw;
        }

        BankOperationResult deposit = BankService.DepositGold(
            player,
            proceeds,
            GoldIncomeSource.OrganSale);
        if (deposit.Success)
        {
            return deposit;
        }

        await RestoreHealthBestEffort(
            creature,
            oldCurrentHp,
            oldMaxHp);
        return deposit;
    }

    public static async Task<BankOperationResult> SellButt(
        Player player,
        ButtRiskOutcome? authoritativeOutcome = null)
    {
        ArgumentNullException.ThrowIfNull(player);
        AccountSnapshot snapshot = BankService.GetSnapshot(player);
        if (!snapshot.IsAccountOpened)
        {
            return BankOperationResult.Fail(
                BankErrorCode.OperationUnavailable);
        }
        if (snapshot.ButtSalesCount == int.MaxValue)
        {
            return BankOperationResult.Fail(
                BankErrorCode.ArithmeticOverflow);
        }

        Creature creature = player.Creature;
        int oldCurrentHp = creature.CurrentHp;
        int hpCost;
        int maximumHpCost;
        try
        {
            hpCost = CalculateButtHpCost(
                GetButtHpCost(player),
                snapshot.ButtSalesCount);
            maximumHpCost = snapshot.ButtSalesCount >= 3
                ? checked(hpCost * 2)
                : hpCost;
        }
        catch (OverflowException)
        {
            return BankOperationResult.Fail(
                BankErrorCode.ArithmeticOverflow);
        }
        if (oldCurrentHp <= maximumHpCost)
        {
            return BankOperationResult.Fail(
                BankErrorCode.InsufficientHealth);
        }

        ButtRiskOutcome outcome = authoritativeOutcome
            ?? GetButtRiskOutcomeForNextSale(player);
        if (!Enum.IsDefined(outcome))
        {
            return BankOperationResult.Fail(
                BankErrorCode.InvalidAccount);
        }
        int actualHpCost = outcome == ButtRiskOutcome.Hemorrhage
            ? maximumHpCost
            : hpCost;
        int proceeds = outcome == ButtRiskOutcome.Unpaid
            ? 0
            : GetButtGoldValueForNextSale(player);
        BankOperationResult preview = proceeds == 0
            ? BankOperationResult.Ok()
            : BankService.PreviewDepositGold(
                player,
                proceeds,
                GoldIncomeSource.OrganSale);
        if (!preview.Success)
        {
            return preview;
        }

        try
        {
            await SetCurrentHpWithoutGameplayTriggers(
                creature,
                oldCurrentHp - actualHpCost);
        }
        catch
        {
            await RestoreCurrentHpBestEffort(creature, oldCurrentHp);
            throw;
        }

        BankOperationResult deposit;
        try
        {
            deposit = proceeds == 0
                ? BankOperationResult.Ok()
                : BankService.DepositGold(
                    player,
                    proceeds,
                    GoldIncomeSource.OrganSale);
        }
        catch
        {
            await RestoreCurrentHpBestEffort(creature, oldCurrentHp);
            throw;
        }
        if (!deposit.Success)
        {
            await RestoreCurrentHpBestEffort(creature, oldCurrentHp);
            return deposit;
        }

        BankOperationResult count = BankService.RecordButtSale(player);
        if (!count.Success)
        {



            throw new InvalidOperationException(
                $"TD Bank could not record a completed butt sale: {count.Error}");
        }

        return deposit with { ButtOutcome = outcome };
    }

    private static ButtRiskOutcome ResolveButtRiskOutcome(
        Player player,
        int completedSales)
    {
        AscensionBankBenefits benefits =
            AscensionBankBenefits.For(player);
        if (completedSales < 3)
        {
            return ButtRiskOutcome.Normal;
        }

        int nextSaleOrdinal = checked(completedSales + 1);
        ulong seed = GameApiCompatibility.GetRunSeed(
            player.RunState.Rng);
        int slot = player.RunState.GetPlayerSlotIndex(player);

        Rng rng = GameApiCompatibility.CreateRng(
            seed,
            FormattableString.Invariant(
                $"td_bank_butt_risk_v1_slot_{slot}_sale_{nextSaleOrdinal}"));
        int roll = rng.NextInt(0, 100);
        return ResolveButtRiskOutcome(
            completedSales,
            benefits.AscensionLevel,
            roll);
    }

    private static async Task RestoreHealthBestEffort(
        Creature creature,
        int currentHp,
        int maxHp)
    {
        try
        {
            await RestoreCurrentAndMaxHpWithoutGameplayTriggers(
                creature,
                currentHp,
                maxHp);
        }
        catch
        {


        }
    }

    private static async Task RestoreCurrentHpBestEffort(
        Creature creature,
        int currentHp)
    {
        try
        {
            await SetCurrentHpWithoutGameplayTriggers(
                creature,
                currentHp);
        }
        catch
        {

        }
    }

    private static Task SetCurrentHpWithoutGameplayTriggers(
   Creature creature,
   int currentHp)
    {
        creature.SetCurrentHpInternal(currentHp);
        return Task.CompletedTask;
    }

    private static Task SetCurrentAndMaxHpWithoutGameplayTriggers(
        Creature creature,
        int currentHp,
        int maxHp)
    {


        creature.SetCurrentHpInternal(currentHp);
        creature.SetMaxHpInternal(maxHp);
        return Task.CompletedTask;
    }

    private static Task RestoreCurrentAndMaxHpWithoutGameplayTriggers(
        Creature creature,
        int currentHp,
        int maxHp)
    {


        creature.SetMaxHpInternal(maxHp);
        creature.SetCurrentHpInternal(currentHp);
        return Task.CompletedTask;
    }
}
