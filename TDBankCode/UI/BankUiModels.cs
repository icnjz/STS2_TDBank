using System;
using System.Collections.Generic;

namespace TDBank.TDBankCode.UI;

public enum BankCreditTier
{
    Starter,
    MiddleClass,
    NouveauRiche,
}

public sealed record BankPeerOption(string Id, string DisplayName, long? Gold = null);

public sealed record BankCreditOffer(
   BankCreditTier Tier,
   int QualificationThreshold,
   int CreditLimit,
   int MaximumDebt,
   int InterestRateBasisPoints);

public sealed record BankUiSnapshot
{
    public static BankUiSnapshot Empty { get; } = new();

    public int AscensionLevel { get; init; }
    public bool IsAccountOpened { get; init; }
    public long SavingsBalance { get; init; }
    public long CreditBalance { get; init; }
    public long TotalGoldEarned { get; init; }

    public long SavingsPrincipal { get; init; }

    public int SavingsTenths { get; init; }

    public long SavingsInterestEarned { get; init; }

    public int SavingsInterestTurns { get; init; }
    public BankCreditTier? CreditTier { get; init; }

    public int SavingsBaseRateBasisPoints { get; init; } = 1_000;

    public int SavingsBonusRateBasisPoints { get; init; }

    public int SavingsBonusCap { get; init; }

    public IReadOnlyList<BankCreditOffer> CreditOffers { get; init; } =
   new BankCreditOffer[]
   {
            new(BankCreditTier.Starter, 200, 200, 400, 2_199),
            new(BankCreditTier.MiddleClass, 900, 1_000, 2_000, 2_499),
            new(BankCreditTier.NouveauRiche, 2_200, 2_000, 4_000, 2_799),
   };

    public long CreditLimit { get; init; }

    public long MaximumDebt { get; init; }

    public long CreditFloor { get; init; }

    public int DebtCycleFloors { get; init; }

    public int DebtGraceFloorCount { get; init; } = 3;

    public int DebtGraceFloorsRemaining { get; init; }

    public int DebtInterestRateBasisPoints { get; init; }

    public long LastDebtInterestCharge { get; init; }
    public bool IsBankrupt { get; init; }
    public int CurrentHp { get; init; }
    public int MaxHp { get; init; }
    public int ButtSalesCount { get; init; }
    public int TradableRelicCount { get; init; }

    public int RelicGoldPerSeizure { get; init; } = 100;

    public int RelicSeizureCap { get; init; }

    public int KidneyHpCost { get; init; } = 10;
    public int KidneyGoldValue { get; init; } = 200;
    public int ButtHpCost { get; init; } = 5;
    public int ButtGoldValue { get; init; } = 50;
    public bool AreOrganSalesAvailable { get; init; } = true;

    public bool IsBankingAvailable { get; init; } = true;
    public string? UnavailableReason { get; init; }
    public IReadOnlyList<BankPeerOption> Teammates { get; init; } = Array.Empty<BankPeerOption>();

    public long Debt => Math.Max(0, -CreditBalance);
    public long EstimatedNextDebtInterest
    {
        get
        {
            if (Debt == 0
                || DebtGraceFloorsRemaining > 0
                || DebtInterestRateBasisPoints <= 0)
            {
                return 0;
            }

            long remainingBeforeCeiling =
                Math.Max(0, MaximumDebt - Debt);
            long compounded = Math.Max(
                1,
                (long)Math.Ceiling(
                    Debt * (decimal)DebtInterestRateBasisPoints / 10_000m));
            return Math.Min(compounded, remainingBeforeCeiling);
        }
    }
}

public readonly record struct BankETransferRequest(
    string RecipientId,
    long Amount);
