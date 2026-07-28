using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Events;
using TDBank.TDBankCode.Banking;

namespace TDBank.TDBankCode.Integration;

[HarmonyPatch]
internal static class EventCreditAvailabilityPatch
{
    private const int ExpectedTargetCount = 16;

    private readonly record struct LambdaTarget(
        Type EventType,
        string? NestedTypeName,
        string MethodName);

    private readonly record struct DirectTarget(
        Type EventType,
        string MethodName,
        int ExpectedGoldGetters);

    private static readonly LambdaTarget[] LambdaTargets =
    {
        new(
            typeof(CrystalSphere),
            "<>c",
            "<IsAllowed>b__11_0"),
        new(
            typeof(FakeMerchant),
            "<>c",
            "<IsAllowed>b__20_0"),
        new(
            typeof(EndlessConveyor),
            "<>c",
            "<IsAllowed>b__0_0"),
        new(
            typeof(RanwidTheElder),
            "<>c",
            "<IsAllowed>b__14_1"),
        new(
            typeof(LuminousChoir),
            "<>c__DisplayClass0_0",
            "<IsAllowed>b__0"),
        new(
            typeof(TeaMaster),
            "<>c",
            "<IsAllowed>b__4_0"),
        new(
            typeof(WaterloggedScriptorium),
            "<>c",
            "<IsAllowed>b__2_0"),
        new(
            typeof(WelcomeToWongos),
            "<>c",
            "<IsAllowed>b__14_0"),
        new(
            typeof(WhisperingHollow),
            "<>c",
            "<IsAllowed>b__2_0"),
        new(
            typeof(ZenWeaver),
            null,
            "<IsAllowed>b__3_0"),
    };

    private static readonly DirectTarget[] DirectTargets =
    {
        new(
            typeof(EndlessConveyor),
            "GenerateGrabSomethingOffTheBeltOption",
            1),
        new(typeof(LuminousChoir), "GenerateInitialOptions", 1),
        new(typeof(TeaMaster), "GenerateInitialOptions", 2),
        new(typeof(WaterloggedScriptorium), "GenerateInitialOptions", 2),
        new(typeof(WelcomeToWongos), "GenerateInitialOptions", 3),
        new(typeof(ZenWeaver), "GenerateInitialOptions", 1),
    };

    private static IEnumerable<MethodBase> TargetMethods()
        => GetValidatedTargets();

    internal static IReadOnlyList<MethodBase> GetValidatedTargets()
    {
        var methods = new List<MethodBase>(ExpectedTargetCount);

        foreach (LambdaTarget target in LambdaTargets)
        {
            Type holder = target.NestedTypeName is null
                ? target.EventType
                : AccessTools.Inner(
                    target.EventType,
                    target.NestedTypeName)
                    ?? throw new MissingMemberException(
                        target.EventType.FullName,
                        target.NestedTypeName);
            MethodInfo method = AccessTools.DeclaredMethod(
                    holder,
                    target.MethodName,
                    new[] { typeof(Player) })
                ?? throw new MissingMethodException(
                    holder.FullName,
                    target.MethodName);
            methods.Add(method);
        }

        foreach (DirectTarget target in DirectTargets)
        {
            MethodInfo method = AccessTools.DeclaredMethod(
                    target.EventType,
                    target.MethodName,
                    Type.EmptyTypes)
                ?? throw new MissingMethodException(
                    target.EventType.FullName,
                    target.MethodName);
            methods.Add(method);
        }

        if (methods.Count != ExpectedTargetCount
            || methods.Distinct().Count() != ExpectedTargetCount)
        {
            throw new InvalidOperationException(
                "TD Bank expected exactly 16 distinct public-beta event "
                + $"affordability targets, but resolved {methods.Count}.");
        }

        return methods;
    }

    internal static int SpendableGold(Player player)
    {
        long purchasingPower = BankService.GetPurchasingPower(player);
        return (int)Math.Clamp(
            purchasingPower,
            int.MinValue,
            int.MaxValue);
    }

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> ReplaceNativeGoldChecks(
        IEnumerable<CodeInstruction> instructions,
        MethodBase __originalMethod)
    {
        MethodInfo goldGetter = AccessTools.PropertyGetter(
                typeof(Player),
                nameof(Player.Gold))
            ?? throw new MissingMethodException(
                typeof(Player).FullName,
                "get_Gold");
        MethodInfo replacement = AccessTools.DeclaredMethod(
                typeof(EventCreditAvailabilityPatch),
                nameof(SpendableGold),
                new[] { typeof(Player) })
            ?? throw new MissingMethodException(
                typeof(EventCreditAvailabilityPatch).FullName,
                nameof(SpendableGold));

        int expected = ExpectedGetterCount(__originalMethod);
        int replaced = 0;
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.Calls(goldGetter))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = replacement;
                replaced++;
            }

            yield return instruction;
        }

        if (replaced != expected)
        {
            throw new InvalidOperationException(
                "TD Bank event credit patch expected "
                + $"{expected} Player.Gold getter(s) in "
                + $"{__originalMethod.DeclaringType?.FullName}."
                + $"{__originalMethod.Name}, but found {replaced}. "
                + "The public-beta event API has drifted.");
        }
    }

    private static int ExpectedGetterCount(MethodBase method)
    {
        if (method.Name.StartsWith(
                "<IsAllowed>b__",
                StringComparison.Ordinal))
        {
            return 1;
        }

        DirectTarget[] matches = DirectTargets
            .Where(target =>
                target.EventType == method.DeclaringType
                && target.MethodName == method.Name)
            .ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidOperationException(
                "TD Bank has no locked Gold-getter count for "
                + $"{method.DeclaringType?.FullName}.{method.Name}.");
        }

        return matches[0].ExpectedGoldGetters;
    }
}
