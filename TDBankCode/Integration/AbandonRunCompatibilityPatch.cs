using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Runs;
using TDBank.TDBankCode.Compatibility;
using TDBank.TDBankCode.Networking;
using TDBank.TDBankCode.UI;

namespace TDBank.TDBankCode.Integration;

[HarmonyPatch]
internal static class AbandonRunCompatibilityPatch
{
    private static MethodBase TargetMethod()
    {
        MethodInfo original = AccessTools.Method(
                typeof(CreatureCmd),
                "KillWithoutCheckingWinCondition",
                new[] { typeof(Creature), typeof(bool), typeof(int) })
            ?? throw new MissingMethodException(
                typeof(CreatureCmd).FullName,
                "KillWithoutCheckingWinCondition");
        return AccessTools.AsyncMoveNext(original)
            ?? throw new MissingMethodException(
                original.DeclaringType?.FullName,
                $"{original.Name}.MoveNext");
    }

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> AllowAbandonOutsideCombat(
        IEnumerable<CodeInstruction> instructions)
    {
        var codes = instructions.ToList();
        MethodInfo combatManagerInstance = AccessTools.PropertyGetter(
                typeof(CombatManager),
                nameof(CombatManager.Instance))
            ?? throw new MissingMethodException(
                typeof(CombatManager).FullName,
                "get_Instance");
        MethodInfo combatInProgress = AccessTools.PropertyGetter(
                typeof(CombatManager),
                nameof(CombatManager.IsInProgress))
            ?? throw new MissingMethodException(
                typeof(CombatManager).FullName,
                "get_IsInProgress");
        MethodInfo replacement = AccessTools.Method(
                typeof(AbandonRunCompatibilityPatch),
                nameof(IsCombatInProgressOrAbandoning))
            ?? throw new MissingMethodException(
                typeof(AbandonRunCompatibilityPatch).FullName,
                nameof(IsCombatInProgressOrAbandoning));

        bool replaced = false;
        for (int index = 0; index < codes.Count - 1; index++)
        {
            if (!codes[index].Calls(combatManagerInstance)
                || !codes[index + 1].Calls(combatInProgress))
            {
                continue;
            }



            codes[index].opcode = OpCodes.Call;
            codes[index].operand = replacement;
            codes[index + 1].opcode = OpCodes.Nop;
            codes[index + 1].operand = null;
            replaced = true;
            break;
        }

        if (!replaced && GameApiCompatibility.Uses64BitRng)
        {
            throw new InvalidOperationException(
                "TD Bank could not locate the multiplayer "
                + "outside-combat death guard.");
        }

        return codes;
    }

    private static bool IsCombatInProgressOrAbandoning()
    {
        return ShouldTreatAsCombatForDeathGuard(
            CombatManager.Instance.IsInProgress,
            RunManager.Instance.IsAbandoned);
    }

    internal static bool ShouldTreatAsCombatForDeathGuard(
        bool combatInProgress,
        bool isAbandoned)
        => combatInProgress || isAbandoned;
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.Abandon))]
internal static class AbandonRunUiCleanupPatch
{
    [HarmonyPrefix]
    private static void Before()
    {
        TryCloseBank();
        BankNetwork.ResetRunState();
        FloorTransitionGate.Reset();
    }

    internal static void TryCloseBank()
    {
        try
        {
            BankUiBridge.Close();
        }
        catch
        {

        }
    }
}

[HarmonyPatch(typeof(RunManager), "AbandonInternal")]
internal static class AbandonRunInternalUiCleanupPatch
{
    [HarmonyPrefix]
    private static void Before()
    {
        AbandonRunUiCleanupPatch.TryCloseBank();
        BankNetwork.ResetRunState();
        FloorTransitionGate.Reset();
    }
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.CleanUp))]
internal static class RunCleanupBankUiPatch
{
    [HarmonyPrefix]
    private static void Before()
    {
        AbandonRunUiCleanupPatch.TryCloseBank();
        BankNetwork.ResetRunState();
        FloorTransitionGate.Reset();
    }
}
