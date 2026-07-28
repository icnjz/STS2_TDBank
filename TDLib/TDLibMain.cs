using System.Reflection;
using System.Threading;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace TDLib;

[ModInitializer(nameof(Initialize))]
public static class TDLibMain
{
    public const string ModId = "TDLib";

    private static int _initialized;

    public static MegaCrit.Sts2.Core.Logging.Logger Logger { get; } =
        new(ModId, MegaCrit.Sts2.Core.Logging.LogType.Generic);

    public static void Initialize()
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0)
        {
            return;
        }

        Assembly assembly = typeof(TDLibMain).Assembly;
        new Harmony(ModId).PatchAll(assembly);
        Logger.Info(
            "TDLib initialized: isolated TD Bank player-save support is online.");
    }
}
