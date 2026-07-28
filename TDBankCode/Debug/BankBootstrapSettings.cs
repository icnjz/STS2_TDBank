#if DEBUG
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes.Debug;
using MegaCrit.Sts2.Core.Rooms;
using TDBank.TDBankCode.Banking;

namespace TDBank.TDBankCode.Debug;

 public sealed class BankBootstrapSettings : IBootstrapSettings
{
    public CharacterModel Character => ModelDb.Character<Ironclad>();
    public RoomType RoomType => RoomType.Shop;
    public EncounterModel Encounter => null!;
    public EventModel Event => null!;
    public ActModel Act => ActModel.GetDefaultList()[0];
    public int Ascension => 0;
    public bool SaveRunHistory => false;
    public string? Seed => "TOWER-DEBT-SMOKE-TEST";
    public bool DoPreloading => true;
    public bool BootstrapInMultiplayer => false;
    public List<ModifierModel> Modifiers => [];
    public string? Language => "zhs";

    public async Task Setup(Player localPlayer)
    {
        await PlayerCmd.GainGold(
            BankService.TycoonQualification,
            localPlayer);
    }
}

[HarmonyPatch(typeof(BootstrapSettingsUtil), nameof(BootstrapSettingsUtil.Get))]
internal static class BankBootstrapSettingsPatch
{
    [HarmonyPostfix]
    private static void After(ref Type? __result)
    {
        if (CommandLineHelper.HasArg("bootstrap"))
        {
            __result = typeof(BankBootstrapSettings);
        }
    }
}
#endif
