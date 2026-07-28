using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace TDLib.Saves;

[HarmonyPatch(
    typeof(MegaCritSerializerContext),
    "global::System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver.GetTypeInfo")]
[HarmonyAfter("BaseLib")]
internal static class JsonSaveTypeResolverPatch
{
    [HarmonyPostfix]
    private static void ResolveRegisteredType(
        MegaCritSerializerContext __instance,
        Type type,
        JsonSerializerOptions options,
        ref JsonTypeInfo? __result)
    {
        if (__result is null
            && JsonSaveTypeRegistry.TryResolve(
                type,
                __instance,
                options,
                out JsonTypeInfo? registered))
        {
            __result = registered;
        }

        if (__result is not null
            && type == typeof(SerializablePlayer))
        {
            PlayerSaveRegistry.AddJsonProperties(__result, options);
        }
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.ToSerializable))]
[HarmonyAfter("BaseLib")]
internal static class PlayerToSerializablePatch
{
    [HarmonyPostfix]
    private static void Capture(
        Player __instance,
        SerializablePlayer __result)
    {
        PlayerSaveRegistry.Capture(__instance, __result);
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.FromSerializable))]
[HarmonyAfter("BaseLib")]
internal static class PlayerFromSerializablePatch
{
    [HarmonyPostfix]
    private static void Restore(
        SerializablePlayer save,
        Player __result)
    {
        PlayerSaveRegistry.Restore(save, __result);
    }
}

[HarmonyPatch(typeof(SerializablePlayer), nameof(SerializablePlayer.Serialize))]
[HarmonyAfter("BaseLib")]
internal static class SerializablePlayerWritePatch
{
    [HarmonyPostfix]
    private static void Write(
        SerializablePlayer __instance,
        PacketWriter writer)
    {
        PlayerSaveRegistry.Write(__instance, writer);
    }
}

[HarmonyPatch(
    typeof(SerializablePlayer),
    nameof(SerializablePlayer.Deserialize))]
[HarmonyAfter("BaseLib")]
internal static class SerializablePlayerReadPatch
{
    [HarmonyPostfix]
    private static void Read(
        SerializablePlayer __instance,
        PacketReader reader)
    {
        PlayerSaveRegistry.Read(__instance, reader);
    }
}
