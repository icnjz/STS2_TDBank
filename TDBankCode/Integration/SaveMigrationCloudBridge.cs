using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;

namespace TDBank.TDBankCode.Integration;

public static class MigrationProgressClassifier
{
    public enum Result
    {
        Unknown,
        Pristine,
        Substantive,
    }

    private static readonly string[] ZeroRootProperties =
    [
        "architect_damage",
        "current_score",
        "floors_climbed",
        "max_multiplayer_ascension",
        "preferred_multiplayer_ascension",
        "test_subject_kills",
        "total_playtime",
        "total_unlocks",
        "wongo_points",
    ];

    private static readonly string[] EmptyRootArrays =
    [
        "ancient_stats",
        "card_stats",
        "discovered_acts",
        "discovered_events",
        "discovered_potions",
        "encounter_stats",
        "enemy_stats",
        "epochs",
        "unlocked_achievements",
    ];

    private static readonly HashSet<string> DefaultCards =
    [
        "CARD.STRIKE_IRONCLAD",
        "CARD.DEFEND_IRONCLAD",
        "CARD.BASH",
    ];

    public static Result Classify(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Result.Unknown;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !TryReadInt64(root, "schema_version", out long schema)
                || schema != 22)
            {
                return Result.Unknown;
            }

            foreach (string property in ZeroRootProperties)
            {
                if (!TryReadInt64(root, property, out long value))
                {
                    return Result.Unknown;
                }

                if (value != 0)
                {
                    return Result.Substantive;
                }
            }

            foreach (string property in EmptyRootArrays)
            {
                if (!root.TryGetProperty(property, out JsonElement value)
                    || value.ValueKind != JsonValueKind.Array)
                {
                    return Result.Unknown;
                }

                if (value.GetArrayLength() != 0)
                {
                    return Result.Substantive;
                }
            }

            if (!root.TryGetProperty("pending_character_unlock", out JsonElement pendingUnlock)
                || pendingUnlock.ValueKind != JsonValueKind.String)
            {
                return Result.Unknown;
            }

            if (!string.Equals(
                    pendingUnlock.GetString(),
                    "NONE.NONE",
                    StringComparison.Ordinal))
            {
                return Result.Substantive;
            }

            Result cardsResult = ClassifyDefaultStringArray(
                root,
                "discovered_cards",
                DefaultCards);
            if (cardsResult != Result.Pristine)
            {
                return cardsResult;
            }

            Result relicsResult = ClassifyDefaultStringArray(
                root,
                "discovered_relics",
                new HashSet<string>(StringComparer.Ordinal)
                {
                    "RELIC.BURNING_BLOOD",
                });
            if (relicsResult != Result.Pristine)
            {
                return relicsResult;
            }

            if (!root.TryGetProperty("character_stats", out JsonElement characterStats)
                || characterStats.ValueKind != JsonValueKind.Array)
            {
                return Result.Unknown;
            }

            if (characterStats.GetArrayLength() != 1)
            {
                return Result.Substantive;
            }

            JsonElement ironclad = characterStats[0];
            if (ironclad.ValueKind != JsonValueKind.Object
                || !ironclad.TryGetProperty("id", out JsonElement characterId)
                || characterId.ValueKind != JsonValueKind.String)
            {
                return Result.Unknown;
            }

            if (!string.Equals(
                    characterId.GetString(),
                    "CHARACTER.IRONCLAD",
                    StringComparison.Ordinal))
            {
                return Result.Substantive;
            }

            foreach (string property in new[]
                     {
                         "best_win_streak",
                         "current_streak",
                         "max_ascension",
                         "playtime",
                         "preferred_ascension",
                         "total_losses",
                         "total_wins",
                     })
            {
                if (!TryReadInt64(ironclad, property, out long value))
                {
                    return Result.Unknown;
                }

                if (value != 0)
                {
                    return Result.Substantive;
                }
            }

            if (!TryReadInt64(ironclad, "fastest_win_time", out long fastestWin)
                || fastestWin != -1
                || !ironclad.TryGetProperty("badges", out JsonElement badges)
                || badges.ValueKind != JsonValueKind.Array)
            {
                return Result.Unknown;
            }

            return badges.GetArrayLength() == 0
                ? Result.Pristine
                : Result.Substantive;
        }
        catch (Exception)
        {
            return Result.Unknown;
        }
    }

    private static Result ClassifyDefaultStringArray(
        JsonElement root,
        string propertyName,
        IReadOnlySet<string> expected)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement array)
            || array.ValueKind != JsonValueKind.Array)
        {
            return Result.Unknown;
        }

        var actual = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || item.GetString() is not { } text)
            {
                return Result.Unknown;
            }

            actual.Add(text);
        }

        return actual.SetEquals(expected)
            ? Result.Pristine
            : Result.Substantive;
    }

    private static bool TryReadInt64(
        JsonElement parent,
        string propertyName,
        out long value)
    {
        value = 0;
        return parent.TryGetProperty(propertyName, out JsonElement property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt64(out value);
    }
}

public static class MigrationCloudFileRules
{
    private static readonly HashSet<string> ProfileSaveFiles =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "progress.save",
            "prefs.save",
            "current_run.save",
            "current_run_mp.save",
        };

    public static bool IsCloudManagedTarget(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        string[] parts = relativePath
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2)
        {
            return parts[0].Equals("modded", StringComparison.OrdinalIgnoreCase)
                && parts[1].Equals("profile.save", StringComparison.OrdinalIgnoreCase);
        }

        if (parts.Length < 4
            || !parts[0].Equals("modded", StringComparison.OrdinalIgnoreCase)
            || !IsProfileDirectory(parts[1])
            || !parts[2].Equals("saves", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (parts.Length == 4)
        {
            return ProfileSaveFiles.Contains(parts[3]);
        }

        return parts.Length == 5
            && parts[3].Equals("history", StringComparison.OrdinalIgnoreCase)
            && parts[4].EndsWith(".run", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsProfileDirectory(string directory)
    {
        return directory.Length == "profile1".Length
            && directory.StartsWith("profile", StringComparison.OrdinalIgnoreCase)
            && directory[^1] is >= '1' and <= '3';
    }
}

internal static class SaveMigrationCloudBridge
{
    internal const string MarkerFileName = "tdbank_migration_v2_1.pending.json";

    private const int MarkerSchemaVersion = 1;


    private const string ReleaseVersion = "2.2.0";

    private static readonly FieldInfo? SaveStoreField =
        AccessTools.Field(typeof(SaveManager), "_saveStore");

    private static readonly JsonSerializerOptions MarkerJsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
    };

    private static readonly object Gate = new();

    private static ValidatedMarker? _activeMarker;
    private static bool _decisionAttempted;
    private static bool _overwriteAuthorized;

    internal static bool TryForceCloudOverwrite(
        SaveManager saveManager,
        out bool result)
    {
        result = false;
        lock (Gate)
        {
            if (_decisionAttempted)
            {
                if (_overwriteAuthorized)
                {
                    result = true;
                    return true;
                }

                return false;
            }

            _decisionAttempted = true;
            if (!UserDataPathProvider.IsRunningModded)
            {
                return false;
            }

            if (!TryLoadAndValidateMarker(out ValidatedMarker? marker)
                || marker is null)
            {
                return false;
            }

            if (!TryGetCloudStore(saveManager, out ICloudSaveStore? cloud)
                || cloud is null)
            {
                MainFile.Logger.Warn(
                    "A valid save-migration marker exists, but Steam Cloud is unavailable. "
                    + "Leaving the marker in place and preserving the game's normal behavior.");
                return false;
            }

            if (AllManagedRemoteFilesMatch(cloud, marker))
            {
                DeleteCompletedMarker(marker);
                return false;
            }

            RemoteState remoteState = ClassifyRemoteModdedState(cloud, marker);
            if (remoteState != RemoteState.AbsentOrPristine)
            {
                MainFile.Logger.Warn(
                    remoteState == RemoteState.Substantive
                        ? "Steam Cloud contains established modded progress. "
                          + "TD Bank will not overwrite it with the installer migration."
                        : "Steam Cloud's modded save state could not be classified safely. "
                          + "TD Bank will not force an upload.");
                return false;
            }

            _activeMarker = marker;
            _overwriteAuthorized = true;
            result = true;
            MainFile.Logger.Info(
                "Validated the TD Bank save migration and found no competing modded "
                + "progress in Steam Cloud. Uploading the migrated local saves.");
            return true;
        }
    }

    internal static void VerifyUploadAndComplete(SaveManager saveManager)
    {
        lock (Gate)
        {
            if (!_overwriteAuthorized || _activeMarker is not { } marker)
            {
                return;
            }

            if (!TryGetCloudStore(saveManager, out ICloudSaveStore? cloud)
                || cloud is null
                || !AllManagedRemoteFilesMatch(cloud, marker))
            {
                MainFile.Logger.Warn(
                    "The complete migrated save upload could not be verified. "
                    + "The pending marker was retained for a safe retry.");
                return;
            }

            DeleteCompletedMarker(marker);
            _overwriteAuthorized = false;
            _activeMarker = null;
            MainFile.Logger.Info(
                "Verified the migrated profile/progress files in Steam Cloud "
                + "and cleared the pending migration marker.");
        }
    }

    private static bool TryLoadAndValidateMarker(out ValidatedMarker? validated)
    {
        validated = null;
        string accountRoot;
        try
        {
            accountRoot = Path.GetFullPath(ProjectSettings.GlobalizePath(
                UserDataPathProvider.GetAccountScopedBasePath(null)));
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn(
                $"Could not resolve the TD Bank save-migration marker path: {ex.Message}");
            return false;
        }

        string markerPath = Path.Combine(accountRoot, MarkerFileName);
        if (!System.IO.File.Exists(markerPath))
        {
            return false;
        }

        MigrationMarkerDocument? marker;
        try
        {
            marker = JsonSerializer.Deserialize<MigrationMarkerDocument>(
                System.IO.File.ReadAllText(markerPath, Encoding.UTF8),
                MarkerJsonOptions);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn(
                $"Ignoring an unreadable save-migration marker: {ex.Message}");
            return false;
        }

        string accountId = Path.GetFileName(
            accountRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (marker is null
            || marker.SchemaVersion != MarkerSchemaVersion
            || !string.Equals(marker.ModId, MainFile.ModId, StringComparison.Ordinal)
            || !string.Equals(marker.ReleaseVersion, ReleaseVersion, StringComparison.Ordinal)
            || !string.Equals(marker.AccountId, accountId, StringComparison.Ordinal)
            || !DateTimeOffset.TryParse(marker.CreatedUtc, out _)
            || marker.Profiles is null
            || marker.Profiles.Count == 0
            || marker.Profiles.Any(static id => id is < 1 or > 3)
            || marker.Profiles.Distinct().Count() != marker.Profiles.Count
            || marker.Files is null
            || marker.Files.Count == 0
            || marker.Files.Count > 4096)
        {
            MainFile.Logger.Warn(
                "Ignoring a save-migration marker with invalid identity or structure.");
            return false;
        }

        var files = new List<ValidatedMarkerFile>(marker.Files.Count);
        foreach (MigrationMarkerFile entry in marker.Files)
        {
            try
            {
                if (!TryValidateMarkerFile(
                        accountRoot,
                        entry,
                        out ValidatedMarkerFile? file)
                    || file is null)
                {
                    MainFile.Logger.Warn(
                        "Ignoring a save-migration marker because a copied file or hash "
                        + "no longer matches.");
                    return false;
                }

                files.Add(file);
            }
            catch (Exception ex)
            {
                MainFile.Logger.Warn(
                    $"Ignoring an unsafe save-migration marker entry: {ex.Message}");
                return false;
            }
        }

        if (files.Select(static file => file.TargetRelativePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() != files.Count)
        {
            MainFile.Logger.Warn(
                "Ignoring a save-migration marker containing duplicate target paths.");
            return false;
        }

        if (!HasRequiredCriticalEntries(marker.Profiles, files))
        {
            MainFile.Logger.Warn(
                "Ignoring a save-migration marker missing a critical profile/progress hash.");
            return false;
        }

        validated = new ValidatedMarker(
            markerPath,
            accountRoot,
            marker.Profiles.ToArray(),
            files);
        return true;
    }

    private static bool TryValidateMarkerFile(
        string accountRoot,
        MigrationMarkerFile entry,
        out ValidatedMarkerFile? validated)
    {
        validated = null;
        string? sourceRelative = NormalizeRelativePath(entry.SourceRelativePath);
        string? targetRelative = NormalizeRelativePath(entry.TargetRelativePath);
        if (sourceRelative is null
            || targetRelative is null
            || sourceRelative.StartsWith("modded/", StringComparison.OrdinalIgnoreCase)
            || !targetRelative.Equals(
                "modded/" + sourceRelative,
                StringComparison.OrdinalIgnoreCase)
            || !(sourceRelative.Equals("profile.save", StringComparison.OrdinalIgnoreCase)
                 || sourceRelative.StartsWith("profile1/", StringComparison.OrdinalIgnoreCase)
                 || sourceRelative.StartsWith("profile2/", StringComparison.OrdinalIgnoreCase)
                 || sourceRelative.StartsWith("profile3/", StringComparison.OrdinalIgnoreCase))
            || !IsSha256(entry.SourceSha256)
            || !IsSha256(entry.TargetSha256)
            || !entry.SourceSha256!.Equals(
                entry.TargetSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string sourcePath = ResolveWithinAccountRoot(accountRoot, sourceRelative);
        string targetPath = ResolveWithinAccountRoot(accountRoot, targetRelative);
        if (!System.IO.File.Exists(sourcePath)
            || !System.IO.File.Exists(targetPath))
        {
            return false;
        }

        string sourceHash = HashFile(sourcePath);
        string targetHash = HashFile(targetPath);
        if (!sourceHash.Equals(entry.SourceSha256, StringComparison.OrdinalIgnoreCase)
            || !targetHash.Equals(entry.TargetSha256, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        validated = new ValidatedMarkerFile(
            sourceRelative,
            targetRelative,
            entry.TargetSha256.ToLowerInvariant(),
            entry.Critical);
        return true;
    }

    private static bool HasRequiredCriticalEntries(
        IReadOnlyCollection<int> profiles,
        IReadOnlyCollection<ValidatedMarkerFile> files)
    {
        bool hasProfile = files.Any(static file =>
            file.Critical
            && file.TargetRelativePath.Equals(
                "modded/profile.save",
                StringComparison.OrdinalIgnoreCase));
        if (!hasProfile)
        {
            return false;
        }

        return profiles.All(profileId =>
        {
            string path = ProgressSaveManager.GetProgressPathForProfile(
                profileId,
                forceModState: true);
            return files.Any(file =>
                file.Critical
                && file.TargetRelativePath.Equals(
                    path,
                    StringComparison.OrdinalIgnoreCase));
        });
    }

    private static RemoteState ClassifyRemoteModdedState(
        ICloudSaveStore cloud,
        ValidatedMarker marker)
    {
        try
        {
            Dictionary<string, ValidatedMarkerFile> expectedFiles = marker.Files
                .Where(static file =>
                    MigrationCloudFileRules.IsCloudManagedTarget(
                        file.TargetRelativePath))
                .ToDictionary(
                    static file => file.TargetRelativePath,
                    StringComparer.OrdinalIgnoreCase);

            for (int profileId = 1; profileId <= 3; profileId++)
            {
                string progressPath = ProgressSaveManager.GetProgressPathForProfile(
                    profileId,
                    forceModState: true);
                if (cloud.FileExists(progressPath))
                {
                    if (!RemoteFileMatches(
                            cloud,
                            progressPath,
                            expectedFiles))
                    {
                        string? progress = cloud.ReadFile(progressPath);
                        MigrationProgressClassifier.Result classification =
                            MigrationProgressClassifier.Classify(progress);
                        if (classification == MigrationProgressClassifier.Result.Substantive)
                        {
                            return RemoteState.Substantive;
                        }

                        if (classification == MigrationProgressClassifier.Result.Unknown)
                        {
                            return RemoteState.Unknown;
                        }
                    }
                }

                foreach (string runFile in new[]
                         {
                             RunSaveManager.GetRunSavePath(
                                 profileId,
                                 "current_run.save",
                                 forceModState: true),
                             RunSaveManager.GetRunSavePath(
                                 profileId,
                                 "current_run_mp.save",
                                 forceModState: true),
                         })
                {
                    if (cloud.FileExists(runFile))
                    {
                        if (!RemoteFileMatches(
                                cloud,
                                runFile,
                                expectedFiles))
                        {
                            return RemoteState.Substantive;
                        }
                    }
                }

                string historyPath = RunHistorySaveManager.GetHistoryPath(
                    profileId,
                    forceModState: true);
                foreach (string historyFile in cloud.GetFilesInDirectory(historyPath)
                             .Where(static file =>
                                 file.EndsWith(".run", StringComparison.OrdinalIgnoreCase)))
                {
                    string historyFilePath = historyPath + "/" + historyFile;
                    if (!RemoteFileMatches(
                            cloud,
                            historyFilePath,
                            expectedFiles))
                    {
                        return RemoteState.Substantive;
                    }
                }
            }

            return RemoteState.AbsentOrPristine;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn(
                $"Could not safely inspect Steam Cloud modded saves: {ex.Message}");
            return RemoteState.Unknown;
        }
    }

    private static bool RemoteFileMatches(
        ICloudSaveStore cloud,
        string path,
        IReadOnlyDictionary<string, ValidatedMarkerFile> expectedFiles)
    {
        return expectedFiles.TryGetValue(path, out ValidatedMarkerFile? expected)
            && HashText(cloud.ReadFile(path)).Equals(
                expected.TargetSha256,
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool AllManagedRemoteFilesMatch(
        ICloudSaveStore cloud,
        ValidatedMarker marker)
    {
        try
        {
            foreach (ValidatedMarkerFile file in marker.Files.Where(
                         static file =>
                             MigrationCloudFileRules.IsCloudManagedTarget(
                                 file.TargetRelativePath)))
            {
                if (!cloud.FileExists(file.TargetRelativePath))
                {
                    return false;
                }

                string? content = cloud.ReadFile(file.TargetRelativePath);
                if (!HashText(content).Equals(
                        file.TargetSha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn(
                $"Could not verify migrated Steam Cloud files: {ex.Message}");
            return false;
        }
    }

    private static bool TryGetCloudStore(
        SaveManager saveManager,
        out ICloudSaveStore? cloud)
    {
        cloud = null;
        try
        {
            if (SaveStoreField?.GetValue(saveManager) is not CloudSaveStore cloudSaveStore)
            {
                return false;
            }

            cloud = cloudSaveStore.CloudStore;
            return true;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn(
                $"Could not access the game's cloud save store: {ex.Message}");
            return false;
        }
    }

    private static void DeleteCompletedMarker(ValidatedMarker marker)
    {
        try
        {
            System.IO.File.Delete(marker.MarkerPath);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn(
                $"Migration finished, but the pending marker could not be removed: {ex.Message}");
        }
    }

    private static string? NormalizeRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string normalized = path.Replace('\\', '/').Trim();
        if (normalized.StartsWith("/", StringComparison.Ordinal)
            || normalized.Contains(":", StringComparison.Ordinal)
            || normalized.Split('/').Any(static segment =>
                segment.Length == 0 || segment is "." or ".."))
        {
            return null;
        }

        return normalized;
    }

    private static string ResolveWithinAccountRoot(
        string accountRoot,
        string relativePath)
    {
        string fullPath = Path.GetFullPath(Path.Combine(
            accountRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        string rootPrefix = accountRoot.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Migration marker path escaped the account root.");
        }

        return fullPath;
    }

    private static bool IsSha256(string? hash)
    {
        return hash is { Length: 64 }
            && hash.All(static character =>
                character is >= '0' and <= '9'
                or >= 'a' and <= 'f'
                or >= 'A' and <= 'F');
    }

    private static string HashFile(string path)
    {
        return Convert.ToHexString(
                SHA256.HashData(System.IO.File.ReadAllBytes(path)))
            .ToLowerInvariant();
    }

    private static string HashText(string? text)
    {
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(text ?? string.Empty)))
            .ToLowerInvariant();
    }

    private enum RemoteState
    {
        AbsentOrPristine,
        Substantive,
        Unknown,
    }

    private sealed record ValidatedMarker(
        string MarkerPath,
        string AccountRoot,
        IReadOnlyList<int> Profiles,
        IReadOnlyList<ValidatedMarkerFile> Files);

    private sealed record ValidatedMarkerFile(
        string SourceRelativePath,
        string TargetRelativePath,
        string TargetSha256,
        bool Critical);

    private sealed class MigrationMarkerDocument
    {
        [JsonPropertyName("schema_version")]
        public int SchemaVersion { get; init; }

        [JsonPropertyName("mod_id")]
        public string? ModId { get; init; }

        [JsonPropertyName("release_version")]
        public string? ReleaseVersion { get; init; }

        [JsonPropertyName("account_id")]
        public string? AccountId { get; init; }

        [JsonPropertyName("created_utc")]
        public string? CreatedUtc { get; init; }

        [JsonPropertyName("profiles")]
        public List<int>? Profiles { get; init; }

        [JsonPropertyName("files")]
        public List<MigrationMarkerFile>? Files { get; init; }
    }

    private sealed class MigrationMarkerFile
    {
        [JsonPropertyName("source_relative_path")]
        public string? SourceRelativePath { get; init; }

        [JsonPropertyName("target_relative_path")]
        public string? TargetRelativePath { get; init; }

        [JsonPropertyName("source_sha256")]
        public string? SourceSha256 { get; init; }

        [JsonPropertyName("target_sha256")]
        public string? TargetSha256 { get; init; }

        [JsonPropertyName("critical")]
        public bool Critical { get; init; }
    }
}

[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.ShouldOverwriteCloudWithLocal))]
internal static class SaveMigrationCloudDecisionPatch
{
    [HarmonyPrefix]
    private static bool Prefix(SaveManager __instance, ref bool __result)
    {
        return !SaveMigrationCloudBridge.TryForceCloudOverwrite(
            __instance,
            out __result);
    }
}

[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.InitProfileId))]
internal static class SaveMigrationCloudVerificationPatch
{
    [HarmonyPostfix]
    private static void Postfix(SaveManager __instance)
    {
        SaveMigrationCloudBridge.VerifyUploadAndComplete(__instance);
    }
}
