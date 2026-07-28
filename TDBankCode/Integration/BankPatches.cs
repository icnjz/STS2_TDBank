using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Gold;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using TDBank.TDBankCode.Banking;
using TDBank.TDBankCode.Networking;

namespace TDBank.TDBankCode.Integration;

[HarmonyPatch(
   typeof(PlayerCmd),
   nameof(PlayerCmd.GainGold),
   typeof(decimal),
   typeof(Player),
   typeof(bool))]
internal static class NativeGoldInitializationPatch
{
    [HarmonyPrefix]
    private static void Before(Player player)
    {
        BankRuntime.PrepareNativeGoldObservation(player);
    }
}

[HarmonyPatch]
internal static class QualifyingGoldPatch
{
    private static MethodBase TargetMethod()
    {
        MethodInfo original = AccessTools.Method(
                typeof(PlayerCmd),
                nameof(PlayerCmd.GainGold),
                new[] { typeof(decimal), typeof(Player), typeof(bool) })
            ?? throw new MissingMethodException(
                typeof(PlayerCmd).FullName,
                nameof(PlayerCmd.GainGold));
        return AccessTools.AsyncMoveNext(original)
            ?? throw new MissingMethodException(
                original.DeclaringType?.FullName,
                $"{original.Name}.MoveNext");
    }

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> ObserveExactAward(
        IEnumerable<CodeInstruction> instructions,
        MethodBase __originalMethod)
    {
        var codes = instructions.ToList();
        Type stateMachine = __originalMethod.DeclaringType
            ?? throw new InvalidOperationException(
                "GainGold async state-machine type is unavailable.");
        FieldInfo playerField = AccessTools.Field(stateMachine, "player")
            ?? throw new MissingFieldException(stateMachine.FullName, "player");
        FieldInfo amountField = AccessTools.Field(stateMachine, "amount")
            ?? throw new MissingFieldException(stateMachine.FullName, "amount");
        FieldInfo stolenBackField =
            AccessTools.Field(stateMachine, "wasStolenBack")
            ?? throw new MissingFieldException(
                stateMachine.FullName,
                "wasStolenBack");
        MethodInfo goldSetter = AccessTools.PropertySetter(
                typeof(Player),
                nameof(Player.Gold))
            ?? throw new MissingMethodException(
                typeof(Player).FullName,
                "set_Gold");
        MethodInfo observer = AccessTools.Method(
                typeof(QualifyingGoldPatch),
                nameof(RecordExactAward))
            ?? throw new MissingMethodException(
                typeof(QualifyingGoldPatch).FullName,
                nameof(RecordExactAward));

        int[] matches = codes
            .Select((instruction, index) => (instruction, index))
            .Where(pair => pair.instruction.Calls(goldSetter))
            .Select(pair => pair.index)
            .ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidOperationException(
                "TD Bank expected exactly one Player.Gold assignment in "
                + $"GainGold.MoveNext, but found {matches.Length}.");
        }

        int insertionIndex = matches[0] + 1;
        codes.InsertRange(
            insertionIndex,
            new[]
            {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldfld, playerField),
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldfld, amountField),
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldfld, stolenBackField),
                new CodeInstruction(OpCodes.Call, observer),
            });
        return codes;
    }

    private static void RecordExactAward(
        Player player,
        decimal exactAmount,
        bool wasStolenBack)
    {
        BankRuntime.RecordNativeGoldGain(
            player,
            exactAmount,
            wasStolenBack);
    }
}

[HarmonyPatch(typeof(MerchantEntry), nameof(MerchantEntry.EnoughGold), MethodType.Getter)]
internal static class MerchantCreditAvailabilityPatch
{
    private static readonly AccessTools.FieldRef<MerchantEntry, Player> PlayerRef =
        AccessTools.FieldRefAccess<MerchantEntry, Player>("_player");

    [HarmonyPostfix]
    private static void After(MerchantEntry __instance, ref bool __result)
    {
        if (__result)
        {
            return;
        }

        Player player = PlayerRef(__instance);
        __result = BankService.GetPurchasingPower(player) >= __instance.Cost;
    }
}

[HarmonyPatch(typeof(PlayerCmd), nameof(PlayerCmd.LoseGold))]
internal static class CreditBackedSpendingPatch
{
    [HarmonyPrefix]
    private static void Before(
        decimal amount,
        Player player,
        GoldLossType goldLossType,
        out int __state)
    {
        _ = BankService.InitializeUnifiedSavings(player);
        _ = BankService.InitializeQualification(player);
        __state = player.Gold;

        if (goldLossType != GoldLossType.Spent
            || amount <= 0
            || amount > int.MaxValue)
        {
            return;
        }

        BankOperationResult result =
            MerchantCreditLedger.AdvanceCashShortfall(player, (int)amount);
        if (result.Success && result.Amount > 0)
        {
            BankRuntime.SafeRefresh();
        }
    }

    [HarmonyPostfix]
    private static void After(
        Player player,
        GoldLossType goldLossType,
        int __state,
        ref Task __result)
    {
        __result = ObserveCompletion(
            __result,
            player,
            __state,
            goldLossType);
    }

    private static async Task ObserveCompletion(
        Task original,
        Player player,
        int beforeGold,
        GoldLossType goldLossType)
    {
        await original;





        player.Gold = PreserveNegativeBalanceAfterNativeLoss(
            beforeGold,
            player.Gold);

        BankRuntime.RecordNativeGoldLoss(
            player,
            beforeGold,
            goldLossType);
    }

    internal static int PreserveNegativeBalanceAfterNativeLoss(
        int beforeGold,
        int afterGold)
    {
        return beforeGold < 0 && afterGold > beforeGold
            ? beforeGold
            : afterGold;
    }
}

internal static class MerchantCreditLedger
{
    internal static BankOperationResult AdvanceCashShortfall(Player player, int charged)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (charged <= 0)
        {
            return BankOperationResult.Fail(BankErrorCode.InvalidAmount);
        }

        int shortfall = charged - Math.Max(0, player.Gold);
        return shortfall <= 0
            ? BankOperationResult.Ok()
            : BankService.AdvanceCreditForPurchase(player, shortfall);
    }
}

[HarmonyPatch(typeof(RunManager), "ExitCurrentRoom")]
internal static class CompletedMapFloorBankingPatch
{
    private sealed record ExitObservation(
        RunState RunState,
        AbstractRoom Room,
        int FloorToken,
        bool HistoryMatches);

    [HarmonyPrefix]
    private static void Before(
        RunManager __instance,
        out ExitObservation? __state)
    {
        RunState? runState = __instance.DebugOnlyGetState();
        AbstractRoom? room = runState?.CurrentRoom;
        if (runState is null
            || room is null
            || runState.CurrentRoomCount != 1
            || room is MapRoom
            || room.RoomType == RoomType.Map
            || runState.TotalFloor <= 0
            || __instance.IsCleaningUp
            || __instance.IsAbandoned
            || !__instance.IsInProgress
            || __instance.NetService.Type
                is NetGameType.None or NetGameType.Replay)
        {
            __state = null;
            return;
        }

        var firstHistoryRoom =
            runState.CurrentMapPointHistoryEntry?.Rooms.FirstOrDefault();
        bool historyMatches = firstHistoryRoom is not null
            && firstHistoryRoom.RoomType == room.RoomType
            && firstHistoryRoom.ModelId == room.ModelId;
        if (!historyMatches
            || !FloorTransitionGate.TryBegin(
                runState.TotalFloor))
        {
            __state = null;
            return;
        }

        __state = new ExitObservation(
            runState,
            room,
            runState.TotalFloor,
            historyMatches);
    }

    [HarmonyPostfix]
    private static void After(
        ExitObservation? __state,
        ref Task<AbstractRoom?> __result)
    {
        __result = ObserveCompletion(__result, __state);
    }

    private static async Task<AbstractRoom?> ObserveCompletion(
        Task<AbstractRoom?> original,
        ExitObservation? observation)
    {
        if (observation is null)
        {
            return await original;
        }

        AbstractRoom? exitedRoom = null;
        try
        {
            exitedRoom = await original;
            bool shouldSettle =
                ReferenceEquals(exitedRoom, observation.Room)
                && ShouldSettleCompletedMapFloor(
                    wasBaseRoom: true,
                    isMapRoom: exitedRoom is MapRoom
                        || exitedRoom.RoomType == RoomType.Map,
                    floorToken: observation.FloorToken,
                    historyMatches: observation.HistoryMatches,
                    sameActiveRun:
                        ReferenceEquals(
                            RunManager.Instance.DebugOnlyGetState(),
                            observation.RunState)
                        && RunManager.Instance.IsInProgress
                        && !RunManager.Instance.IsCleaningUp
                        && !RunManager.Instance.IsAbandoned);
            if (!shouldSettle)
            {
                return exitedRoom;
            }






            if (BankRuntime.OnCompletedMapFloor(observation.FloorToken))
            {
                BankNetwork.AdvanceLifecycleEpoch();
            }
            return exitedRoom;
        }
        finally
        {
            FloorTransitionGate.End(observation.FloorToken);
        }
    }

    internal static bool ShouldSettleCompletedMapFloor(
        bool wasBaseRoom,
        bool isMapRoom,
        int floorToken,
        bool historyMatches,
        bool sameActiveRun)
    {
        return wasBaseRoom
            && !isMapRoom
            && floorToken > 0
            && historyMatches
            && sameActiveRun;
    }
}

[HarmonyPatch(typeof(ActChangeSynchronizer), "MoveToNextAct")]
internal static class NextActFloorBankingPatch
{
    [HarmonyPrefix]
    private static void Before()
    {
        RunManager runManager = RunManager.Instance;
        RunState? runState = runManager.DebugOnlyGetState();
        AbstractRoom? room = runState?.CurrentRoom;
        if (runState is null
            || room is null
            || runState.CurrentRoomCount != 1
            || room is MapRoom
            || room.RoomType == RoomType.Map
            || runState.TotalFloor <= 0
            || runManager.IsCleaningUp
            || runManager.IsAbandoned
            || !runManager.IsInProgress
            || runManager.NetService.Type
                is NetGameType.None or NetGameType.Replay)
        {
            return;
        }

        var firstHistoryRoom =
            runState.CurrentMapPointHistoryEntry?.Rooms.FirstOrDefault();
        bool historyMatches = firstHistoryRoom is not null
            && firstHistoryRoom.RoomType == room.RoomType
            && firstHistoryRoom.ModelId == room.ModelId;
        if (!historyMatches
            || !FloorTransitionGate.TryBegin(runState.TotalFloor))
        {
            return;
        }

        FloorTransitionGate.HoldUntilActEntered(runState.TotalFloor);
        if (BankRuntime.OnCompletedMapFloor(runState.TotalFloor))
        {
            BankNetwork.AdvanceLifecycleEpoch();
        }
    }
}

[HarmonyPatch(typeof(RunManager), "SendPostActionChecksum")]
internal static class RejectedBankActionChecksumPatch
{
    [HarmonyPrefix]
    private static bool Before(GameAction action)
    {
        return action is not TDBankOperationGameAction bankAction
            || bankAction.IsExecutionEpochCurrent;
    }
}
