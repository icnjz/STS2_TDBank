using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;

namespace TDBank.TDBankCode.UI;

[HarmonyPatch(typeof(ActiveScreenContext), nameof(ActiveScreenContext.GetCurrentScreen))]
internal static class ActiveScreenBankPatch
{
    [HarmonyPostfix]
    private static void After(ref IScreenContext? __result)
    {
        if (BankUiBridge.VisibleScreen is { } bank)
        {
            __result = bank;
        }
    }
}
