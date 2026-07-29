using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CNJ.TowerDebt.Setup.Core;

internal static partial class SaveProtection
{
    private const string BackupFolderName = "cnj-tower-debt-save-backups";
    private const string PendingMarkerName = "tdbank_migration_v2_1.pending.json";

    private static readonly HashSet<string> BlankTargetFiles =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "saves/prefs.save",
            "saves/prefs.save.backup",
            "saves/progress.save",
            "saves/progress.save.backup",
        };

    public static string DefaultSaveRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SlayTheSpire2");

    public static SaveProtectionResult ProtectAndInitialize(
   string saveRoot,
   string transactionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(saveRoot);
        ValidateTransactionId(transactionId);

        var fullSaveRoot = Path.GetFullPath(saveRoot);
        if (!Directory.Exists(fullSaveRoot))
        {
            return SaveProtectionResult.Empty(fullSaveRoot);
        }

        if (IsReparsePoint(fullSaveRoot))
        {
            return new SaveProtectionResult(
                fullSaveRoot,
                string.Empty,
                0,
                [
                    new SaveProfileResult(
                        "*",
                        "*",
                        SaveProfileDisposition.SkippedUnsafe,
                        fullSaveRoot,
                        fullSaveRoot,
                        "The save root is a reparse point."),
                ]);
        }

        var steamRoot = CombineUnder(fullSaveRoot, "steam");
        if (!Directory.Exists(steamRoot))
        {
            return SaveProtectionResult.Empty(fullSaveRoot);
        }

        if (IsReparsePoint(steamRoot))
        {
            return new SaveProtectionResult(
                fullSaveRoot,
                string.Empty,
                0,
                [
                    new SaveProfileResult(
                        "*",
                        "*",
                        SaveProfileDisposition.SkippedUnsafe,
                        steamRoot,
                        steamRoot,
                        "The Steam save root is a reparse point."),
                ]);
        }

        var plans = new List<ProfilePlan>();
        var results = new List<SaveProfileResult>();
        var accountRoots = Directory.EnumerateDirectories(steamRoot)
            .Where(path => SteamAccountRegex().IsMatch(Path.GetFileName(path)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var accountRoot in accountRoots)
        {
            BuildAccountPlans(accountRoot, plans, results);
        }

        if (plans.Count == 0)
        {
            return new SaveProtectionResult(
                fullSaveRoot,
                string.Empty,
                accountRoots.Length,
                results);
        }

        var backupRoot = AllocateBackupRoot(fullSaveRoot, transactionId);
        var workingRoot = CombineUnder(backupRoot, "working");
        var replacements = new List<Replacement>();
        var createdRootProfileFiles = new List<string>();
        var markerReplacements = new List<MarkerReplacement>();
        var markerAccounts = new Dictionary<string, List<ProfilePlan>>(
            StringComparer.OrdinalIgnoreCase);

        try
        {
            Directory.CreateDirectory(backupRoot);
            InstallerLog.TryWrite(
                $"Save protection snapshot started. Root={fullSaveRoot}; Backup={backupRoot}");

            SnapshotPlans(fullSaveRoot, backupRoot, plans);

            foreach (var plan in plans)
            {
                switch (plan.Disposition)
                {
                    case SaveProfileDisposition.Migrated:
                        ReplaceWithVerifiedCopy(
                            plan,
                            fullSaveRoot,
                            workingRoot,
                            replacements);
                        AddMarkerPlan(markerAccounts, plan);
                        break;
                    case SaveProfileDisposition.AlreadyEquivalent:
                        AddMarkerPlan(markerAccounts, plan);
                        break;
                }
            }

            foreach (var (accountId, markerPlans) in markerAccounts)
            {
                var accountRoot = CombineUnder(steamRoot, accountId);
                EnsureModdedProfileSelector(
                    accountRoot,
                    fullSaveRoot,
                    backupRoot,
                    createdRootProfileFiles);
            }

            WriteManifest(backupRoot, fullSaveRoot, accountRoots.Length, results);
            foreach (var (accountId, markerPlans) in markerAccounts)
            {
                var accountRoot = CombineUnder(steamRoot, accountId);
                markerReplacements.Add(
                    WritePendingMarker(
                        accountRoot,
                        accountId,
                        backupRoot,
                        markerPlans));
            }
            SafeDeleteDirectory(workingRoot, backupRoot);

            InstallerLog.TryWrite(
                $"Save protection succeeded. Migrated={results.Count(result => result.Disposition == SaveProfileDisposition.Migrated)}; " +
                $"Preserved={results.Count(result => result.Disposition == SaveProfileDisposition.PreservedEstablished)}; " +
                $"Equivalent={results.Count(result => result.Disposition == SaveProfileDisposition.AlreadyEquivalent)}; " +
                $"Backup={backupRoot}");

            return new SaveProtectionResult(
                fullSaveRoot,
                backupRoot,
                accountRoots.Length,
                results);
        }
        catch (Exception exception)
        {
            var rollbackErrors = RollBackReplacements(
                replacements,
                createdRootProfileFiles,
                markerReplacements,
                fullSaveRoot,
                workingRoot,
                backupRoot);
            if (rollbackErrors.Count > 0)
            {
                var detail = string.Join(Environment.NewLine, rollbackErrors);
                InstallerLog.TryWrite(
                    $"Save protection failed: {exception}. Rollback errors: {detail}");
                throw new InstallerOperationException(
                    InstallerErrorCode.SaveRollbackFailed,
                    exception,
                    backupDirectory: backupRoot,
                    detail: detail);
            }

            InstallerLog.TryWrite(
                $"Save protection failed, but rollback completed: {exception}");
            throw new InstallerOperationException(
                InstallerErrorCode.SaveProtectionFailed,
                exception,
                backupDirectory: backupRoot,
                targetPath: fullSaveRoot);
        }
    }

    private static void BuildAccountPlans(
        string accountRoot,
        ICollection<ProfilePlan> plans,
        ICollection<SaveProfileResult> results)
    {
        var accountId = Path.GetFileName(accountRoot);
        var moddedRoot = Path.Combine(accountRoot, "modded");

        if (IsReparsePoint(accountRoot) || IsReparsePoint(moddedRoot))
        {
            results.Add(
                new SaveProfileResult(
                    accountId,
                    "*",
                    SaveProfileDisposition.SkippedUnsafe,
                    accountRoot,
                    moddedRoot,
                    "The account or modded directory is a reparse point."));
            return;
        }

        var sourceSelector = Path.Combine(accountRoot, "profile.save");
        var targetSelector = Path.Combine(moddedRoot, "profile.save");
        var selectorSupportsMigration = File.Exists(sourceSelector)
            && !IsReparsePoint(sourceSelector)
            && (!File.Exists(targetSelector)
                || (!IsReparsePoint(targetSelector)
                    && FilesEquivalent(sourceSelector, targetSelector)));

        var profileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unsupportedProfileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in Directory.EnumerateDirectories(accountRoot))
        {
            var name = Path.GetFileName(directory);
            if (ProfileRegex().IsMatch(name))
            {
                profileNames.Add(name);
            }
            else if (AnyProfileRegex().IsMatch(name))
            {
                unsupportedProfileNames.Add(name);
            }
        }

        if (Directory.Exists(moddedRoot))
        {
            foreach (var directory in Directory.EnumerateDirectories(moddedRoot))
            {
                var name = Path.GetFileName(directory);
                if (ProfileRegex().IsMatch(name))
                {
                    profileNames.Add(name);
                }
                else if (AnyProfileRegex().IsMatch(name))
                {
                    unsupportedProfileNames.Add(name);
                }
            }
        }

        foreach (var profileName in unsupportedProfileNames.OrderBy(
                     name => name,
                     StringComparer.OrdinalIgnoreCase))
        {
            results.Add(
                new SaveProfileResult(
                    accountId,
                    profileName,
                    SaveProfileDisposition.SkippedUnsafe,
                    Path.Combine(accountRoot, profileName),
                    Path.Combine(moddedRoot, profileName),
                    "Slay the Spire 2 supports only profile1, profile2, and profile3."));
        }

        foreach (var profileName in profileNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            var vanillaPath = Path.Combine(accountRoot, profileName);
            var moddedPath = Path.Combine(moddedRoot, profileName);

            if (TreeContainsReparsePoint(vanillaPath)
                || TreeContainsReparsePoint(moddedPath))
            {
                results.Add(
                    new SaveProfileResult(
                        accountId,
                        profileName,
                        SaveProfileDisposition.SkippedUnsafe,
                        vanillaPath,
                        moddedPath,
                        "A profile contains a symbolic link or directory junction."));
                continue;
            }

            var progressPath = Path.Combine(vanillaPath, "saves", "progress.save");
            if (!Directory.Exists(vanillaPath) || !IsUsableProgress(progressPath))
            {
                results.Add(
                    new SaveProfileResult(
                        accountId,
                        profileName,
                        SaveProfileDisposition.NoUsableVanilla,
                        vanillaPath,
                        moddedPath,
                        "The vanilla progress.save file is missing or invalid."));
                continue;
            }

            SaveProfileDisposition disposition;
            string? detail = null;
            if (!selectorSupportsMigration)
            {
                disposition = SaveProfileDisposition.PreservedEstablished;
                detail =
                    "The vanilla/modded profile selector is missing, unsafe, or different; " +
                    "setup cannot create a cloud-safe migration marker.";
            }
            else if (Directory.Exists(moddedPath)
                && DirectoryContentsEquivalent(vanillaPath, moddedPath))
            {
                disposition = SaveProfileDisposition.AlreadyEquivalent;
                detail = "The vanilla and modded profile contents already match.";
            }
            else if (CanReplaceAsBlank(moddedPath))
            {
                disposition = SaveProfileDisposition.Migrated;
            }
            else
            {
                disposition = SaveProfileDisposition.PreservedEstablished;
                detail = "The modded profile contains progress or unrecognized files.";
            }

            var plan = new ProfilePlan(
                accountId,
                profileName,
                vanillaPath,
                moddedPath,
                disposition);
            plans.Add(plan);
            results.Add(
                new SaveProfileResult(
                    accountId,
                    profileName,
                    disposition,
                    vanillaPath,
                    moddedPath,
                    detail));
        }
    }

    private static bool CanReplaceAsBlank(string moddedProfile)
    {
        if (!Directory.Exists(moddedProfile))
        {
            return true;
        }

        var files = EnumerateRelativeFiles(moddedProfile);
        if (files.Count == 0)
        {
            return true;
        }

        if (files.Keys.Any(relative => !BlankTargetFiles.Contains(relative)))
        {
            return false;
        }

        foreach (var progressName in new[]
                 {
                     "saves/progress.save",
                     "saves/progress.save.backup",
                 })
        {
            if (files.TryGetValue(progressName, out var progressPath)
                && !IsPristineProgress(progressPath))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsUsableProgress(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(
                File.ReadAllBytes(path),
                new JsonDocumentOptions { MaxDepth = 256 });
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("schema_version", out var schema)
                && schema.TryGetInt32(out var schemaVersion)
                && schemaVersion > 0
                && root.TryGetProperty("unique_id", out var uniqueId)
                && uniqueId.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(uniqueId.GetString());
        }
        catch
        {
            return false;
        }
    }

    private static bool IsPristineProgress(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(
                File.ReadAllBytes(path),
                new JsonDocumentOptions { MaxDepth = 256 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("schema_version", out var schema)
                || !schema.TryGetInt32(out var schemaVersion)
                || !GameValidator.IsSupportedProgressSchema(schemaVersion)
                || !root.TryGetProperty("unique_id", out var uniqueId)
                || uniqueId.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(uniqueId.GetString()))
            {
                return false;
            }

            foreach (var propertyName in new[]
                     {
                         "architect_damage",
                         "current_score",
                         "floors_climbed",
                         "max_multiplayer_ascension",
                         "preferred_multiplayer_ascension",
                         "test_subject_kills",
                         "total_playtime",
                         "total_unlocks",
                         "wongo_points",
                     })
            {
                if (!HasZeroNumber(root, propertyName))
                {
                    return false;
                }
            }

            foreach (var propertyName in new[]
                     {
                         "ancient_stats",
                         "card_stats",
                         "discovered_acts",
                         "discovered_events",
                         "discovered_potions",
                         "encounter_stats",
                         "enemy_stats",
                         "epochs",
                         "unlocked_achievements",
                     })
            {
                if (!HasEmptyArray(root, propertyName))
                {
                    return false;
                }
            }

            return HasOnlyDefaultCharacterStats(root)
                && HasOnlyAllowedStrings(
                    root,
                    "discovered_cards",
                    "CARD.STRIKE_IRONCLAD",
                    "CARD.DEFEND_IRONCLAD",
                    "CARD.BASH")
                && HasOnlyAllowedStrings(
                    root,
                    "discovered_relics",
                    "RELIC.BURNING_BLOOD")
                && HasOnlyAllowedStrings(
                    root,
                    "ftue_completed",
                    "accept_tutorials_ftue",
                    "multiplayer_warning")
                && (!root.TryGetProperty("pending_character_unlock", out var pending)
                    || string.Equals(
                        pending.GetString(),
                        "NONE.NONE",
                        StringComparison.Ordinal));
        }
        catch
        {
            return false;
        }
    }

    private static bool HasZeroNumber(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out var number)
            && number == 0;
    }

    private static bool HasEmptyArray(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.Array
            && value.GetArrayLength() == 0;
    }

    private static bool HasOnlyAllowedStrings(
        JsonElement root,
        string propertyName,
        params string[] allowed)
    {
        if (!root.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var allowlist = new HashSet<string>(allowed, StringComparer.Ordinal);
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String
                || item.GetString() is not { } text
                || !allowlist.Contains(text))
            {
                return false;
            }
        }
        return true;
    }

    private static bool HasOnlyDefaultCharacterStats(JsonElement root)
    {
        if (!root.TryGetProperty("character_stats", out var value)
            || value.ValueKind != JsonValueKind.Array)
        {
            return false;
        }
        if (value.GetArrayLength() == 0)
        {
            return true;
        }
        if (value.GetArrayLength() != 1)
        {
            return false;
        }

        var character = value[0];
        if (character.ValueKind != JsonValueKind.Object
            || !character.TryGetProperty("id", out var id)
            || !string.Equals(
                id.GetString(),
                "CHARACTER.IRONCLAD",
                StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var propertyName in new[]
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
            if (!HasZeroNumber(character, propertyName))
            {
                return false;
            }
        }

        if (character.TryGetProperty("badges", out var badges)
            && (badges.ValueKind != JsonValueKind.Array
                || badges.GetArrayLength() != 0))
        {
            return false;
        }
        if (character.TryGetProperty("fastest_win_time", out var fastest)
            && (!fastest.TryGetInt64(out var fastestValue)
                || fastestValue is not (-1 or 0)))
        {
            return false;
        }
        return true;
    }

    private static void SnapshotPlans(
        string saveRoot,
        string backupRoot,
        IReadOnlyCollection<ProfilePlan> plans)
    {
        foreach (var accountGroup in plans.GroupBy(plan => plan.AccountId))
        {
            var accountRoot = CombineUnder(saveRoot, "steam", accountGroup.Key);
            var snapshotAccount = CombineUnder(
                backupRoot,
                "snapshot",
                "steam",
                accountGroup.Key);

            CopyFileIfPresent(
                Path.Combine(accountRoot, "profile.save"),
                Path.Combine(snapshotAccount, "vanilla", "profile.save"));
            CopyFileIfPresent(
                Path.Combine(accountRoot, "profile.save.backup"),
                Path.Combine(snapshotAccount, "vanilla", "profile.save.backup"));
            CopyFileIfPresent(
                Path.Combine(accountRoot, "modded", "profile.save"),
                Path.Combine(snapshotAccount, "modded", "profile.save"));
            CopyFileIfPresent(
                Path.Combine(accountRoot, "modded", "profile.save.backup"),
                Path.Combine(snapshotAccount, "modded", "profile.save.backup"));
            CopyFileIfPresent(
                Path.Combine(accountRoot, PendingMarkerName),
                Path.Combine(snapshotAccount, "modded", PendingMarkerName));

            foreach (var plan in accountGroup)
            {
                if (Directory.Exists(plan.VanillaPath))
                {
                    CopyDirectory(
                        plan.VanillaPath,
                        Path.Combine(snapshotAccount, "vanilla", plan.ProfileName));
                }
                if (Directory.Exists(plan.ModdedPath))
                {
                    CopyDirectory(
                        plan.ModdedPath,
                        Path.Combine(snapshotAccount, "modded", plan.ProfileName));
                }
            }
        }
    }

    private static void ReplaceWithVerifiedCopy(
        ProfilePlan plan,
        string saveRoot,
        string workingRoot,
        ICollection<Replacement> replacements)
    {
        var stagePath = CombineUnder(
            workingRoot,
            "stage",
            plan.AccountId,
            plan.ProfileName);
        var displacedPath = CombineUnder(
            workingRoot,
            "displaced",
            plan.AccountId,
            plan.ProfileName);

        CopyDirectory(plan.VanillaPath, stagePath);
        if (!DirectoryContentsEquivalent(plan.VanillaPath, stagePath))
        {
            throw new IOException($"Staged save verification failed: {plan.VanillaPath}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(plan.ModdedPath)!);
        var targetExisted = Directory.Exists(plan.ModdedPath);
        if (targetExisted)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(displacedPath)!);
            Directory.Move(plan.ModdedPath, displacedPath);
        }

        try
        {
            Directory.Move(stagePath, plan.ModdedPath);
        }
        catch
        {
            if (targetExisted && Directory.Exists(displacedPath))
            {
                Directory.Move(displacedPath, plan.ModdedPath);
            }
            throw;
        }

        if (!DirectoryContentsEquivalent(plan.VanillaPath, plan.ModdedPath))
        {
            SafeDeleteDirectory(plan.ModdedPath, saveRoot);
            if (targetExisted && Directory.Exists(displacedPath))
            {
                Directory.Move(displacedPath, plan.ModdedPath);
            }
            throw new IOException($"Installed save verification failed: {plan.ModdedPath}");
        }

        replacements.Add(
            new Replacement(plan.ModdedPath, displacedPath, targetExisted));
    }

    private static void EnsureModdedProfileSelector(
        string accountRoot,
        string saveRoot,
        string backupRoot,
        ICollection<string> createdFiles)
    {
        var source = Path.Combine(accountRoot, "profile.save");
        var target = Path.Combine(accountRoot, "modded", "profile.save");
        if (!File.Exists(source) || File.Exists(target))
        {
            return;
        }

        if (IsReparsePoint(source))
        {
            throw new IOException($"Refusing to copy a reparse-point profile selector: {source}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target, overwrite: false);
        if (!FilesEquivalent(source, target))
        {
            File.Delete(target);
            throw new IOException($"Profile selector verification failed: {target}");
        }

        if (!IsPathUnder(target, saveRoot) || !IsPathUnder(backupRoot, saveRoot))
        {
            File.Delete(target);
            throw new IOException("Profile selector path escaped the save root.");
        }
        createdFiles.Add(target);
    }

    private static MarkerReplacement WritePendingMarker(
        string accountRoot,
        string accountId,
        string backupRoot,
        IReadOnlyCollection<ProfilePlan> markerPlans)
    {
        var markerFiles = new List<object>();
        var sourceSelector = Path.Combine(accountRoot, "profile.save");
        var targetSelector = Path.Combine(accountRoot, "modded", "profile.save");
        if (File.Exists(sourceSelector) && File.Exists(targetSelector))
        {
            markerFiles.Add(
                MarkerFile(
                    accountRoot,
                    sourceSelector,
                    targetSelector,
                    critical: true));
        }

        foreach (var plan in markerPlans.OrderBy(plan => plan.ProfileNumber))
        {
            foreach (var (relative, sourcePath) in EnumerateRelativeFiles(plan.VanillaPath))
            {
                var targetPath = Path.Combine(
                    plan.ModdedPath,
                    relative.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(targetPath))
                {
                    throw new IOException($"A migrated target file is missing: {targetPath}");
                }

                markerFiles.Add(
                    MarkerFile(
                        accountRoot,
                        sourcePath,
                        targetPath,
                        critical: string.Equals(
                            relative,
                            "saves/progress.save",
                            StringComparison.OrdinalIgnoreCase)));
            }
        }

        var marker = new
        {
            schema_version = 1,
            mod_id = "TDBank",
            release_version = "2.2.0",
            account_id = accountId,
            created_utc = DateTimeOffset.UtcNow,
            backup_directory = backupRoot,
            profiles = markerPlans
                .Select(plan => plan.ProfileNumber)
                .Distinct()
                .Order()
                .ToArray(),
            files = markerFiles,
        };

        var markerPath = Path.Combine(accountRoot, PendingMarkerName);
        var previousContents = File.Exists(markerPath)
            ? File.ReadAllBytes(markerPath)
            : null;
        var temporaryPath = Path.Combine(
            accountRoot,
            $".{PendingMarkerName}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(
                    stream,
                    marker,
                    new JsonSerializerOptions { WriteIndented = true });
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(markerPath))
            {
                File.Replace(temporaryPath, markerPath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temporaryPath, markerPath);
            }
            return new MarkerReplacement(markerPath, previousContents);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static object MarkerFile(
        string accountRoot,
        string sourcePath,
        string targetPath,
        bool critical)
    {
        return new
        {
            source_relative_path = ToMarkerRelativePath(accountRoot, sourcePath),
            target_relative_path = ToMarkerRelativePath(accountRoot, targetPath),
            source_sha256 = HashFile(sourcePath),
            target_sha256 = HashFile(targetPath),
            critical,
        };
    }

    private static string ToMarkerRelativePath(string accountRoot, string path)
    {
        if (!IsPathUnder(path, accountRoot))
        {
            throw new IOException($"Marker path escaped its Steam account root: {path}");
        }

        return Path.GetRelativePath(accountRoot, path)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
    }

    private static void WriteManifest(
        string backupRoot,
        string saveRoot,
        int accountsScanned,
        IReadOnlyCollection<SaveProfileResult> results)
    {
        var manifest = new
        {
            schemaVersion = 1,
            installer = "CNJ Tower Debt Setup",
            packageVersion = "v0.1",
            createdAtUtc = DateTimeOffset.UtcNow,
            saveRoot,
            accountsScanned,
            profiles = results,
        };
        File.WriteAllText(
            Path.Combine(backupRoot, "save-protection-manifest.json"),
            JsonSerializer.Serialize(
                manifest,
                new JsonSerializerOptions { WriteIndented = true }));
    }

    private static List<string> RollBackReplacements(
        IReadOnlyList<Replacement> replacements,
        IReadOnlyCollection<string> createdRootProfileFiles,
        IReadOnlyList<MarkerReplacement> markerReplacements,
        string saveRoot,
        string workingRoot,
        string backupRoot)
    {
        var errors = new List<string>();
        foreach (var marker in markerReplacements.Reverse())
        {
            TryRollback(
                () =>
                {
                    if (!IsPathUnder(marker.Path, saveRoot))
                    {
                        throw new IOException($"Marker rollback path escaped the save root: {marker.Path}");
                    }
                    if (marker.PreviousContents is null)
                    {
                        if (File.Exists(marker.Path))
                        {
                            File.Delete(marker.Path);
                        }
                    }
                    else
                    {
                        File.WriteAllBytes(marker.Path, marker.PreviousContents);
                    }
                },
                marker.Path,
                errors);
        }

        foreach (var createdFile in createdRootProfileFiles.Reverse())
        {
            TryRollback(
                () =>
                {
                    if (File.Exists(createdFile))
                    {
                        if (!IsPathUnder(createdFile, saveRoot))
                        {
                            throw new IOException($"Rollback path escaped the save root: {createdFile}");
                        }
                        File.Delete(createdFile);
                    }
                },
                createdFile,
                errors);
        }

        foreach (var replacement in replacements.Reverse())
        {
            TryRollback(
                () =>
                {
                    SafeDeleteDirectory(replacement.TargetPath, saveRoot);
                    if (replacement.TargetExisted
                        && Directory.Exists(replacement.DisplacedPath))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(replacement.TargetPath)!);
                        Directory.Move(replacement.DisplacedPath, replacement.TargetPath);
                    }
                },
                replacement.TargetPath,
                errors);
        }

        TryRollback(
            () => SafeDeleteDirectory(workingRoot, backupRoot),
            workingRoot,
            errors);
        return errors;
    }

    private static void AddMarkerPlan(
        IDictionary<string, List<ProfilePlan>> markerAccounts,
        ProfilePlan plan)
    {
        if (!markerAccounts.TryGetValue(plan.AccountId, out var accountPlans))
        {
            accountPlans = [];
            markerAccounts.Add(plan.AccountId, accountPlans);
        }
        accountPlans.Add(plan);
    }

    private static bool DirectoryContentsEquivalent(string left, string right)
    {
        if (!Directory.Exists(left) || !Directory.Exists(right))
        {
            return false;
        }

        var leftFiles = EnumerateRelativeFiles(left);
        var rightFiles = EnumerateRelativeFiles(right);
        if (leftFiles.Count != rightFiles.Count
            || leftFiles.Keys.Any(relative => !rightFiles.ContainsKey(relative)))
        {
            return false;
        }

        return leftFiles.All(
            pair => FilesEquivalent(pair.Value, rightFiles[pair.Key]));
    }

    private static SortedDictionary<string, string> EnumerateRelativeFiles(string root)
    {
        var files = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(root))
        {
            return files;
        }

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file)
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/');
            files.Add(relative, file);
        }
        return files;
    }

    private static bool FilesEquivalent(string left, string right)
    {
        var leftInfo = new FileInfo(left);
        var rightInfo = new FileInfo(right);
        return leftInfo.Length == rightInfo.Length
            && CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(File.ReadAllBytes(left)),
                SHA256.HashData(File.ReadAllBytes(right)));
    }

    private static string HashFile(string path)
    {
        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
            .ToLowerInvariant();
    }

    private static void CopyDirectory(string source, string destination)
    {
        if (TreeContainsReparsePoint(source))
        {
            throw new IOException($"Refusing to copy a save tree containing a reparse point: {source}");
        }

        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(destination, relative));
        }
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: false);
        }
        if (!DirectoryContentsEquivalent(source, destination))
        {
            throw new IOException($"Copied save directory failed SHA-256 verification: {source}");
        }
    }

    private static void CopyFileIfPresent(string source, string destination)
    {
        if (!File.Exists(source))
        {
            return;
        }
        if (IsReparsePoint(source))
        {
            throw new IOException($"Refusing to copy a reparse-point save file: {source}");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite: false);
        if (!FilesEquivalent(source, destination))
        {
            throw new IOException($"Copied save file failed SHA-256 verification: {source}");
        }
    }

    private static bool TreeContainsReparsePoint(string root)
    {
        if (!Directory.Exists(root))
        {
            return false;
        }
        if (IsReparsePoint(root))
        {
            return true;
        }

        try
        {
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                var directory = pending.Pop();
                foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    if (IsReparsePoint(entry))
                    {
                        return true;
                    }
                    if (Directory.Exists(entry))
                    {
                        pending.Push(entry);
                    }
                }
            }
            return false;
        }
        catch
        {
            return true;
        }
    }

    private static bool IsReparsePoint(string path)
    {
        return (File.Exists(path) || Directory.Exists(path))
            && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    }

    private static string AllocateBackupRoot(string saveRoot, string transactionId)
    {
        var parent = CombineUnder(saveRoot, BackupFolderName);
        var candidate = CombineUnder(parent, transactionId);
        for (var suffix = 1; Directory.Exists(candidate) || File.Exists(candidate); suffix++)
        {
            candidate = CombineUnder(parent, $"{transactionId}-{suffix}");
        }
        return candidate;
    }

    private static void ValidateTransactionId(string transactionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);
        if (!TransactionIdRegex().IsMatch(transactionId)
            || transactionId.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The transaction ID contains unsafe path characters.",
                nameof(transactionId));
        }
    }

    private static string CombineUnder(string root, params string[] parts)
    {
        var fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var combined = Path.GetFullPath(Path.Combine([root, .. parts]));
        if (!combined.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(
                combined.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException($"Path escaped the allowed save root: {combined}");
        }
        return combined;
    }

    private static bool IsPathUnder(string path, string root)
    {
        var fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
    }

    private static void SafeDeleteDirectory(string path, string allowedRoot)
    {
        if (!Directory.Exists(path))
        {
            return;
        }
        if (!IsPathUnder(path, allowedRoot))
        {
            throw new IOException($"Refusing to delete outside the allowed root: {path}");
        }
        Directory.Delete(path, recursive: true);
    }

    private static void TryRollback(
        Action action,
        string label,
        ICollection<string> errors)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            errors.Add($"{label}: {exception.Message}");
        }
    }

    [GeneratedRegex(@"^\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex SteamAccountRegex();

    [GeneratedRegex(@"^profile([1-3])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProfileRegex();

    [GeneratedRegex(@"^profile\d+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AnyProfileRegex();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex TransactionIdRegex();

    private sealed record ProfilePlan(
        string AccountId,
        string ProfileName,
        string VanillaPath,
        string ModdedPath,
        SaveProfileDisposition Disposition)
    {
        public int ProfileNumber =>
            int.Parse(ProfileRegex().Match(ProfileName).Groups[1].Value);
    }

    private sealed record Replacement(
        string TargetPath,
        string DisplacedPath,
        bool TargetExisted);

    private sealed record MarkerReplacement(
        string Path,
        byte[]? PreviousContents);
}
