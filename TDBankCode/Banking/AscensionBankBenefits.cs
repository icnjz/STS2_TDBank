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
   int SavingsBonusBasisPoints,
   int SavingsBonusCap,
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
                200, 900, 2200,
                200, 1000, 2000,
                200,
                3,
                2199, 2499, 2799,
                0, 0,
                10, 200,
                5, 50,
                100, int.MaxValue),
            1 => Create(
                level,
                175, 850, 2100,
                225, 1050, 2100,
                200,
                3,
                2199, 2499, 2799,
                0, 0,
                10, 200,
                5, 50,
                100, int.MaxValue),
            2 => Create(
                level,
                150, 800, 2000,
                250, 1100, 2200,
                200,
                3,
                2199, 2499, 2799,
                0, 0,
                10, 200,
                5, 50,
                100, int.MaxValue),
            3 => Create(
                level,
                0, 750, 1900,
                300, 1200, 2400,
                250,
                6,
                1599, 1899, 2199,
                200, 20,
                8, 300,
                4, 80,
                250, 6),
            4 => Create(
                level,
                0, 700, 1750,
                325, 1300, 2600,
                250,
                6,
                1549, 1849, 2149,
                250, 25,
                8, 320,
                4, 85,
                300, 6),
            5 => Create(
                level,
                0, 650, 1600,
                350, 1400, 2800,
                260,
                7,
                1499, 1799, 2099,
                300, 30,
                8, 340,
                4, 90,
                350, 5),
            6 => Create(
                level,
                0, 600, 1450,
                375, 1500, 3000,
                270,
                8,
                1449, 1749, 2049,
                350, 35,
                7, 360,
                4, 95,
                400, 5),
            7 => Create(
                level,
                0, 550, 1300,
                400, 1600, 3200,
                280,
                9,
                1399, 1699, 1999,
                400, 40,
                7, 380,
                3, 100,
                450, 5),
            8 => Create(
                level,
                0, 500, 1150,
                450, 1750, 3500,
                290,
                10,
                1299, 1599, 1899,
                450, 45,
                6, 400,
                3, 110,
                500, 4),
            9 => Create(
                level,
                0, 450, 1000,
                500, 1900, 3800,
                300,
                11,
                1199, 1499, 1799,
                500, 50,
                5, 450,
                2, 125,
                600, 4),
            _ => Create(
                level,
                0, 400, 850,
                600, 2200, 4400,
                300,
                12,
                999, 1299, 1599,
                600, 60,
                5, 500,
                2, 150,
                750, 3),
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

    public long CalculateSavingsBonus(long positiveBalance)
    {
        if (positiveBalance <= 0
            || SavingsBonusBasisPoints <= 0
            || SavingsBonusCap <= 0)
        {
            return 0;
        }

        decimal raw =
            (decimal)positiveBalance * SavingsBonusBasisPoints / 10_000m;
        return Math.Min(SavingsBonusCap, (long)decimal.Floor(raw));
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
        int savingsBonusBasisPoints,
        int savingsBonusCap,
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
            savingsBonusBasisPoints,
            savingsBonusCap,
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
