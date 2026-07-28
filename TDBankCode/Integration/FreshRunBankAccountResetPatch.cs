using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Runs;
using TDBank.TDBankCode.Banking;

namespace TDBank.TDBankCode.Integration;

[HarmonyPatch]
internal static class FreshRunBankAccountResetPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.DeclaredMethod(
            typeof(RunManager),
            nameof(RunManager.SetUpNewSingleplayer))
            ?? throw new MissingMethodException(
                typeof(RunManager).FullName,
                nameof(RunManager.SetUpNewSingleplayer));
        yield return AccessTools.DeclaredMethod(
            typeof(RunManager),
            nameof(RunManager.SetUpNewMultiplayer))
            ?? throw new MissingMethodException(
                typeof(RunManager).FullName,
                nameof(RunManager.SetUpNewMultiplayer));
    }

    [HarmonyPostfix]
    private static void AfterNewRunSetup(RunState state)
    {
        ResetFreshRunAccounts(state);
    }

    internal static void ResetFreshRunAccounts(RunState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        foreach (var player in state.Players)
        {
            BankStateStore.Set(player, new AccountState());
        }
    }
}
