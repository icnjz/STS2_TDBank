using System.Globalization;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Gold;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;
using TDBank.TDBankCode.Banking;
using TDBank.TDBankCode.Networking;
using TDBank.TDBankCode.UI;

namespace TDBank.TDBankCode.Integration;

public static class BankRuntime
{
    private static int _initialized;
    private static readonly object WarningGate = new();
    private static readonly Dictionary<ulong, (int Debt, int Floor, int Relics)>
        LastCeilingWarnings = new();

    public static void Initialize()
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0)
        {
            return;
        }

        BankUiBridge.SnapshotProvider = BuildUiSnapshot;
        BankUiBridge.OpenAccountRequested += SubmitOpenAccount;
        BankUiBridge.ApplyRequested += SubmitCardApplication;
        BankUiBridge.UpgradeRequested += SubmitCardApplication;
        BankUiBridge.ETransferRequested += SubmitETransfer;
        BankUiBridge.KidneySaleRequested += SubmitKidneySale;
        BankUiBridge.ButtSaleRequested += SubmitButtSale;

        RunManager.Instance.RunStarted += OnRunStarted;
        RunManager.Instance.ActEntered += OnActEntered;
    }

    private static void OnRunStarted(RunState runState)
    {
        BankNetwork.ResetRunState();
        FloorTransitionGate.Reset();
        lock (WarningGate)
        {
            LastCeilingWarnings.Clear();
        }

        foreach (Player player in runState.Players)
        {
            try
            {
                if (BankService.IsAccountOpened(player))
                {
                    BankOperationResult savingsInitialization =
                        BankService.InitializeUnifiedSavings(player);
                    if (!savingsInitialization.Success)
                    {
                        SafeLogWarning(
                            $"Could not initialize unified savings for player "
                            + $"{player.NetId}: {savingsInitialization.Error}");
                    }
                }

                BankOperationResult result = BankService.InitializeQualification(player);
                if (!result.Success && result.Error != BankErrorCode.AlreadyProcessed)
                {
                    SafeLogWarning(
                        $"Could not initialize qualification for player {player.NetId}: {result.Error}");
                }

                BankOperationResult legacyRepair =
                    BankService.RepairLegacyNegativeForeclosureBalance(
                        player);
                if (legacyRepair.Amount > 0
                    && LocalContext.NetId == player.NetId)
                {
                    SafeNotify(
                        "legacy_foreclosure_repaired",
                        isError: false,
                        legacyRepair.Amount);
                }

                CreditCeilingRelicLiquidationResult recoveredLiquidation =
                    LiquidateAndNotify(player, runState);
                if (recoveredLiquidation.DebtCleared > 0)
                {
                    SafeLogWarning(
                        $"Recovered TD Bank ceiling liquidation for player "
                        + $"{player.NetId}: debt "
                        + $"{recoveredLiquidation.DebtCleared}, relics "
                        + $"{recoveredLiquidation.RelicsRemoved}/"
                        + $"{recoveredLiquidation.RelicsRequested}.");
                }
            }
            catch (Exception exception)
            {
                SafeLogError(
                    $"Could not initialize qualification for player {player.NetId}: {exception}");
            }
        }

        _ = BankNetwork.RestoreLifecycleEpoch(runState);
        SafeRefresh();
    }

    private static void OnActEntered()
    {
        FloorTransitionGate.ReleaseAfterActEntered();
    }

    internal static void PrepareNativeGoldObservation(Player player)
    {
        try
        {
            _ = BankService.InitializeUnifiedSavings(player);
            _ = BankService.InitializeQualification(player);
        }
        catch (Exception exception)
        {
            SafeLogError(
                $"Could not prepare native gold observation for player "
                + $"{player.NetId}: {exception}");
        }
    }

    internal static void RecordNativeGoldGain(
        Player player,
        decimal exactAmount,
        bool wasStolenBack)
    {
        try
        {
            int amount = decimal.ToInt32(exactAmount);
            BankOperationResult result =
                BankService.RecordNativeGoldGainAmount(
                    player,
                    amount,
                    wasStolenBack);
            if (result.Error == BankErrorCode.InvalidAmount)
            {
                return;
            }
            if (!result.Success)
            {
                SafeLogWarning(
                    $"Could not record native gold for player "
                    + $"{player.NetId}: {result.Error}");
                return;
            }

            if (LocalContext.NetId == player.NetId)
            {
                SafeRefresh();
                RunState? warningRun =
                    RunManager.Instance.DebugOnlyGetState();
                if (warningRun is not null)
                {
                    MaybeWarnNextFloorLiquidation(
                        player,
                        warningRun);
                }
            }
        }
        catch (Exception exception)
        {
            SafeLogError(
                $"Could not observe native gold gain for player {player.NetId}: {exception}");
        }
    }

    internal static void RecordNativeGoldLoss(
        Player player,
        int beforeGold,
        GoldLossType goldLossType)
    {
        try
        {
            BankOperationResult result =
                BankService.RecordNativeGoldLoss(
                    player,
                    beforeGold,
                    goldLossType);
            if (!result.Success)
            {
                SafeLogWarning(
                    $"Could not reconcile native gold loss for player "
                    + $"{player.NetId}: {result.Error}");
                return;
            }

            if (result.SecondaryAmount > 0)
            {
                RunState? activeRun =
                    RunManager.Instance.DebugOnlyGetState();
                if (activeRun is not null
                    && activeRun.Players.Contains(player))
                {
                    _ = LiquidateAndNotify(player, activeRun);
                }
            }

            if (LocalContext.NetId == player.NetId)
            {
                SafeRefresh();
                if (result.SecondaryAmount > 0)
                {
                    SafeNotify(
                        "credit_ceiling_closed_notice",
                        isError: true,
                        result.SecondaryAmount);
                }
                RunState? warningRun =
                    RunManager.Instance.DebugOnlyGetState();
                if (warningRun is not null)
                {
                    MaybeWarnNextFloorLiquidation(
                        player,
                        warningRun);
                }
            }
        }
        catch (Exception exception)
        {
            SafeLogError(
                $"Could not observe native gold loss for player "
                + $"{player.NetId}: {exception}");
        }
    }

    internal static bool OnCompletedMapFloor(int floorToken)
    {
        try
        {
            RunState? runState = RunManager.Instance.DebugOnlyGetState();
            if (runState is null)
            {
                return false;
            }

            int localSavingsCredit = 0;
            int localInterestCharge = 0;
            int localCeilingCollection = 0;
            bool settledAnyAccount = false;
            foreach (Player player in runState.Players.OrderBy(
                         static player => player.NetId))
            {
                BankOperationResult savingsResult =
                    BankService.AccrueSavingsInterest(player, floorToken);
                settledAnyAccount |= savingsResult.Success;
                if (savingsResult.Success
                    && savingsResult.Amount > 0
                    && LocalContext.NetId == player.NetId)
                {
                    localSavingsCredit = savingsResult.Amount;
                }

                BankOperationResult result = BankService.AccrueDebtInterest(player, floorToken);
                settledAnyAccount |= result.Success;
                if (!result.Success)
                {
                    _ = BankService.ClearUnreturnedStolenGold(player);
                    continue;
                }

                if (result.Amount > 0
                    && LocalContext.NetId == player.NetId)
                {
                    localInterestCharge = result.Amount;
                }
                if (result.SecondaryAmount > 0
                    && LocalContext.NetId == player.NetId)
                {
                    localCeilingCollection = result.SecondaryAmount;
                }
                if (result.SecondaryAmount > 0)
                {
                    _ = LiquidateAndNotify(player, runState);
                }
                MaybeWarnNextFloorLiquidation(player, runState);
                _ = BankService.ClearUnreturnedStolenGold(player);
            }

            SafeRefresh();
            if (localSavingsCredit > 0)
            {
                SafeNotify(
                    "savings_interest_notice",
                    isError: false,
                    localSavingsCredit);
            }
            if (localInterestCharge > 0)
            {
                SafeNotify("debt_interest_notice", isError: true, localInterestCharge);
            }
            if (localCeilingCollection > 0)
            {
                SafeNotify(
                    "credit_ceiling_closed_notice",
                    isError: true,
                    localCeilingCollection);
            }
            return settledAnyAccount;
        }
        catch (Exception exception)
        {
            SafeLogError($"Could not settle completed-floor banking: {exception}");
            return false;
        }
    }

    private static BankUiSnapshot BuildUiSnapshot()
    {
        RunState? runState = RunManager.Instance.DebugOnlyGetState();
        Player? me = GetLocalPlayer(runState);
        if (runState is null || me is null)
        {
            return BankUiSnapshot.Empty with
            {
                IsBankingAvailable = false,
                UnavailableReason = BankUiText.Get("default_unavailable"),
            };
        }

        if (RunManager.Instance.NetService.Type is NetGameType.None or NetGameType.Replay)
        {
            return BankUiSnapshot.Empty with
            {
                IsBankingAvailable = false,
                UnavailableReason = BankUiText.Get("unavailable_mode"),
            };
        }

        AccountSnapshot snapshot = BankService.GetSnapshot(me);
        AccountState state = BankStateStore.Get(me);
        AscensionBankBenefits benefits =
            AscensionBankBenefits.For(me);
        BankCreditOffer[] creditOffers =
        [
            new(
                BankCreditTier.Starter,
                benefits.PoorQualification,
                benefits.PoorCreditLimit,
                benefits.GetMaximumDebt(CreditTier.VisaPoor),
                benefits.PoorDebtInterestBasisPoints),
            new(
                BankCreditTier.MiddleClass,
                benefits.MiddleClassQualification,
                benefits.MiddleClassCreditLimit,
                benefits.GetMaximumDebt(CreditTier.VisaMiddleClass),
                benefits.MiddleClassDebtInterestBasisPoints),
            new(
                BankCreditTier.NouveauRiche,
                benefits.TycoonQualification,
                benefits.TycoonCreditLimit,
                benefits.GetMaximumDebt(CreditTier.VisaTycoon),
                benefits.TycoonDebtInterestBasisPoints),
        ];
        var teammates = runState.Players
            .Where(player =>
                player.NetId != me.NetId
                && BankService.IsAccountOpened(player))
            .Select(player => new BankPeerOption(
                player.NetId.ToString(CultureInfo.InvariantCulture),
                GetPlayerDisplayName(runState, player),
                player.Gold))
            .ToArray();

        return new BankUiSnapshot
        {
            AscensionLevel = benefits.AscensionLevel,
            IsAccountOpened = snapshot.IsAccountOpened,
            SavingsBalance = snapshot.SavingsBalance,
            CreditBalance = snapshot.CreditBalance,
            TotalGoldEarned = snapshot.QualifyingEarned,
            SavingsPrincipal = snapshot.SavingsPrincipal,
            SavingsTenths = snapshot.SavingsTenths,
            SavingsInterestEarned =
                snapshot.SavingsInterestEarnedTotal,
            SavingsInterestTurns = state.SavingsInterestTurns,
            SavingsBaseRateBasisPoints =
                BankService.SavingsInterestPercent * 100,
            SavingsBonusRateBasisPoints =
                benefits.SavingsBonusBasisPoints,
            SavingsBonusCap = benefits.SavingsBonusCap,
            CreditOffers = creditOffers,
            CreditTier = ToUiTier(snapshot.CreditTier),
            CreditLimit = snapshot.CreditLimit,
            MaximumDebt = BankService.GetMaximumDebt(
                me,
                snapshot.CreditTier),
            CreditFloor = -BankService.GetMaximumDebt(
                me,
                snapshot.CreditTier),
            DebtCycleFloors = state.DebtCycleFloors,
            DebtGraceFloorsRemaining =
                BankService.GetDebtGraceFloorsRemaining(me),
            DebtGraceFloorCount = benefits.DebtGraceFloorCount,
            DebtInterestRateBasisPoints =
                BankService.GetDebtInterestBasisPoints(
                    me,
                    snapshot.CreditTier),
            LastDebtInterestCharge = state.LastDebtInterestCharge,
            IsBankrupt = snapshot.IsBankrupt,
            CurrentHp = me.Creature.CurrentHp,
            MaxHp = me.Creature.MaxHp,
            ButtSalesCount = snapshot.ButtSalesCount,
            TradableRelicCount = me.Relics.Count(
                CreditCeilingRelicLiquidationService.IsSafelySeizable),
            RelicGoldPerSeizure =
                benefits.RelicLiquidationGoldPerRelic,
            RelicSeizureCap =
                benefits.RelicLiquidationMaximumRelics == int.MaxValue
                    ? 0
                    : benefits.RelicLiquidationMaximumRelics,
            KidneyHpCost = benefits.KidneyHpCost,
            KidneyGoldValue = benefits.KidneyGoldValue,
            ButtHpCost = benefits.ButtHpCost,
            ButtGoldValue = benefits.ButtGoldValue,
            AreOrganSalesAvailable =
                !FloorTransitionGate.IsActive
                && RunManager.Instance.ActionQueueSynchronizer.CombatState
                    == ActionSynchronizerCombatState.NotInCombat,
            Teammates = teammates,
        };
    }

    private static string GetPlayerDisplayName(RunState runState, Player player)
    {
        int slot = runState.GetPlayerSlotIndex(player) + 1;
        try
        {
            return $"P{slot} · {player.Character.Title.GetFormattedText()}";
        }
        catch
        {
            return $"P{slot} · {player.Character.Id.Entry}";
        }
    }

    private static void SubmitOpenAccount()
    {
        BankNetwork.SubmitOpenAccount();
    }

    private static void SubmitCardApplication(BankCreditTier tier)
    {
        BankNetwork.SubmitApplyCard(tier switch
        {
            BankCreditTier.Starter => CreditTier.VisaPoor,
            BankCreditTier.MiddleClass => CreditTier.VisaMiddleClass,
            BankCreditTier.NouveauRiche => CreditTier.VisaTycoon,
            _ => CreditTier.None,
        });
    }

    private static void SubmitKidneySale(long requestedQuantity)
    {
        if (!TryConvertAmount(requestedQuantity, out int quantity))
        {
            return;
        }

        BankNetwork.SubmitSellKidneys(quantity);
    }

    private static void SubmitButtSale()
    {
        BankNetwork.SubmitSellButt();
    }

    private static void SubmitETransfer(BankETransferRequest request)
    {
        if (!TryConvertAmount(request.Amount, out int amount))
        {
            return;
        }

        if (!ulong.TryParse(
                request.RecipientId,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out ulong recipientId))
        {
            SafeNotify("invalid_teammate", isError: true);
            return;
        }

        BankNetwork.SubmitETransfer(recipientId, amount);
    }

    private static bool TryConvertAmount(long requestedAmount, out int amount)
    {
        if (requestedAmount is <= 0 or > int.MaxValue)
        {
            amount = 0;
            SafeNotify(
                "amount_range",
                isError: true,
                int.MaxValue.ToString("N0", CultureInfo.InvariantCulture));
            return false;
        }

        amount = (int)requestedAmount;
        return true;
    }

    private static BankCreditTier? ToUiTier(CreditTier tier)
    {
        return tier switch
        {
            CreditTier.None => null,
            CreditTier.VisaPoor => BankCreditTier.Starter,
            CreditTier.VisaMiddleClass => BankCreditTier.MiddleClass,
            CreditTier.VisaTycoon => BankCreditTier.NouveauRiche,
            _ => null,
        };
    }

    private static Player? GetLocalPlayer(RunState? runState)
    {
        ulong? localId = LocalContext.NetId;
        return runState is not null && localId.HasValue
            ? runState.GetPlayer(localId.Value)
            : null;
    }

    internal static void SafeRefresh()
    {
        try
        {
            BankUiBridge.Refresh();
        }
        catch (Exception exception)
        {
            SafeLogError($"Could not refresh TD Bank UI: {exception}");
        }
    }

    private static void MaybeWarnNextFloorLiquidation(
        Player player,
        RunState runState)
    {
        if (LocalContext.NetId != player.NetId)
        {
            return;
        }

        CreditCeilingWarning? warning =
            CreditCeilingRelicLiquidationService
                .GetNextFloorWarning(player, runState);
        if (warning is null)
        {
            lock (WarningGate)
            {
                LastCeilingWarnings.Remove(player.NetId);
            }
            return;
        }

        int relics = Math.Min(
            warning.Value.RelicsRequested,
            player.Relics.Count(
                CreditCeilingRelicLiquidationService.IsSafelySeizable));
        var key = (
            warning.Value.DebtAtRisk,
            runState.TotalFloor,
            relics);
        lock (WarningGate)
        {
            if (LastCeilingWarnings.TryGetValue(
                    player.NetId,
                    out var previous)
                && previous == key)
            {
                return;
            }

            LastCeilingWarnings[player.NetId] = key;
        }

        BankUiBridge.NotifyImportant(
            BankUiText.Get("credit_ceiling_warning_title"),
            BankUiText.Get(
                "credit_ceiling_warning_message",
                relics),
            danger: true);
    }

    private static CreditCeilingRelicLiquidationResult LiquidateAndNotify(
        Player player,
        RunState runState)
    {
        CreditCeilingRelicLiquidationResult result =
            CreditCeilingRelicLiquidationService
                .LiquidatePendingCreditCeiling(player, runState);
        if (result.DebtCleared > 0
            && LocalContext.NetId == player.NetId)
        {
            BankUiBridge.NotifyImportant(
                BankUiText.Get("credit_liquidation_title"),
                BankUiText.Get(
                    "credit_liquidation_result",
                    result.DebtCleared,
                    result.RelicsRequested,
                    result.RelicsRemoved),
                danger: true);
        }

        return result;
    }

    private static void SafeNotify(string key, bool isError, params object[] args)
    {
        try
        {
            BankUiBridge.Notify(BankUiText.Get(key, args), isError);
        }
        catch (Exception exception)
        {
            SafeLogError($"Could not show TD Bank notification '{key}': {exception}");
        }
    }

    private static void SafeLogWarning(string message)
    {
        try
        {
            MainFile.Logger.Warn(message);
        }
        catch
        {

        }
    }

    private static void SafeLogError(string message)
    {
        try
        {
            MainFile.Logger.Error(message);
        }
        catch
        {

        }
    }
}
