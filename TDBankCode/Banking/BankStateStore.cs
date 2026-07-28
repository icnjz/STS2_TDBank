using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using MegaCrit.Sts2.Core.Entities.Players;
using TDLib.Saves;

namespace TDBank.TDBankCode.Banking;

public static class BankStateStore
{
    private const string SaveFieldName = "TDBank_AccountState";
    private const string LegacyJsonPropertyName =
        "save_dict_TDBank.TDBankCode.Banking.AccountState";
    private const string LegacyEntryKey =
        "spirefield_Player_TDBank_AccountState";

    private static int _registered;

    private static readonly SavedPlayerField<AccountState> StateField =
        new(
            static _ => new AccountState(),
            SaveFieldName,
            LegacyJsonPropertyName,
            LegacyEntryKey);

    [SuppressMessage(
   "Usage",
   "CA2255:The 'ModuleInitializer' attribute is only intended to be used in application code",
   Justification = "The mod assembly must register TDLib save metadata before the first player snapshot freezes it.")]
    [ModuleInitializer]
    internal static void RegisterAtModuleLoad()
    {
        Register();
    }

    public static void Register()
    {
        if (Interlocked.Exchange(ref _registered, 1) != 0)
        {
            return;
        }

        JsonSaveTypeRegistry.RegisterObjectSaveType<AccountState>(
            JsonSaveTypeRegistry.Property<AccountState, int>(nameof(AccountState.Schema)),
            JsonSaveTypeRegistry.Property<AccountState, int>(nameof(AccountState.QualifyingEarned)),
            JsonSaveTypeRegistry.Property<AccountState, int>(nameof(AccountState.QualificationInitialized)),
            JsonSaveTypeRegistry.Property<AccountState, int>(nameof(AccountState.SavingsPrincipal)),
            JsonSaveTypeRegistry.Property<AccountState, int>(nameof(AccountState.SavingsInterest)),
            JsonSaveTypeRegistry.Property<AccountState, int>(nameof(AccountState.SavingsTenths)),
            JsonSaveTypeRegistry.Property<AccountState, int>(nameof(AccountState.SavingsInterestTurns)),
            JsonSaveTypeRegistry.Property<AccountState, int>(nameof(AccountState.CreditTier)),
            JsonSaveTypeRegistry.Property<AccountState, int>(nameof(AccountState.CreditDebt)),
            JsonSaveTypeRegistry.Property<AccountState, int>(nameof(AccountState.LastDebtInterestCharge)),
            JsonSaveTypeRegistry.Property<AccountState, int>(nameof(AccountState.LastSavingsTurnToken)),
            JsonSaveTypeRegistry.Property<AccountState, int>(nameof(AccountState.LastDebtFloorToken)),
            JsonSaveTypeRegistry.Property<AccountState, int>(nameof(AccountState.Revision)),
            JsonSaveTypeRegistry.Property<AccountState, int>(nameof(AccountState.UnifiedSavingsInitialized)),
            JsonSaveTypeRegistry.Property<AccountState, int>(nameof(AccountState.CreditPermanentlyClosed)),
            JsonSaveTypeRegistry.Property<AccountState, int>(nameof(AccountState.StolenSavingsPrincipal)),
            JsonSaveTypeRegistry.Property<AccountState, int>(nameof(AccountState.StolenSavingsInterest)),
            JsonSaveTypeRegistry.Property<AccountState, int>(nameof(AccountState.StolenSavingsTenths)),
            JsonSaveTypeRegistry.Property<AccountState, int>(nameof(AccountState.DebtCycleFloors)),
            JsonSaveTypeRegistry.Property<AccountState, int>(nameof(AccountState.CreditCeilingPending)),
            JsonSaveTypeRegistry.Property<AccountState, int>(nameof(AccountState.BankAccountOpened)),
            JsonSaveTypeRegistry.Property<AccountState, int>(nameof(AccountState.ButtSalesCount)),
            JsonSaveTypeRegistry.Property<AccountState, int>(nameof(AccountState.SavingsInterestEarnedTotal)),
            JsonSaveTypeRegistry.Property<AccountState, int>(nameof(AccountState.DebtGraceUsed)),
            JsonSaveTypeRegistry.Property<AccountState, int>(nameof(AccountState.PendingRelicLiquidationDebt)));


        _ = StateField.Name;
    }

    public static AccountState Get(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        Register();

        AccountState? state = StateField.Get(player);
        if (state is null)
        {
            state = new AccountState();
            StateField.Set(player, state);
        }

        state.Normalize();
        return state;
    }

    public static void Set(Player player, AccountState state)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(state);
        Register();

        StateField.Set(player, state.Clone());
    }
}
