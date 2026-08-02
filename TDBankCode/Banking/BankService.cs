using MegaCrit.Sts2.Core.Entities.Gold;
using MegaCrit.Sts2.Core.Entities.Players;

namespace TDBank.TDBankCode.Banking;

public enum BankErrorCode
{
    None = 0,
    InvalidAmount = 1,
    InvalidAccount = 2,
    SameAccount = 3,
    SamePlayer = 4,
    InsufficientFunds = 5,
    CreditCardNotOpen = 6,
    CreditLimitExceeded = 7,
    InvalidCreditTier = 9,
    CreditTierNotUpgrade = 10,
    NotEligible = 11,
    AlreadyHighestCreditTier = 12,
    AlreadyProcessed = 13,
    ArithmeticOverflow = 14,
    OperationUnavailable = 15,
    CreditPermanentlyClosed = 16,
    InsufficientHealth = 17,
}

public enum GoldIncomeSource
{
    NormalGameGold = 0,
    ETransfer = 1,
    OrganSale = 2,
    SavingsInterest = 3,
    StolenGoldReturned = 4,
    Other = 5,
}

public readonly record struct BankOperationResult(
   BankErrorCode Error,
   int Amount = 0,
   int SecondaryAmount = 0,
   ButtRiskOutcome ButtOutcome = ButtRiskOutcome.Normal)
{
    public bool Success => Error == BankErrorCode.None;

    public static BankOperationResult Ok(
        int amount = 0,
        int secondaryAmount = 0,
        ButtRiskOutcome buttOutcome = ButtRiskOutcome.Normal)
        => new(
            BankErrorCode.None,
            amount,
            secondaryAmount,
            buttOutcome);

    public static BankOperationResult Fail(BankErrorCode error)
        => new(error);
}

public readonly record struct AccountSnapshot(
   int SavingsBalance,
   int SavingsPrincipal,
   int SavingsInterest,
   int SavingsTenths,
   int SavingsInterestEarnedTotal,
   CreditTier CreditTier,
   int CreditDebt,
   int CreditLimit,
   int AvailableCredit,
   int QualifyingEarned,
   bool IsBankrupt,
   int Revision,
   bool IsAccountOpened,
   int ButtSalesCount)
{
    public int CreditBalance => -CreditDebt;

    public long TotalPurchasingPower =>
        Math.Max(0L, SavingsBalance) + AvailableCredit;
}

public static class BankService
{
    public const int DebtGraceFloorCount = 3;
    public const int PoorDebtInterestBasisPoints = 2199;
    public const int MiddleClassDebtInterestBasisPoints = 2499;
    public const int TycoonDebtInterestBasisPoints = 2799;
    public const int MaximumDebtLimitMultiplier = 2;

    public const int PoorQualification = 150;
    public const int MiddleClassQualification = 600;
    public const int TycoonQualification = 1600;

    public const int PoorCreditLimit = 200;
    public const int MiddleClassCreditLimit = 700;
    public const int TycoonCreditLimit = 1200;

    private static readonly object Gate = new();
    public static AccountState GetState(Player player) => BankStateStore.Get(player);

    public static bool IsAccountOpened(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        lock (Gate)
        {
            return BankStateStore.Get(player).BankAccountOpened != 0;
        }
    }

    public static AccountSnapshot GetSnapshot(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);

        lock (Gate)
        {
            AccountState state = BankStateStore.Get(player);
            if (state.BankAccountOpened == 0)
            {
                return new AccountSnapshot(
                    player.Gold,
                    0,
                    0,
                    0,
                    0,
                    CreditTier.None,
                    0,
                    0,
                    0,
                    0,
                    false,
                    state.Revision,
                    false,
                    state.ButtSalesCount);
            }

            BankOperationResult initialization =
                EnsureUnifiedSavingsLocked(player, state);
            if (!initialization.Success)
            {
                throw new OverflowException(
                    "Legacy TD Bank balances could not be merged safely.");
            }

            ReconcileSavingsComponentsLocked(player, state);
            CreditTier tier = GetStoredTier(state);
            int creditLimit = GetCreditLimit(player, tier);
            return new AccountSnapshot(
                player.Gold,
                state.SavingsPrincipal,
                state.SavingsInterest,
                state.SavingsTenths,
                state.SavingsInterestEarnedTotal,
                tier,
                state.CreditDebt,
                creditLimit,
                Math.Min(
                    creditLimit,
                    Math.Max(
                        0,
                        GetMaximumDebt(player, tier) - state.CreditDebt)),
                state.QualifyingEarned,
                state.CreditPermanentlyClosed != 0,
                state.Revision,
                true,
                state.ButtSalesCount);
        }
    }

    public static BankOperationResult InitializeUnifiedSavings(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        lock (Gate)
        {
            AccountState state = BankStateStore.Get(player);
            return state.BankAccountOpened == 0
                ? BankOperationResult.Ok()
                : EnsureUnifiedSavingsLocked(player, state);
        }
    }

    public static BankOperationResult OpenBankAccount(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);

        lock (Gate)
        {
            AccountState state = BankStateStore.Get(player);
            if (state.BankAccountOpened != 0)
            {
                return BankOperationResult.Fail(
                    BankErrorCode.AlreadyProcessed);
            }

            AccountState rollbackState = state.Clone();
            int rollbackGold = player.Gold;
            state.BankAccountOpened = 1;
            BankOperationResult initialization =
                EnsureUnifiedSavingsLocked(player, state);
            if (!initialization.Success)
            {
                player.Gold = rollbackGold;
                state.CopyFrom(rollbackState);
                return initialization;
            }

            int startingGold = Math.Max(0, player.Gold);
            state.QualifyingEarned = 0;
            state.QualificationInitialized = 1;
            state.SavingsInterestEarnedTotal = 0;
            state.DebtGraceUsed = 0;
            state.PendingRelicLiquidationDebt = 0;

            BumpRevision(state);
            return BankOperationResult.Ok(startingGold);
        }
    }

    public static BankOperationResult RecordButtSale(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);

        lock (Gate)
        {
            AccountState state = BankStateStore.Get(player);
            if (state.BankAccountOpened == 0)
            {
                return BankOperationResult.Fail(
                    BankErrorCode.OperationUnavailable);
            }
            if (state.ButtSalesCount == int.MaxValue)
            {
                return BankOperationResult.Fail(
                    BankErrorCode.ArithmeticOverflow);
            }

            state.ButtSalesCount++;
            BumpRevision(state);
            return BankOperationResult.Ok(state.ButtSalesCount);
        }
    }

    public static int GetSavingsPrincipal(Player player)
        => GetSnapshot(player).SavingsPrincipal;

    public static int GetSavingsInterest(Player player)
        => GetSnapshot(player).SavingsInterest;

    public static int GetSavingsTenths(Player player)
        => GetSnapshot(player).SavingsTenths;

    public static int GetSavingsInterestEarnedTotal(Player player)
        => GetSnapshot(player).SavingsInterestEarnedTotal;

    public static int GetSavingsBalance(Player player)
        => GetSnapshot(player).SavingsBalance;

    public static long CalculateNextSavingsInterest(
   long savingsBalance,
   int carriedTenths)
    {
        return AscensionBankBenefits.ForAscension(0)
            .CalculateSavingsInterest(savingsBalance);
    }

    public static long CalculateNextSavingsInterest(
   Player player,
   long savingsBalance,
   int carriedTenths)
    {
        ArgumentNullException.ThrowIfNull(player);
        return AscensionBankBenefits.For(player)
            .CalculateSavingsInterest(savingsBalance);
    }

    public static int GetQualifyingEarned(Player player)
        => GetSnapshot(player).QualifyingEarned;

    public static CreditTier GetCreditTier(Player player)
        => GetSnapshot(player).CreditTier;

    public static int GetCreditDebt(Player player)
        => GetSnapshot(player).CreditDebt;

    public static int GetAvailableCredit(Player player)
        => GetSnapshot(player).AvailableCredit;

    public static bool IsCreditCardOpen(Player player)
        => GetCreditTier(player) != CreditTier.None;

    public static bool IsBankrupt(Player player)
        => GetSnapshot(player).IsBankrupt;

    public static long GetPurchasingPower(Player player)
        => GetSnapshot(player).TotalPurchasingPower;

    public static int GetQualificationThreshold(CreditTier tier)
        => AscensionBankBenefits.ForAscension(0)
            .GetQualificationThreshold(tier);

    public static int GetQualificationThreshold(
        Player player,
        CreditTier tier)
    {
        ArgumentNullException.ThrowIfNull(player);
        return AscensionBankBenefits.For(player)
            .GetQualificationThreshold(tier);
    }

    public static int GetCreditLimit(CreditTier tier)
        => AscensionBankBenefits.ForAscension(0).GetCreditLimit(tier);

    public static int GetCreditLimit(Player player, CreditTier tier)
    {
        ArgumentNullException.ThrowIfNull(player);
        return AscensionBankBenefits.For(player).GetCreditLimit(tier);
    }

    public static int GetMaximumDebt(CreditTier tier)
   => AscensionBankBenefits.ForAscension(0).GetMaximumDebt(tier);

    public static int GetMaximumDebt(Player player, CreditTier tier)
    {
        ArgumentNullException.ThrowIfNull(player);
        return AscensionBankBenefits.For(player).GetMaximumDebt(tier);
    }

    public static int GetDebtInterestBasisPoints(CreditTier tier)
   => AscensionBankBenefits.ForAscension(0)
       .GetDebtInterestBasisPoints(tier);

    public static int GetDebtInterestBasisPoints(
        Player player,
        CreditTier tier)
    {
        ArgumentNullException.ThrowIfNull(player);
        return AscensionBankBenefits.For(player)
            .GetDebtInterestBasisPoints(tier);
    }

    public static int GetDebtGraceFloorCount(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        return AscensionBankBenefits.For(player).DebtGraceFloorCount;
    }

    public static int GetDebtCycleFloors(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        lock (Gate)
        {
            return BankStateStore.Get(player).DebtCycleFloors;
        }
    }

    public static int GetDebtGraceFloorsRemaining(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        lock (Gate)
        {
            AccountState state = BankStateStore.Get(player);
            int graceFloors = GetDebtGraceFloorCount(player);
            if (state.CreditDebt == 0)
            {
                return state.DebtGraceUsed == 0
                    ? graceFloors
                    : 0;
            }

            return Math.Max(
                0,
                graceFloors - state.DebtCycleFloors);
        }
    }

    public static CreditTier GetHighestEligibleTier(int qualifyingEarned)
    {
        if (qualifyingEarned >= TycoonQualification)
        {
            return CreditTier.BisaTycoon;
        }

        if (qualifyingEarned >= MiddleClassQualification)
        {
            return CreditTier.BisaMiddleClass;
        }

        return qualifyingEarned >= PoorQualification
            ? CreditTier.BisaPoor
            : CreditTier.None;
    }

    public static CreditTier GetHighestEligibleTier(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        int qualifyingEarned = GetQualifyingEarned(player);
        AscensionBankBenefits benefits =
            AscensionBankBenefits.For(player);
        if (qualifyingEarned
            >= benefits.GetQualificationThreshold(CreditTier.BisaTycoon))
        {
            return CreditTier.BisaTycoon;
        }

        if (qualifyingEarned
            >= benefits.GetQualificationThreshold(
                CreditTier.BisaMiddleClass))
        {
            return CreditTier.BisaMiddleClass;
        }

        return qualifyingEarned
                >= benefits.GetQualificationThreshold(CreditTier.BisaPoor)
            ? CreditTier.BisaPoor
            : CreditTier.None;
    }

    public static CreditTier GetNextCreditTier(CreditTier tier)
    {
        return tier switch
        {
            CreditTier.None => CreditTier.BisaPoor,
            CreditTier.BisaPoor => CreditTier.BisaMiddleClass,
            CreditTier.BisaMiddleClass => CreditTier.BisaTycoon,
            CreditTier.BisaTycoon => CreditTier.BisaTycoon,
            _ => throw new ArgumentOutOfRangeException(
                nameof(tier),
                tier,
                "Unknown credit tier."),
        };
    }

    public static bool CanApplyFor(Player player, CreditTier requestedTier)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (!IsValidCardTier(requestedTier))
        {
            return false;
        }

        AccountSnapshot snapshot = GetSnapshot(player);
        return !snapshot.IsBankrupt
            && requestedTier > snapshot.CreditTier
            && snapshot.QualifyingEarned
                >= GetQualificationThreshold(player, requestedTier);
    }

    public static BankOperationResult InitializeQualification(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);

        lock (Gate)
        {
            AccountState state = BankStateStore.Get(player);
            if (state.BankAccountOpened == 0)
            {
                return BankOperationResult.Ok();
            }

            BankOperationResult initialization =
                EnsureUnifiedSavingsLocked(player, state);
            if (!initialization.Success)
            {
                return initialization;
            }

            if (state.QualificationInitialized != 0)
            {
                return BankOperationResult.Fail(BankErrorCode.AlreadyProcessed);
            }

            state.QualifyingEarned = 0;
            state.QualificationInitialized = 1;
            BumpRevision(state);
            return BankOperationResult.Ok();
        }
    }

    public static BankOperationResult RecordNativeGoldGain(
   Player player,
   int beforeGold)
    {
        ArgumentNullException.ThrowIfNull(player);

        long gained = (long)player.Gold - beforeGold;
        return gained is <= 0 or > int.MaxValue
            ? BankOperationResult.Fail(BankErrorCode.InvalidAmount)
            : RecordNativeGoldGainAmount(
                player,
                (int)gained,
                wasStolenBack: false);
    }

    public static BankOperationResult RecordNativeGoldGainAmount(
   Player player,
   int amount,
   bool wasStolenBack)
    {
        ArgumentNullException.ThrowIfNull(player);

        lock (Gate)
        {
            if (amount <= 0)
            {
                return BankOperationResult.Fail(BankErrorCode.InvalidAmount);
            }

            AccountState state = BankStateStore.Get(player);
            if (state.BankAccountOpened == 0)
            {
                return BankOperationResult.Ok(amount);
            }
            BankOperationResult initialization =
                EnsureUnifiedSavingsLocked(player, state);
            if (!initialization.Success)
            {
                return initialization;
            }

            long beforeGold = (long)player.Gold - amount;
            int debtRepaid = Math.Min(state.CreditDebt, amount);
            int walletAmount = amount - debtRepaid;
            long adjustedGold = (long)player.Gold - debtRepaid;
            if (beforeGold is > int.MaxValue or < int.MinValue
                || adjustedGold is > int.MaxValue or < int.MinValue)
            {
                return BankOperationResult.Fail(
                    BankErrorCode.ArithmeticOverflow);
            }

            int positiveIncrease =
                PositiveBalanceIncrease(beforeGold, adjustedGold);
            long newPrincipal = state.SavingsPrincipal;
            long newInterest = state.SavingsInterest;
            long newEarned = state.QualifyingEarned;
            int newTenths = state.SavingsTenths;
            int restoredWholeInterest = 0;
            int restoredWholeWallet = 0;
            int remainingDebt = state.CreditDebt - debtRepaid;
            int newStolenPrincipal = state.StolenSavingsPrincipal;
            int newStolenInterest = state.StolenSavingsInterest;
            int newStolenTenths = state.StolenSavingsTenths;

            if (wasStolenBack)
            {
                int returnedInterest =
                    Math.Min(newStolenInterest, amount);
                newStolenInterest -= returnedInterest;
                int remainingReturn = amount - returnedInterest;
                int returnedPrincipal =
                    Math.Min(newStolenPrincipal, remainingReturn);
                newStolenPrincipal -= returnedPrincipal;

                int restoredInterest =
                    Math.Min(returnedInterest, positiveIncrease);
                int positiveRemaining =
                    positiveIncrease - restoredInterest;
                int restoredPrincipal =
                    Math.Min(returnedPrincipal, positiveRemaining);
                positiveRemaining -= restoredPrincipal;

                newInterest += restoredInterest;
                newPrincipal += restoredPrincipal + positiveRemaining;
                if (positiveIncrease > 0
                    && newStolenTenths > 0)
                {
                    int restoredTenths = checked(
                        newTenths + newStolenTenths);
                    restoredWholeInterest = restoredTenths / 10;
                    newTenths = restoredTenths % 10;
                    newStolenTenths = 0;

                    int fractionalDebtPayment =
                        Math.Min(remainingDebt, restoredWholeInterest);
                    remainingDebt -= fractionalDebtPayment;
                    debtRepaid += fractionalDebtPayment;
                    int fractionalWallet =
                        restoredWholeInterest - fractionalDebtPayment;
                    restoredWholeWallet = fractionalWallet;
                    adjustedGold += fractionalWallet;
                    newInterest += fractionalWallet;
                }
            }
            else
            {
                newPrincipal += positiveIncrease;
                newEarned += amount;
            }

            if (newEarned > int.MaxValue
                || newPrincipal > int.MaxValue
                || newInterest > int.MaxValue
                || adjustedGold is > int.MaxValue or < int.MinValue)
            {
                return BankOperationResult.Fail(
                    BankErrorCode.ArithmeticOverflow);
            }

            player.Gold = (int)adjustedGold;
            state.CreditDebt = remainingDebt;
            if (remainingDebt
                < GetMaximumDebt(player, GetStoredTier(state)))
            {
                state.CreditCeilingPending = 0;
            }
            if (remainingDebt == 0)
            {
                ResetDebtCycleLocked(state);
            }
            state.QualifyingEarned = (int)newEarned;
            state.SavingsPrincipal = (int)newPrincipal;
            state.SavingsInterest = (int)newInterest;
            state.SavingsTenths = newTenths;
            state.StolenSavingsPrincipal = newStolenPrincipal;
            state.StolenSavingsInterest = newStolenInterest;
            state.StolenSavingsTenths = newStolenTenths;
            ReconcileSavingsComponentsLocked(player, state);
            BumpRevision(state);
            return BankOperationResult.Ok(
                checked(walletAmount + restoredWholeWallet),
                debtRepaid);
        }
    }

    public static BankOperationResult RecordNativeGoldLoss(
   Player player,
   int beforeGold,
   GoldLossType goldLossType = GoldLossType.Lost)
    {
        ArgumentNullException.ThrowIfNull(player);

        lock (Gate)
        {
            AccountState state = BankStateStore.Get(player);
            if (state.BankAccountOpened == 0)
            {
                return BankOperationResult.Ok(
                    PositiveBalanceDecrease(beforeGold, player.Gold));
            }
            BankOperationResult initialization =
                EnsureUnifiedSavingsLocked(player, state);
            if (!initialization.Success)
            {
                return initialization;
            }

            int removed = PositiveBalanceDecrease(beforeGold, player.Gold);
            if (removed > 0)
            {
                int fromInterest =
                    Math.Min(state.SavingsInterest, removed);
                int fromPrincipal = removed - fromInterest;
                if (goldLossType == GoldLossType.Stolen)
                {
                    long stolenInterest =
                        (long)state.StolenSavingsInterest + fromInterest;
                    long stolenPrincipal =
                        (long)state.StolenSavingsPrincipal + fromPrincipal;
                    if (stolenInterest > int.MaxValue
                        || stolenPrincipal > int.MaxValue)
                    {
                        return BankOperationResult.Fail(
                            BankErrorCode.ArithmeticOverflow);
                    }

                    state.StolenSavingsInterest = (int)stolenInterest;
                    state.StolenSavingsPrincipal = (int)stolenPrincipal;
                    if (Math.Max(0, player.Gold) == 0
                        && state.SavingsTenths > 0)
                    {
                        state.StolenSavingsTenths = checked(
                            state.StolenSavingsTenths
                            + state.SavingsTenths);
                    }
                }

                ConsumeSavingsLocked(state, removed);
            }

            ReconcileSavingsComponentsLocked(player, state);
            int ceilingCollection = 0;
            if (state.CreditCeilingPending != 0)
            {
                BankOperationResult closure =
                    FinalizePendingCreditCeilingLocked(player, state);
                if (!closure.Success)
                {
                    return closure;
                }
                ceilingCollection = closure.SecondaryAmount;
            }
            BumpRevision(state);
            return BankOperationResult.Ok(removed, ceilingCollection);
        }
    }

    public static BankOperationResult ClearUnreturnedStolenGold(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);

        lock (Gate)
        {
            AccountState state = BankStateStore.Get(player);
            if (state.BankAccountOpened == 0)
            {
                return BankOperationResult.Ok();
            }
            BankOperationResult initialization =
                EnsureUnifiedSavingsLocked(player, state);
            if (!initialization.Success)
            {
                return initialization;
            }

            int principal = state.StolenSavingsPrincipal;
            int interest = state.StolenSavingsInterest;
            int tenths = state.StolenSavingsTenths;
            state.StolenSavingsPrincipal = 0;
            state.StolenSavingsInterest = 0;
            state.StolenSavingsTenths = 0;
            if (principal > 0 || interest > 0 || tenths > 0)
            {
                BumpRevision(state);
            }

            return BankOperationResult.Ok(principal, interest);
        }
    }

    public static BankOperationResult RecordGoldEarned(Player player, int amount)
    {
        ArgumentNullException.ThrowIfNull(player);

        lock (Gate)
        {
            if (amount <= 0)
            {
                return BankOperationResult.Fail(BankErrorCode.InvalidAmount);
            }

            AccountState state = BankStateStore.Get(player);
            if (state.BankAccountOpened == 0)
            {
                return BankOperationResult.Fail(
                    BankErrorCode.OperationUnavailable);
            }
            BankOperationResult initialization =
                EnsureUnifiedSavingsLocked(player, state);
            if (!initialization.Success)
            {
                return initialization;
            }

            long earned = (long)state.QualifyingEarned + amount;
            if (earned > int.MaxValue)
            {
                return BankOperationResult.Fail(BankErrorCode.ArithmeticOverflow);
            }

            state.QualifyingEarned = (int)earned;
            BumpRevision(state);
            return BankOperationResult.Ok(amount);
        }
    }

    public static BankOperationResult ApplyForCreditCard(
   Player player,
   CreditTier requestedTier)
    {
        ArgumentNullException.ThrowIfNull(player);

        lock (Gate)
        {
            if (!IsValidCardTier(requestedTier))
            {
                return BankOperationResult.Fail(
                    BankErrorCode.InvalidCreditTier);
            }

            AccountState state = BankStateStore.Get(player);
            if (state.BankAccountOpened == 0)
            {
                return BankOperationResult.Fail(
                    BankErrorCode.OperationUnavailable);
            }
            BankOperationResult initialization =
                EnsureUnifiedSavingsLocked(player, state);
            if (!initialization.Success)
            {
                return initialization;
            }

            if (state.CreditPermanentlyClosed != 0)
            {
                return BankOperationResult.Fail(
                    BankErrorCode.CreditPermanentlyClosed);
            }

            CreditTier currentTier = GetStoredTier(state);
            if (currentTier == CreditTier.BisaTycoon)
            {
                return BankOperationResult.Fail(
                    BankErrorCode.AlreadyHighestCreditTier);
            }

            if (requestedTier <= currentTier)
            {
                return BankOperationResult.Fail(
                    BankErrorCode.CreditTierNotUpgrade);
            }

            if (state.QualifyingEarned
                < GetQualificationThreshold(player, requestedTier))
            {
                return BankOperationResult.Fail(BankErrorCode.NotEligible);
            }

            state.CreditTier = (int)requestedTier;
            BumpRevision(state);
            return BankOperationResult.Ok(
                GetCreditLimit(player, requestedTier));
        }
    }

    public static BankOperationResult ApplyForNextCreditTier(Player player)
    {
        AccountSnapshot snapshot = GetSnapshot(player);
        if (snapshot.IsBankrupt)
        {
            return BankOperationResult.Fail(
                BankErrorCode.CreditPermanentlyClosed);
        }

        if (snapshot.CreditTier == CreditTier.BisaTycoon)
        {
            return BankOperationResult.Fail(
                BankErrorCode.AlreadyHighestCreditTier);
        }

        return ApplyForCreditCard(
            player,
            GetNextCreditTier(snapshot.CreditTier));
    }

    public static bool ApplyCard(
        Player player,
        CreditTier requestedTier,
        out string error)
    {
        BankOperationResult result =
            ApplyForCreditCard(player, requestedTier);
        error = GetErrorMessage(result.Error);
        return result.Success;
    }

    public static BankOperationResult AddGold(
   Player player,
   int amount,
   bool countsTowardQualification = true)
   => DepositGold(
       player,
       amount,
       countsTowardQualification
           ? GoldIncomeSource.NormalGameGold
           : GoldIncomeSource.Other);

    public static BankOperationResult DepositGold(
   Player player,
   int amount,
   GoldIncomeSource source)
    {
        ArgumentNullException.ThrowIfNull(player);

        lock (Gate)
        {
            if (amount <= 0)
            {
                return BankOperationResult.Fail(BankErrorCode.InvalidAmount);
            }
            if (!Enum.IsDefined(source))
            {
                return BankOperationResult.Fail(BankErrorCode.InvalidAccount);
            }

            AccountState state = BankStateStore.Get(player);
            if (state.BankAccountOpened == 0)
            {
                return BankOperationResult.Fail(
                    BankErrorCode.OperationUnavailable);
            }
            BankOperationResult initialization =
                EnsureUnifiedSavingsLocked(player, state);
            if (!initialization.Success)
            {
                return initialization;
            }

            ReconcileSavingsComponentsLocked(player, state);
            return DepositGoldLocked(player, state, amount, source);
        }
    }

    public static BankOperationResult PreviewDepositGold(
   Player player,
   int amount,
   GoldIncomeSource source)
    {
        ArgumentNullException.ThrowIfNull(player);

        lock (Gate)
        {
            if (amount <= 0)
            {
                return BankOperationResult.Fail(BankErrorCode.InvalidAmount);
            }
            if (!Enum.IsDefined(source))
            {
                return BankOperationResult.Fail(
                    BankErrorCode.InvalidAccount);
            }

            AccountState state = BankStateStore.Get(player);
            if (state.BankAccountOpened == 0
                || state.UnifiedSavingsInitialized == 0)
            {
                return BankOperationResult.Fail(
                    BankErrorCode.OperationUnavailable);
            }

            int debtRepaid = Math.Min(state.CreditDebt, amount);
            int walletAmount = amount - debtRepaid;
            long newGold = (long)player.Gold + walletAmount;
            bool qualifies = source is
                GoldIncomeSource.NormalGameGold
                or GoldIncomeSource.SavingsInterest;
            long newEarned = qualifies
                ? (long)state.QualifyingEarned + amount
                : state.QualifyingEarned;
            long newIssuedInterest =
                source == GoldIncomeSource.SavingsInterest
                    ? (long)state.SavingsInterestEarnedTotal + amount
                    : state.SavingsInterestEarnedTotal;
            int positiveIncrease =
                PositiveBalanceIncrease(player.Gold, newGold);
            long newPrincipal = state.SavingsPrincipal;
            long newInterest = state.SavingsInterest;
            int currentPositiveGold = Math.Max(0, player.Gold);
            long tracked = newPrincipal + newInterest;
            if (tracked < currentPositiveGold)
            {
                newPrincipal += currentPositiveGold - tracked;
            }
            else if (tracked > currentPositiveGold)
            {
                long toConsume = tracked - currentPositiveGold;
                long fromInterest = Math.Min(newInterest, toConsume);
                newInterest -= fromInterest;
                newPrincipal = Math.Max(
                    0L,
                    newPrincipal - (toConsume - fromInterest));
            }
            if (source == GoldIncomeSource.SavingsInterest)
            {
                newInterest += positiveIncrease;
            }
            else
            {
                newPrincipal += positiveIncrease;
            }

            return newGold is > int.MaxValue or < int.MinValue
                || newEarned > int.MaxValue
                || newIssuedInterest > int.MaxValue
                || newPrincipal > int.MaxValue
                || newInterest > int.MaxValue
                ? BankOperationResult.Fail(
                    BankErrorCode.ArithmeticOverflow)
                : BankOperationResult.Ok(walletAmount, debtRepaid);
        }
    }

    public static BankOperationResult AdvanceCreditForPurchase(
   Player player,
   int amount)
    {
        ArgumentNullException.ThrowIfNull(player);

        lock (Gate)
        {
            if (amount <= 0)
            {
                return BankOperationResult.Fail(BankErrorCode.InvalidAmount);
            }

            AccountState state = BankStateStore.Get(player);
            if (state.BankAccountOpened == 0)
            {
                return BankOperationResult.Fail(
                    BankErrorCode.OperationUnavailable);
            }
            BankOperationResult initialization =
                EnsureUnifiedSavingsLocked(player, state);
            if (!initialization.Success)
            {
                return initialization;
            }

            ReconcileSavingsComponentsLocked(player, state);
            BankOperationResult existingCeiling =
                EnforceCreditCeilingLocked(
                    player,
                    state,
                    GetStoredTier(state));
            if (!existingCeiling.Success)
            {
                return existingCeiling;
            }
            if (state.CreditPermanentlyClosed != 0)
            {
                return BankOperationResult.Fail(
                    BankErrorCode.CreditPermanentlyClosed);
            }

            CreditTier tier = GetStoredTier(state);
            if (tier == CreditTier.None)
            {
                return BankOperationResult.Fail(
                    BankErrorCode.CreditCardNotOpen);
            }

            int maximumDebt = GetMaximumDebt(player, tier);
            int available =
                Math.Max(0, maximumDebt - state.CreditDebt);
            if (amount > GetCreditLimit(player, tier)
                || amount > available)
            {
                return BankOperationResult.Fail(
                    BankErrorCode.CreditLimitExceeded);
            }

            long newGold = (long)player.Gold + amount;
            long newDebt = (long)state.CreditDebt + amount;
            if (newGold is > int.MaxValue or < int.MinValue
                || newDebt > int.MaxValue)
            {
                return BankOperationResult.Fail(
                    BankErrorCode.ArithmeticOverflow);
            }

            player.Gold = (int)newGold;
            BeginDebtCycleIfNeededLocked(player, state);
            state.CreditDebt = (int)newDebt;
            if (state.CreditDebt == maximumDebt)
            {


                state.CreditCeilingPending = 1;
            }
            BumpRevision(state);
            return BankOperationResult.Ok(amount);
        }
    }

    public static BankOperationResult FinalizePendingCreditCeiling(
   Player player)
    {
        ArgumentNullException.ThrowIfNull(player);

        lock (Gate)
        {
            AccountState state = BankStateStore.Get(player);
            if (state.BankAccountOpened == 0)
            {
                return BankOperationResult.Ok();
            }
            BankOperationResult initialization =
                EnsureUnifiedSavingsLocked(player, state);
            if (!initialization.Success)
            {
                return initialization;
            }

            ReconcileSavingsComponentsLocked(player, state);
            int wasPending = state.CreditCeilingPending;
            BankOperationResult result =
                FinalizePendingCreditCeilingLocked(player, state);
            if (result.Success
                && (wasPending != state.CreditCeilingPending
                    || result.SecondaryAmount > 0))
            {
                BumpRevision(state);
            }
            return result;
        }
    }

    public static BankOperationResult TrySpend(Player player, int amount)
    {
        ArgumentNullException.ThrowIfNull(player);

        lock (Gate)
        {
            if (amount <= 0)
            {
                return BankOperationResult.Fail(BankErrorCode.InvalidAmount);
            }

            AccountState state = BankStateStore.Get(player);
            if (state.BankAccountOpened == 0)
            {
                return BankOperationResult.Fail(
                    BankErrorCode.OperationUnavailable);
            }
            BankOperationResult initialization =
                EnsureUnifiedSavingsLocked(player, state);
            if (!initialization.Success)
            {
                return initialization;
            }

            ReconcileSavingsComponentsLocked(player, state);
            BankOperationResult existingCeiling =
                EnforceCreditCeilingLocked(
                    player,
                    state,
                    GetStoredTier(state));
            if (!existingCeiling.Success)
            {
                return existingCeiling;
            }
            int availableSavings = Math.Max(0, player.Gold);
            int fromSavings = Math.Min(availableSavings, amount);
            int fromCredit = amount - fromSavings;
            if (fromCredit > 0)
            {
                if (state.CreditPermanentlyClosed != 0)
                {
                    return BankOperationResult.Fail(
                        BankErrorCode.CreditPermanentlyClosed);
                }

                CreditTier tier = GetStoredTier(state);
                if (tier == CreditTier.None)
                {
                    return BankOperationResult.Fail(
                        BankErrorCode.InsufficientFunds);
                }

                int availableCredit =
                    Math.Max(
                        0,
                        GetMaximumDebt(player, tier) - state.CreditDebt);
                if (fromCredit > GetCreditLimit(player, tier)
                    || fromCredit > availableCredit)
                {
                    return BankOperationResult.Fail(
                        BankErrorCode.CreditLimitExceeded);
                }
            }

            int newDebt = checked(state.CreditDebt + fromCredit);
            bool closesAtCeiling = fromCredit > 0
                && newDebt
                    == GetMaximumDebt(player, GetStoredTier(state));
            long finalGold = (long)player.Gold - fromSavings;
            if (finalGold is > int.MaxValue or < int.MinValue)
            {
                return BankOperationResult.Fail(
                    BankErrorCode.ArithmeticOverflow);
            }

            player.Gold -= fromSavings;
            ConsumeSavingsLocked(state, fromSavings);
            if (fromCredit > 0)
            {
                BeginDebtCycleIfNeededLocked(player, state);
            }
            state.CreditDebt = newDebt;
            if (closesAtCeiling)
            {
                BankOperationResult closure =
                    CloseCreditAtCeilingLocked(player, state);
                if (!closure.Success)
                {
                    return closure;
                }
            }
            ReconcileSavingsComponentsLocked(player, state);
            BumpRevision(state);
            return BankOperationResult.Ok(amount, fromCredit);
        }
    }

    public static BankOperationResult AccrueSavingsInterest(
   Player player,
   int floorToken)
    {
        ArgumentNullException.ThrowIfNull(player);

        lock (Gate)
        {
            AccountState state = BankStateStore.Get(player);
            if (state.BankAccountOpened == 0)
            {
                return BankOperationResult.Ok();
            }
            BankOperationResult initialization =
                EnsureUnifiedSavingsLocked(player, state);
            if (!initialization.Success)
            {
                return initialization;
            }

            if (state.LastSavingsTurnToken == floorToken)
            {
                return BankOperationResult.Fail(
                    BankErrorCode.AlreadyProcessed);
            }

            ReconcileSavingsComponentsLocked(player, state);
            int wholeGold = checked((int)CalculateNextSavingsInterest(
                player,
                player.Gold,
                state.SavingsTenths));
            int remainingTenths = 0;
            int debtRepaid = Math.Min(state.CreditDebt, wholeGold);
            int walletAmount = wholeGold - debtRepaid;
            long newInterest =
                (long)state.SavingsInterest + walletAmount;
            long newGold = (long)player.Gold + walletAmount;
            long newQualifying =
                (long)state.QualifyingEarned + wholeGold;
            long newIssuedInterest =
                (long)state.SavingsInterestEarnedTotal + wholeGold;
            if (newInterest > int.MaxValue
                || newQualifying > int.MaxValue
                || newIssuedInterest > int.MaxValue
                || newGold is > int.MaxValue or < int.MinValue)
            {
                return BankOperationResult.Fail(
                    BankErrorCode.ArithmeticOverflow);
            }

            player.Gold = (int)newGold;
            state.CreditDebt -= debtRepaid;
            if (state.CreditDebt
                < GetMaximumDebt(player, GetStoredTier(state)))
            {
                state.CreditCeilingPending = 0;
            }
            if (state.CreditDebt == 0)
            {
                ResetDebtCycleLocked(state);
            }
            state.SavingsInterest = (int)newInterest;
            state.QualifyingEarned = (int)newQualifying;
            state.SavingsInterestEarnedTotal =
                (int)newIssuedInterest;
            state.SavingsTenths = remainingTenths;
            if (state.SavingsInterestTurns < int.MaxValue)
            {
                state.SavingsInterestTurns++;
            }

            state.LastSavingsTurnToken = floorToken;
            BumpRevision(state);
            return BankOperationResult.Ok(walletAmount, debtRepaid);
        }
    }

    public static BankOperationResult AccrueDebtInterest(
   Player player,
   int floorToken)
    {
        ArgumentNullException.ThrowIfNull(player);

        lock (Gate)
        {
            AccountState state = BankStateStore.Get(player);
            if (state.BankAccountOpened == 0)
            {
                return BankOperationResult.Ok();
            }
            BankOperationResult initialization =
                EnsureUnifiedSavingsLocked(player, state);
            if (!initialization.Success)
            {
                return initialization;
            }

            ReconcileSavingsComponentsLocked(player, state);
            BankOperationResult ceiling;
            if (state.CreditCeilingPending != 0)
            {
                ceiling =
                    FinalizePendingCreditCeilingLocked(player, state);
                if (ceiling.Success
                    && state.CreditCeilingPending == 0)
                {
                    BumpRevision(state);
                }
            }
            else
            {
                ceiling = EnforceCreditCeilingLocked(
                    player,
                    state,
                    GetStoredTier(state));
            }
            if (!ceiling.Success)
            {
                return ceiling;
            }
            if (ceiling.SecondaryAmount > 0)
            {
                return BankOperationResult.Ok(
                    secondaryAmount: ceiling.SecondaryAmount);
            }

            if (state.LastDebtFloorToken == floorToken)
            {
                return BankOperationResult.Fail(
                    BankErrorCode.AlreadyProcessed);
            }

            if (state.CreditDebt == 0)
            {
                state.LastDebtInterestCharge = 0;
                state.LastDebtFloorToken = floorToken;
                state.DebtCycleFloors = 0;
                state.CreditCeilingPending = 0;
                BumpRevision(state);
                return BankOperationResult.Ok();
            }

            int cycleFloors = state.DebtCycleFloors == int.MaxValue
                ? int.MaxValue
                : state.DebtCycleFloors + 1;
            CreditTier tier = GetStoredTier(state);
            int interestCharge = 0;
            int graceFloors = GetDebtGraceFloorCount(player);
            if (cycleFloors > graceFloors)
            {
                int basisPoints =
                    GetDebtInterestBasisPoints(player, tier);
                interestCharge = checked((int)(
                    ((long)state.CreditDebt * basisPoints + 9_999L)
                    / 10_000L));
            }

            int maximumDebt = GetMaximumDebt(player, tier);
            long compounded = (long)state.CreditDebt + interestCharge;
            int newDebt = (int)Math.Min(maximumDebt, compounded);
            interestCharge = newDebt - state.CreditDebt;
            state.LastDebtInterestCharge = interestCharge;
            state.LastDebtFloorToken = floorToken;
            state.DebtCycleFloors = cycleFloors;
            state.CreditDebt = newDebt;
            int collected = 0;
            if (newDebt == maximumDebt)
            {
                BankOperationResult closure =
                    CloseCreditAtCeilingLocked(player, state);
                if (!closure.Success)
                {
                    return closure;
                }
                collected = closure.SecondaryAmount;
                ReconcileSavingsComponentsLocked(player, state);
            }
            BumpRevision(state);
            return BankOperationResult.Ok(interestCharge, collected);
        }
    }

    public static int GetPendingRelicLiquidationDebt(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        lock (Gate)
        {
            return BankStateStore.Get(player)
                .PendingRelicLiquidationDebt;
        }
    }

    public static BankOperationResult RepairLegacyNegativeForeclosureBalance(
        Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        lock (Gate)
        {
            AccountState state = BankStateStore.Get(player);
            if (state.CreditPermanentlyClosed == 0 || player.Gold >= 0)
            {
                return BankOperationResult.Ok();
            }

            int restored = player.Gold == int.MinValue
                ? int.MaxValue
                : -player.Gold;
            player.Gold = 0;
            ReconcileSavingsComponentsLocked(player, state);
            BumpRevision(state);
            return BankOperationResult.Ok(restored);
        }
    }

    public static BankOperationResult CompletePendingRelicLiquidation(
   Player player,
   int expectedDebt)
    {
        ArgumentNullException.ThrowIfNull(player);
        lock (Gate)
        {
            if (expectedDebt <= 0)
            {
                return BankOperationResult.Fail(
                    BankErrorCode.InvalidAmount);
            }

            AccountState state = BankStateStore.Get(player);
            if (state.PendingRelicLiquidationDebt == 0)
            {
                return BankOperationResult.Fail(
                    BankErrorCode.AlreadyProcessed);
            }
            if (state.PendingRelicLiquidationDebt != expectedDebt)
            {
                return BankOperationResult.Fail(
                    BankErrorCode.InvalidAmount);
            }

            state.PendingRelicLiquidationDebt = 0;
            BumpRevision(state);
            return BankOperationResult.Ok(expectedDebt);
        }
    }

    public static BankOperationResult ETransfer(
   Player sender,
   Player recipient,
   int amount)
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(recipient);

        lock (Gate)
        {
            if (amount <= 0)
            {
                return BankOperationResult.Fail(BankErrorCode.InvalidAmount);
            }

            if (ReferenceEquals(sender, recipient)
                || sender.NetId == recipient.NetId)
            {
                return BankOperationResult.Fail(BankErrorCode.SamePlayer);
            }

            AccountState senderState = BankStateStore.Get(sender);
            AccountState recipientState = BankStateStore.Get(recipient);
            if (senderState.BankAccountOpened == 0
                || recipientState.BankAccountOpened == 0)
            {
                return BankOperationResult.Fail(
                    BankErrorCode.OperationUnavailable);
            }
            BankOperationResult senderInitialization =
                EnsureUnifiedSavingsLocked(sender, senderState);
            BankOperationResult recipientInitialization =
                EnsureUnifiedSavingsLocked(recipient, recipientState);
            if (!senderInitialization.Success)
            {
                return senderInitialization;
            }
            if (!recipientInitialization.Success)
            {
                return recipientInitialization;
            }

            ReconcileSavingsComponentsLocked(sender, senderState);
            ReconcileSavingsComponentsLocked(recipient, recipientState);
            if (Math.Max(0, sender.Gold) < amount)
            {
                return BankOperationResult.Fail(
                    BankErrorCode.InsufficientFunds);
            }

            int transferredInterest =
                Math.Min(senderState.SavingsInterest, amount);
            int transferredPrincipal = amount - transferredInterest;
            int debtRepaid =
                Math.Min(recipientState.CreditDebt, amount);
            int walletAmount = amount - debtRepaid;
            long recipientGold = (long)recipient.Gold + walletAmount;
            int positiveIncrease =
                PositiveBalanceIncrease(recipient.Gold, recipientGold);
            int absorbedBeforePositive = amount - positiveIncrease;
            int absorbedInterest =
                Math.Min(transferredInterest, absorbedBeforePositive);
            int absorbedPrincipal = Math.Min(
                transferredPrincipal,
                absorbedBeforePositive - absorbedInterest);
            int restoredInterest =
                transferredInterest - absorbedInterest;
            int restoredPrincipal =
                transferredPrincipal - absorbedPrincipal;
            long recipientPrincipal =
                (long)recipientState.SavingsPrincipal + restoredPrincipal;
            long recipientInterest =
                (long)recipientState.SavingsInterest + restoredInterest;
            if (recipientGold is > int.MaxValue or < int.MinValue
                || recipientPrincipal > int.MaxValue
                || recipientInterest > int.MaxValue)
            {
                return BankOperationResult.Fail(
                    BankErrorCode.ArithmeticOverflow);
            }

            sender.Gold -= amount;
            ConsumeSavingsLocked(senderState, amount);
            recipient.Gold = (int)recipientGold;
            recipientState.CreditDebt -= debtRepaid;
            if (recipientState.CreditDebt
                < GetMaximumDebt(
                    recipient,
                    GetStoredTier(recipientState)))
            {
                recipientState.CreditCeilingPending = 0;
            }
            if (recipientState.CreditDebt == 0)
            {
                ResetDebtCycleLocked(recipientState);
            }
            recipientState.SavingsPrincipal = (int)recipientPrincipal;
            recipientState.SavingsInterest = (int)recipientInterest;
            ReconcileSavingsComponentsLocked(sender, senderState);
            ReconcileSavingsComponentsLocked(recipient, recipientState);
            BumpRevision(senderState);
            BumpRevision(recipientState);
            return BankOperationResult.Ok(walletAmount, debtRepaid);
        }
    }

    public static bool TryETransfer(
        Player sender,
        Player recipient,
        int amount,
        out string error)
    {
        BankOperationResult result = ETransfer(sender, recipient, amount);
        error = GetErrorMessage(result.Error);
        return result.Success;
    }

    public static string GetErrorMessage(BankErrorCode error)
    {
        return error switch
        {
            BankErrorCode.None => string.Empty,
            BankErrorCode.InvalidAmount =>
                "Amount must be greater than zero.",
            BankErrorCode.InvalidAccount =>
                "That account is not available for this operation.",
            BankErrorCode.SameAccount =>
                "Source and destination accounts must be different.",
            BankErrorCode.SamePlayer =>
                "Sender and recipient must be different players.",
            BankErrorCode.InsufficientFunds =>
                "The savings balance does not have enough funds.",
            BankErrorCode.CreditCardNotOpen =>
                "No credit card has been approved yet.",
            BankErrorCode.CreditLimitExceeded =>
                "This charge would exceed the current card's debt ceiling.",
            BankErrorCode.InvalidCreditTier =>
                "That credit-card tier does not exist.",
            BankErrorCode.CreditTierNotUpgrade =>
                "The requested card is not an upgrade.",
            BankErrorCode.NotEligible =>
                "The cumulative gold requirement has not been met.",
            BankErrorCode.AlreadyHighestCreditTier =>
                "The highest credit-card tier is already active.",
            BankErrorCode.AlreadyProcessed =>
                "Interest for this floor was already processed.",
            BankErrorCode.ArithmeticOverflow =>
                "The balance is too large to process safely.",
            BankErrorCode.OperationUnavailable =>
                "This bank operation is not currently available.",
            BankErrorCode.CreditPermanentlyClosed =>
                "Reaching the debt ceiling permanently closed this card.",
            BankErrorCode.InsufficientHealth =>
                "That sale would be fatal.",
            _ => "The bank rejected the operation.",
        };
    }

    private static BankOperationResult EnsureUnifiedSavingsLocked(
        Player player,
        AccountState state)
    {
        if (state.UnifiedSavingsInitialized != 0)
        {
            return BankOperationResult.Ok();
        }

        int oldGold = player.Gold;
        int oldPrincipal = state.SavingsPrincipal;
        int oldInterest = state.SavingsInterest;
        long mergedGold =
            (long)oldGold + oldPrincipal + oldInterest;
        if (mergedGold is > int.MaxValue or < int.MinValue)
        {
            return BankOperationResult.Fail(
                BankErrorCode.ArithmeticOverflow);
        }

        int positiveMerged = (int)Math.Max(0L, mergedGold);
        int retainedInterest = Math.Min(oldInterest, positiveMerged);
        player.Gold = (int)mergedGold;
        state.SavingsInterest = retainedInterest;
        state.SavingsPrincipal = positiveMerged - retainedInterest;
        state.UnifiedSavingsInitialized = 1;
        ReconcileSavingsComponentsLocked(player, state);
        BumpRevision(state);
        return BankOperationResult.Ok(oldPrincipal + oldInterest);
    }

    private static BankOperationResult DepositGoldLocked(
        Player player,
        AccountState state,
        int amount,
        GoldIncomeSource source)
    {
        int debtRepaid = Math.Min(state.CreditDebt, amount);
        int walletAmount = amount - debtRepaid;
        long newGold = (long)player.Gold + walletAmount;
        bool qualifies = source is
            GoldIncomeSource.NormalGameGold
            or GoldIncomeSource.SavingsInterest;
        long newEarned = qualifies
            ? (long)state.QualifyingEarned + amount
            : state.QualifyingEarned;
        long newIssuedInterest =
            source == GoldIncomeSource.SavingsInterest
                ? (long)state.SavingsInterestEarnedTotal + amount
                : state.SavingsInterestEarnedTotal;
        int positiveIncrease =
            PositiveBalanceIncrease(player.Gold, newGold);
        long newPrincipal = state.SavingsPrincipal;
        long newInterest = state.SavingsInterest;
        if (source == GoldIncomeSource.SavingsInterest)
        {
            newInterest += positiveIncrease;
        }
        else
        {
            newPrincipal += positiveIncrease;
        }

        if (newGold is > int.MaxValue or < int.MinValue
            || newEarned > int.MaxValue
            || newIssuedInterest > int.MaxValue
            || newPrincipal > int.MaxValue
            || newInterest > int.MaxValue)
        {
            return BankOperationResult.Fail(
                BankErrorCode.ArithmeticOverflow);
        }

        player.Gold = (int)newGold;
        state.CreditDebt -= debtRepaid;
        if (state.CreditDebt
            < GetMaximumDebt(player, GetStoredTier(state)))
        {
            state.CreditCeilingPending = 0;
        }
        if (state.CreditDebt == 0)
        {
            ResetDebtCycleLocked(state);
        }
        state.QualifyingEarned = (int)newEarned;
        state.SavingsInterestEarnedTotal =
            (int)newIssuedInterest;
        state.SavingsPrincipal = (int)newPrincipal;
        state.SavingsInterest = (int)newInterest;
        ReconcileSavingsComponentsLocked(player, state);
        BumpRevision(state);
        return BankOperationResult.Ok(walletAmount, debtRepaid);
    }

    private static BankOperationResult EnforceCreditCeilingLocked(
        Player player,
        AccountState state,
        CreditTier tier)
    {
        if (state.CreditPermanentlyClosed != 0
            || state.CreditDebt == 0
            || state.CreditCeilingPending != 0)
        {
            return BankOperationResult.Ok();
        }

        int maximumDebt = GetMaximumDebt(player, tier);
        if (maximumDebt <= 0 || state.CreditDebt < maximumDebt)
        {
            return BankOperationResult.Ok();
        }

        BankOperationResult closure =
            CloseCreditAtCeilingLocked(player, state);
        if (closure.Success)
        {
            ReconcileSavingsComponentsLocked(player, state);
            BumpRevision(state);
        }
        return closure;
    }

    private static BankOperationResult FinalizePendingCreditCeilingLocked(
        Player player,
        AccountState state)
    {
        if (state.CreditCeilingPending == 0)
        {
            return BankOperationResult.Ok();
        }

        CreditTier tier = GetStoredTier(state);
        int maximumDebt = GetMaximumDebt(player, tier);
        if (maximumDebt <= 0 || state.CreditDebt < maximumDebt)
        {
            state.CreditCeilingPending = 0;
            return BankOperationResult.Ok();
        }

        BankOperationResult closure =
            CloseCreditAtCeilingLocked(player, state);
        if (closure.Success)
        {
            ReconcileSavingsComponentsLocked(player, state);
        }
        return closure;
    }

    private static BankOperationResult CloseCreditAtCeilingLocked(
        Player player,
        AccountState state)
    {
        CreditTier tier = GetStoredTier(state);
        int maximumDebt = GetMaximumDebt(player, tier);
        int debtToCollect = Math.Min(state.CreditDebt, maximumDebt);
        if (debtToCollect <= 0)
        {
            state.CreditCeilingPending = 0;
            return BankOperationResult.Ok();
        }

        int positiveCollected = Math.Min(
            Math.Max(0, player.Gold),
            debtToCollect);
        player.Gold -= positiveCollected;
        int debtSettledByRelics = debtToCollect - positiveCollected;
        ConsumeSavingsLocked(state, positiveCollected);
        state.CreditDebt = 0;
        state.CreditTier = (int)CreditTier.None;
        state.CreditPermanentlyClosed = 1;
        state.CreditCeilingPending = 0;
        state.DebtCycleFloors = 0;
        state.DebtGraceUsed = 1;
        state.PendingRelicLiquidationDebt = debtSettledByRelics;

        return BankOperationResult.Ok(
            positiveCollected,
            secondaryAmount: debtToCollect);
    }

    private static void BeginDebtCycleIfNeededLocked(
        Player player,
        AccountState state)
    {
        if (state.CreditDebt != 0)
        {
            return;
        }

        if (state.DebtGraceUsed == 0)
        {
            state.DebtGraceUsed = 1;
            state.DebtCycleFloors = 0;
        }
        else
        {


            state.DebtCycleFloors =
                GetDebtGraceFloorCount(player);
        }
        state.LastDebtInterestCharge = 0;
        state.CreditCeilingPending = 0;
    }

    private static void ResetDebtCycleLocked(AccountState state)
    {
        state.DebtCycleFloors = 0;
        state.LastDebtInterestCharge = 0;
        state.CreditCeilingPending = 0;
    }

    private static void ReconcileSavingsComponentsLocked(
        Player player,
        AccountState state)
    {
        int positiveBalance = Math.Max(0, player.Gold);
        long tracked =
            (long)state.SavingsPrincipal + state.SavingsInterest;
        if (tracked < positiveBalance)
        {
            state.SavingsPrincipal = checked(
                state.SavingsPrincipal
                + (int)(positiveBalance - tracked));
        }
        else if (tracked > positiveBalance)
        {
            ConsumeSavingsLocked(
                state,
                checked((int)(tracked - positiveBalance)));
        }

        if (positiveBalance == 0)
        {
            state.SavingsPrincipal = 0;
            state.SavingsInterest = 0;
            state.SavingsTenths = 0;
        }
    }

    private static void ConsumeSavingsLocked(AccountState state, int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        int fromInterest = Math.Min(state.SavingsInterest, amount);
        state.SavingsInterest -= fromInterest;
        int remaining = amount - fromInterest;
        state.SavingsPrincipal =
            Math.Max(0, state.SavingsPrincipal - remaining);
        if (state.SavingsPrincipal == 0
            && state.SavingsInterest == 0)
        {
            state.SavingsTenths = 0;
        }
    }

    private static int PositiveBalanceIncrease(long before, long after)
    {
        long increase = Math.Max(0L, after) - Math.Max(0L, before);
        return increase <= 0 ? 0 : checked((int)increase);
    }

    private static int PositiveBalanceDecrease(long before, long after)
    {
        long decrease = Math.Max(0L, before) - Math.Max(0L, after);
        return decrease <= 0 ? 0 : checked((int)decrease);
    }

    private static CreditTier GetStoredTier(AccountState state)
        => (CreditTier)state.CreditTier;

    private static bool IsValidCardTier(CreditTier tier)
        => tier is >= CreditTier.BisaPoor and <= CreditTier.BisaTycoon;

    private static void BumpRevision(AccountState state)
    {
        state.Revision = state.Revision == int.MaxValue
            ? 1
            : state.Revision + 1;
    }
}
