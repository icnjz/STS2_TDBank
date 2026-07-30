using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Runs;

namespace TDBank.TDBankCode.Banking;

public readonly record struct AscensionBankBenefits(
   int AscensionLevel,
   int PoorQualification,
   int MiddleClassQualification,
   int TycoonQualification,
   int PoorCreditLimit,
   int MiddleClassCreditLimit,
   int TycoonCreditLimit,
   int MaximumDebtPercent,
   int DebtGraceFloorCount,
   int PoorDebtInterestBasisPoints,
   int MiddleClassDebtInterestBasisPoints,
   int TycoonDebtInterestBasisPoints,
   int SavingsInterestBasisPoints,
   int SavingsInterestCap,
   int KidneyHpCost,
   int KidneyGoldValue,
   int ButtHpCost,
   int ButtGoldValue,
   int RelicLiquidationGoldPerRelic,
   int RelicLiquidationMaximumRelics)
{
    public static AscensionBankBenefits For(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        IRunState? runState = player.RunState;
        return runState is null
            ? ForAscension(0)
            : For(runState);
    }

    public static AscensionBankBenefits For(IRunState runState)
    {
        ArgumentNullException.ThrowIfNull(runState);
        return ForAscension(runState.AscensionLevel);
    }

    public static AscensionBankBenefits ForAscension(int ascensionLevel)
    {
        int level = Math.Clamp(ascensionLevel, 0, 10);
        return level switch
        {
            0 => Create(
                level,
                150, 600, 1600,
                200, 700, 1200,
                200,
                3,
                2199, 2499, 2799,
                150, 10,
                10, 200,
                5, 50,
                100, int.MaxValue),
            1 => Create(
                level,
                175, 650, 1700,
                200, 700, 1200,
                200,
                3,
                2199, 2499, 2799,
                150, 10,
                10, 200,
                5, 50,
                100, int.MaxValue),
            2 => Create(
                level,
                200, 700, 1800,
                200, 650, 1100,
                190,
                3,
                2299, 2599, 2899,
                125, 8,
                10, 200,
                5, 50,
                100, int.MaxValue),
            3 => Create(
                level,
                225, 800, 2000,
                200, 650, 1100,
                190,
                2,
                2299, 2599, 2899,
                125, 8,
                10, 200,
                5, 50,
                100, int.MaxValue),
            4 => Create(
                level,
                250, 900, 2200,
                200, 600, 1000,
                180,
                2,
                2399, 2699, 2999,
                100, 7,
                10, 200,
                5, 50,
                100, int.MaxValue),
            5 => Create(
                level,
                275, 1000, 2400,
                200, 600, 1000,
                180,
                2,
                2399, 2699, 2999,
                100, 7,
                10, 200,
                5, 50,
                100, int.MaxValue),
            6 => Create(
                level,
                300, 1100, 2600,
                175, 550, 900,
                170,
                1,
                2499, 2799, 3099,
                75, 6,
                10, 200,
                5, 50,
                100, int.MaxValue),
            7 => Create(
                level,
                325, 1200, 2800,
                175, 550, 900,
                170,
                1,
                2499, 2799, 3099,
                75, 6,
                10, 200,
                5, 50,
                100, int.MaxValue),
            8 => Create(
                level,
                350, 1300, 3000,
                150, 500, 800,
                160,
                1,
                2599, 2899, 3199,
                50, 5,
                10, 200,
                5, 50,
                100, int.MaxValue),
            9 => Create(
                level,
                375, 1400, 3200,
                150, 450, 750,
                150,
                1,
                2699, 2999, 3299,
                50, 5,
                10, 200,
                5, 50,
                100, int.MaxValue),
            _ => Create(
                level,
                400, 1500, 3500,
                150, 450, 750,
                150,
                1,
                2699, 2999, 3299,
                50, 5,
                10, 200,
                5, 50,
                100, int.MaxValue),
        };
    }

    public int GetQualificationThreshold(CreditTier tier)
    {
        return tier switch
        {
            CreditTier.None => 0,
            CreditTier.VisaPoor => PoorQualification,
            CreditTier.VisaMiddleClass => MiddleClassQualification,
            CreditTier.VisaTycoon => TycoonQualification,
            _ => throw UnknownTier(tier),
        };
    }

    public int GetCreditLimit(CreditTier tier)
    {
        return tier switch
        {
            CreditTier.None => 0,
            CreditTier.VisaPoor => PoorCreditLimit,
            CreditTier.VisaMiddleClass => MiddleClassCreditLimit,
            CreditTier.VisaTycoon => TycoonCreditLimit,
            _ => throw UnknownTier(tier),
        };
    }

    public int GetMaximumDebt(CreditTier tier)
    {
        long scaled =
            (long)GetCreditLimit(tier) * MaximumDebtPercent / 100L;
        return checked((int)scaled);
    }

    public int GetDebtInterestBasisPoints(CreditTier tier)
    {
        return tier switch
        {
            CreditTier.None => 0,
            CreditTier.VisaPoor => PoorDebtInterestBasisPoints,
            CreditTier.VisaMiddleClass =>
                MiddleClassDebtInterestBasisPoints,
            CreditTier.VisaTycoon => TycoonDebtInterestBasisPoints,
            _ => throw UnknownTier(tier),
        };
    }

    public long CalculateSavingsInterest(long positiveBalance)
    {
        if (positiveBalance <= 0
            || SavingsInterestBasisPoints <= 0
            || SavingsInterestCap <= 0)
        {
            return 0;
        }

        decimal raw =
            (decimal)positiveBalance * SavingsInterestBasisPoints / 10_000m;
        return Math.Min(SavingsInterestCap, (long)decimal.Floor(raw));
    }

    private static AscensionBankBenefits Create(
        int level,
        int poorQualification,
        int middleQualification,
        int tycoonQualification,
        int poorLimit,
        int middleLimit,
        int tycoonLimit,
        int maximumDebtPercent,
        int graceFloors,
        int poorInterestBasisPoints,
        int middleInterestBasisPoints,
        int tycoonInterestBasisPoints,
        int savingsInterestBasisPoints,
        int savingsInterestCap,
        int kidneyHpCost,
        int kidneyGoldValue,
        int buttHpCost,
        int buttGoldValue,
        int relicGoldPerRelic,
        int relicMaximum)
        => new(
            level,
            poorQualification,
            middleQualification,
            tycoonQualification,
            poorLimit,
            middleLimit,
            tycoonLimit,
            maximumDebtPercent,
            graceFloors,
            poorInterestBasisPoints,
            middleInterestBasisPoints,
            tycoonInterestBasisPoints,
            savingsInterestBasisPoints,
            savingsInterestCap,
            kidneyHpCost,
            kidneyGoldValue,
            buttHpCost,
            buttGoldValue,
            relicGoldPerRelic,
            relicMaximum);

    private static ArgumentOutOfRangeException UnknownTier(CreditTier tier)
        => new(
            nameof(tier),
            tier,
            "Unknown credit tier.");
}
