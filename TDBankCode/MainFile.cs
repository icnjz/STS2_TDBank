using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using System.Threading;
using TDBank.TDBankCode.Banking;
using TDBank.TDBankCode.Integration;
using TDBank.TDBankCode.Networking;
using TDLib;

namespace TDBank.TDBankCode;


[ModInitializer(nameof(Initialize))]
public partial class MainFile : Node
{
    public const string ModId = "TDBank";

    private static int _initialized;

    public static bool IsOperational { get; private set; }

    public static MegaCrit.Sts2.Core.Logging.Logger Logger { get; } = new(ModId, MegaCrit.Sts2.Core.Logging.LogType.Generic);

    public static void Initialize()
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0)
        {
            return;
        }

        if (!TDLibMain.IsOperational)
        {
            Logger.Error(
                "TD Bank disabled itself because TDLib could not initialize on this game build.");
            return;
        }

        var harmony = new Harmony(ModId);
        try
        {
            harmony.PatchAll();
            BankStateStore.Register();
            BankNetwork.Initialize();
            BankRuntime.Initialize();
            IsOperational = true;
            Logger.Info("TD Bank initialized: compound savings, tiered credit, debt-ceiling liquidation, and questionable KK financial advice are online.");
        }
        catch (Exception exception)
        {
            harmony.UnpatchAll(ModId);
            IsOperational = false;
            Logger.Error(
                $"TD Bank disabled itself because this game build changed an API: {exception}");
        }
    }
}
