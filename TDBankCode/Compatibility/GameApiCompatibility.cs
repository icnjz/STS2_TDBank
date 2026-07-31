using System.Reflection;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Managers;

namespace TDBank.TDBankCode.Compatibility;

internal static class GameApiCompatibility
{
    private static readonly ConstructorInfo? Rng64StringConstructor =
        typeof(Rng).GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            [typeof(ulong), typeof(string)],
            modifiers: null);

    private static readonly ConstructorInfo? Rng32StringConstructor =
        typeof(Rng).GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            [typeof(uint), typeof(string)],
            modifiers: null);

    private static readonly PropertyInfo? RunSeedProperty =
        typeof(RunRngSet).GetProperty(
            "Seed",
            BindingFlags.Instance
            | BindingFlags.Public
            | BindingFlags.NonPublic);

    public static bool Uses64BitRng =>
        Rng64StringConstructor is not null;

    public static bool IsSupportedProgressSchema(long schemaVersion)
        => schemaVersion is >= 21 and <= 24;

    public static Rng CreateRng(ulong seed, string scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        if (Rng64StringConstructor is not null)
        {
            return (Rng)Rng64StringConstructor.Invoke([seed, scope]);
        }

        if (Rng32StringConstructor is not null)
        {
            uint foldedSeed = unchecked((uint)(seed ^ (seed >> 32)));
            return (Rng)Rng32StringConstructor.Invoke([foldedSeed, scope]);
        }

        throw new MissingMethodException(
            typeof(Rng).FullName,
            ".ctor(UInt64|UInt32, String)");
    }

    public static ulong GetRunSeed(RunRngSet rngSet)
    {
        ArgumentNullException.ThrowIfNull(rngSet);
        object? value = RunSeedProperty?.GetValue(rngSet);
        return value switch
        {
            ulong seed64 => seed64,
            uint seed32 => seed32,
            _ => throw new MissingMemberException(
                typeof(RunRngSet).FullName,
                "Seed"),
        };
    }

    public static string GetProgressPathForProfile(
        int profileId,
        bool forceModState)
    {
        return InvokePath(
            typeof(ProgressSaveManager),
            nameof(ProgressSaveManager.GetProgressPathForProfile),
            [profileId],
            forceModState);
    }

    public static string GetRunSavePath(
        int profileId,
        string fileName,
        bool forceModState)
    {
        return InvokePath(
            typeof(RunSaveManager),
            nameof(RunSaveManager.GetRunSavePath),
            [profileId, fileName],
            forceModState);
    }

    public static string GetHistoryPath(
        int profileId,
        bool forceModState)
    {
        return InvokePath(
            typeof(RunHistorySaveManager),
            nameof(RunHistorySaveManager.GetHistoryPath),
            [profileId],
            forceModState);
    }

    private static string InvokePath(
        Type declaringType,
        string methodName,
        object?[] legacyArguments,
        bool forceModState)
    {
        MethodInfo[] candidates = declaringType
            .GetMethods(
                BindingFlags.Static
                | BindingFlags.Public
                | BindingFlags.NonPublic)
            .Where(method => method.Name == methodName)
            .ToArray();
        MethodInfo? modern = candidates.FirstOrDefault(method =>
        {
            ParameterInfo[] parameters = method.GetParameters();
            return parameters.Length == legacyArguments.Length + 1
                && parameters[^1].ParameterType == typeof(bool?);
        });
        object? result;
        if (modern is not null)
        {
            object?[] modernArguments =
            [
                ..legacyArguments,
                forceModState,
            ];
            result = modern.Invoke(null, modernArguments);
        }
        else
        {
            MethodInfo legacy = candidates.Single(method =>
                method.GetParameters().Length == legacyArguments.Length);
            result = legacy.Invoke(null, legacyArguments);
        }

        return result as string
            ?? throw new InvalidOperationException(
                $"{declaringType.FullName}.{methodName} returned no path.");
    }
}
