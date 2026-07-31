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

    public static bool IsOperational { get; private set; }

    public static MegaCrit.Sts2.Core.Logging.Logger Logger { get; } =
        new(ModId, MegaCrit.Sts2.Core.Logging.LogType.Generic);

    public static void Initialize()
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0)
        {
            return;
        }

        Assembly assembly = typeof(TDLibMain).Assembly;
        var harmony = new Harmony(ModId);
        try
        {
            harmony.PatchAll(assembly);
            IsOperational = true;
            Logger.Info(
                "TDLib initialized: isolated TD Bank player-save support is online.");
        }
        catch (Exception exception)
        {
            harmony.UnpatchAll(ModId);
            IsOperational = false;
            Logger.Error(
                $"TDLib disabled itself because this game build changed an API: {exception}");
        }
    }
}
