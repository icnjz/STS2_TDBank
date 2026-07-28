using MegaCrit.Sts2.Core.Multiplayer.Serialization;

namespace TDBank.TDBankCode.Banking;

public enum CreditTier
{
    None = 0,
    VisaPoor = 1,
    VisaMiddleClass = 2,
    VisaTycoon = 3,
}

public sealed class AccountState : IPacketSerializable
{
    public const int CurrentSchema = 6;

    public int Schema { get; set; } = CurrentSchema;

    public int QualifyingEarned { get; set; }

    public int QualificationInitialized { get; set; }

    public int SavingsPrincipal { get; set; }

    public int SavingsInterest { get; set; }

    public int SavingsTenths { get; set; }

    public int SavingsInterestTurns { get; set; }

    public int CreditTier { get; set; }

    public int CreditDebt { get; set; }

    public int LastDebtInterestCharge { get; set; }

    public int LastSavingsTurnToken { get; set; } = -1;

    public int LastDebtFloorToken { get; set; } = -1;

    public int DebtCycleFloors { get; set; }

    public int CreditCeilingPending { get; set; }

    public int BankAccountOpened { get; set; }

    public int ButtSalesCount { get; set; }

    public int SavingsInterestEarnedTotal { get; set; }

    public int DebtGraceUsed { get; set; }

    public int PendingRelicLiquidationDebt { get; set; }

    public int UnifiedSavingsInitialized { get; set; }

    public int CreditPermanentlyClosed { get; set; }

    public int StolenSavingsPrincipal { get; set; }

    public int StolenSavingsInterest { get; set; }

    public int StolenSavingsTenths { get; set; }

    public int Revision { get; set; }

    public AccountState Clone()
    {
        AccountState clone = new();
        clone.CopyFrom(this);
        return clone;
    }

    public void CopyFrom(AccountState other)
    {
        ArgumentNullException.ThrowIfNull(other);

        Schema = other.Schema;
        QualifyingEarned = other.QualifyingEarned;
        QualificationInitialized = other.QualificationInitialized;
        SavingsPrincipal = other.SavingsPrincipal;
        SavingsInterest = other.SavingsInterest;
        SavingsTenths = other.SavingsTenths;
        SavingsInterestTurns = other.SavingsInterestTurns;
        CreditTier = other.CreditTier;
        CreditDebt = other.CreditDebt;
        LastDebtInterestCharge = other.LastDebtInterestCharge;
        LastSavingsTurnToken = other.LastSavingsTurnToken;
        LastDebtFloorToken = other.LastDebtFloorToken;
        DebtCycleFloors = other.DebtCycleFloors;
        CreditCeilingPending = other.CreditCeilingPending;
        BankAccountOpened = other.BankAccountOpened;
        ButtSalesCount = other.ButtSalesCount;
        SavingsInterestEarnedTotal = other.SavingsInterestEarnedTotal;
        DebtGraceUsed = other.DebtGraceUsed;
        PendingRelicLiquidationDebt = other.PendingRelicLiquidationDebt;
        UnifiedSavingsInitialized = other.UnifiedSavingsInitialized;
        CreditPermanentlyClosed = other.CreditPermanentlyClosed;
        StolenSavingsPrincipal = other.StolenSavingsPrincipal;
        StolenSavingsInterest = other.StolenSavingsInterest;
        StolenSavingsTenths = other.StolenSavingsTenths;
        Revision = other.Revision;

        Normalize();
    }

    public void Normalize()
    {
        int sourceSchema = Schema;
        if (sourceSchema < 2)
        {



            SavingsInterestTurns = 0;
            LastSavingsTurnToken = -1;
        }

        if (sourceSchema < 4)
        {


            DebtCycleFloors = 0;
            LastDebtFloorToken = -1;
            LastDebtInterestCharge = 0;
        }

        if (sourceSchema < 5)
        {


            bool hasExistingBankActivity =
                QualifyingEarned > 0
                || QualificationInitialized != 0
                || SavingsPrincipal > 0
                || SavingsInterest > 0
                || SavingsTenths > 0
                || SavingsInterestTurns > 0
                || CreditTier != (int)Banking.CreditTier.None
                || CreditDebt > 0
                || UnifiedSavingsInitialized != 0
                || CreditPermanentlyClosed != 0
                || StolenSavingsPrincipal > 0
                || StolenSavingsInterest > 0
                || StolenSavingsTenths > 0;
            BankAccountOpened = hasExistingBankActivity ? 1 : 0;




            CreditPermanentlyClosed = 0;
            CreditCeilingPending = 0;
        }

        if (sourceSchema < 6)
        {




            QualifyingEarned = 0;
            QualificationInitialized = BankAccountOpened;




            SavingsInterestEarnedTotal = (int)Math.Min(
                int.MaxValue,
                (long)Math.Max(0, SavingsInterest)
                + Math.Max(0, StolenSavingsInterest));





            DebtGraceUsed =
                CreditDebt > 0 || CreditPermanentlyClosed != 0
                    ? 1
                    : 0;
            PendingRelicLiquidationDebt = 0;
        }




        Schema = CurrentSchema;
        QualifyingEarned = Math.Max(0, QualifyingEarned);
        QualificationInitialized = QualificationInitialized == 0 ? 0 : 1;
        SavingsPrincipal = Math.Max(0, SavingsPrincipal);
        SavingsInterest = Math.Max(0, SavingsInterest);
        SavingsTenths = Math.Max(0, SavingsTenths);
        SavingsInterestTurns = Math.Max(0, SavingsInterestTurns);
        Revision = Math.Max(0, Revision);

        long carriedWholeGold = SavingsTenths / 10L;
        SavingsTenths %= 10;

        long normalizedInterest = Math.Min(
            int.MaxValue - (long)SavingsPrincipal,
            SavingsInterest + carriedWholeGold);
        SavingsInterest = (int)Math.Max(0L, normalizedInterest);

        CreditTier = Math.Clamp(
            CreditTier,
            (int)Banking.CreditTier.None,
            (int)Banking.CreditTier.VisaTycoon);
        CreditDebt = Math.Max(0, CreditDebt);
        LastDebtInterestCharge = Math.Max(0, LastDebtInterestCharge);
        DebtCycleFloors = Math.Max(0, DebtCycleFloors);
        CreditCeilingPending = CreditCeilingPending == 0 ? 0 : 1;
        BankAccountOpened = BankAccountOpened == 0 ? 0 : 1;
        ButtSalesCount = Math.Max(0, ButtSalesCount);
        SavingsInterestEarnedTotal =
            Math.Max(0, SavingsInterestEarnedTotal);
        DebtGraceUsed = DebtGraceUsed == 0 ? 0 : 1;
        PendingRelicLiquidationDebt =
            Math.Max(0, PendingRelicLiquidationDebt);
        UnifiedSavingsInitialized = UnifiedSavingsInitialized == 0 ? 0 : 1;
        CreditPermanentlyClosed = CreditPermanentlyClosed == 0 ? 0 : 1;
        StolenSavingsPrincipal = Math.Max(0, StolenSavingsPrincipal);
        StolenSavingsInterest = Math.Max(0, StolenSavingsInterest);
        StolenSavingsTenths = Math.Max(0, StolenSavingsTenths);

        if (CreditPermanentlyClosed != 0)
        {
            CreditTier = (int)Banking.CreditTier.None;
            CreditDebt = 0;
            DebtCycleFloors = 0;
            CreditCeilingPending = 0;
        }


        else if (CreditDebt > 0 && CreditTier == (int)Banking.CreditTier.None)
        {
            CreditTier = (int)Banking.CreditTier.VisaPoor;
        }

        if (CreditDebt > 0)
        {
            DebtGraceUsed = 1;
        }

        if (CreditDebt == 0)
        {
            DebtCycleFloors = 0;
            CreditCeilingPending = 0;
        }
    }

    public void Serialize(PacketWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        Normalize();
        writer.WriteInt(Schema);
        writer.WriteInt(QualifyingEarned);
        writer.WriteInt(QualificationInitialized);
        writer.WriteInt(SavingsPrincipal);
        writer.WriteInt(SavingsInterest);
        writer.WriteInt(SavingsTenths);
        writer.WriteInt(SavingsInterestTurns);
        writer.WriteInt(CreditTier);
        writer.WriteInt(CreditDebt);
        writer.WriteInt(LastDebtInterestCharge);
        writer.WriteInt(LastSavingsTurnToken);
        writer.WriteInt(LastDebtFloorToken);
        writer.WriteInt(Revision);
        writer.WriteInt(UnifiedSavingsInitialized);
        writer.WriteInt(CreditPermanentlyClosed);
        writer.WriteInt(StolenSavingsPrincipal);
        writer.WriteInt(StolenSavingsInterest);
        writer.WriteInt(StolenSavingsTenths);
        writer.WriteInt(DebtCycleFloors);
        writer.WriteInt(CreditCeilingPending);
        writer.WriteInt(BankAccountOpened);
        writer.WriteInt(ButtSalesCount);
        writer.WriteInt(SavingsInterestEarnedTotal);
        writer.WriteInt(DebtGraceUsed);
        writer.WriteInt(PendingRelicLiquidationDebt);
    }

    public void Deserialize(PacketReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        Schema = reader.ReadInt();
        int sourceSchema = Schema;
        QualifyingEarned = reader.ReadInt();
        QualificationInitialized = reader.ReadInt();
        SavingsPrincipal = reader.ReadInt();
        SavingsInterest = reader.ReadInt();
        SavingsTenths = reader.ReadInt();
        SavingsInterestTurns = reader.ReadInt();
        CreditTier = reader.ReadInt();
        CreditDebt = reader.ReadInt();
        LastDebtInterestCharge = reader.ReadInt();
        LastSavingsTurnToken = reader.ReadInt();
        LastDebtFloorToken = reader.ReadInt();
        Revision = reader.ReadInt();
        UnifiedSavingsInitialized = reader.ReadInt();
        CreditPermanentlyClosed = reader.ReadInt();
        StolenSavingsPrincipal = reader.ReadInt();
        StolenSavingsInterest = reader.ReadInt();
        StolenSavingsTenths = reader.ReadInt();
        if (sourceSchema >= 4)
        {
            DebtCycleFloors = reader.ReadInt();
            CreditCeilingPending = reader.ReadInt();
        }
        else
        {
            DebtCycleFloors = 0;
            CreditCeilingPending = 0;
        }
        if (sourceSchema >= 5)
        {
            BankAccountOpened = reader.ReadInt();
            ButtSalesCount = reader.ReadInt();
        }
        else
        {
            BankAccountOpened = 0;
            ButtSalesCount = 0;
        }
        if (sourceSchema >= 6)
        {
            SavingsInterestEarnedTotal = reader.ReadInt();
            DebtGraceUsed = reader.ReadInt();
            PendingRelicLiquidationDebt = reader.ReadInt();
        }
        else
        {
            SavingsInterestEarnedTotal = 0;
            DebtGraceUsed = 0;
            PendingRelicLiquidationDebt = 0;
        }
        Normalize();
    }
}
