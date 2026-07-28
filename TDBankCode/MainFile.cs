using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using TDBank.TDBankCode.Banking;
using TDBank.TDBankCode.Integration;
using TDBank.TDBankCode.Networking;

namespace TDBank.TDBankCode;


[ModInitializer(nameof(Initialize))]
public partial class MainFile : Node
{
    public const string ModId = "TDBank";

    public static MegaCrit.Sts2.Core.Logging.Logger Logger { get; } = new(ModId, MegaCrit.Sts2.Core.Logging.LogType.Generic);

    public static void Initialize()
    {
        BankStateStore.Register();
        BankNetwork.Initialize();
        BankRuntime.Initialize();

        Harmony harmony = new(ModId);
        harmony.PatchAll();
        Logger.Info("TD Bank initialized: compound savings, tiered credit, debt-ceiling liquidation, and questionable KK financial advice are online.");
    }
}
