using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Saves;

namespace TDBank.TDBankCode.Integration;

internal static class UninstallSaveHandoffBridge
{
    internal const int SchemaVersion = 1;
    internal const string Protocol = "tdbank-uninstall-save-handoff";
    internal const string PendingMarkerName =
        "tdbank_uninstall_sync_v1.pending.json";
    internal const string CompletedMarkerName =
        "tdbank_uninstall_sync_v1.completed.json";
    internal const string ReceiptName =
        "tdbank_uninstall_sync_v1.receipt.json";
    private const int CurrentProgressSchema = 22;
    private const int HistoryByteLimit = 5 * 1024 * 1024;
    private const int HistoryFileLimit = 100;
    private const int MaximumFiles = 4096;

    private static readonly FieldInfo? SaveStoreField =
        AccessTools.Field(typeof(SaveManager), "_saveStore");

    private static readonly HashSet<string> DirectSaveNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "progress.save",
            "prefs.save",
            "current_run.save",
            "current_run_mp.save",
        };

    internal static bool HasPendingMarker()
    {
        try
        {
            return System.IO.File.Exists(
                Path.Combine(ResolveAccountRoot(), PendingMarkerName));
        }
        catch
        {
            return false;
        }
    }

    internal static async Task ChainAfterNativeCloudSync(Task nativeCloudSync)
    {
        await nativeCloudSync.ConfigureAwait(false);

        var markerPath = string.Empty;
        HandoffMarker? marker = null;
        try
        {
            var accountRoot = ResolveAccountRoot();
            markerPath = Path.Combine(accountRoot, PendingMarkerName);
            if (!System.IO.File.Exists(markerPath))
            {
                return;
            }

            marker = LoadAndValidateMarker(markerPath, accountRoot);
            Execute(marker, markerPath, accountRoot);
            MainFile.Logger.Info(
                "Verified the TD Bank uninstall save handoff in local storage "
                + "and Steam Cloud. The Setup may now remove its mods.");
        }
        catch (Exception exception)
        {
            MainFile.Logger.Error(
                $"TD Bank uninstall save handoff failed closed: {exception}");
            TryWriteFailureReceipt(markerPath, marker, exception);
        }
        finally
        {



            RequestGameExit();
        }
    }

    private static void Execute(
        HandoffMarker marker,
        string markerPath,
        string accountRoot)
    {
        if (SaveStoreField?.GetValue(SaveManager.Instance)
            is not CloudSaveStore cloudStore)
        {
            throw new InvalidOperationException(
                "Steam Cloud is unavailable; no verified handoff can be issued.");
        }

        var baseline = ValidateLocalAndRemoteVanillaBaseline(
            marker,
            accountRoot,
            cloudStore.CloudStore);
        SnapshotRemoteBaseline(
            marker,
            accountRoot,
            cloudStore.CloudStore,
            baseline);

        var stagedFiles = BuildAndStageMigration(
            marker,
            accountRoot);
        if (stagedFiles.Count == 0)
        {
            throw new InvalidDataException(
                "The pending handoff contains no usable modded save files.");
        }

        var applied = new List<AppliedFile>();
        try
        {
            ApplyLocalFiles(marker, accountRoot, stagedFiles, applied);
            WriteAndVerifyCloud(cloudStore, stagedFiles);
            WriteSuccessReceipt(marker, markerPath, accountRoot, stagedFiles);

            var completedPath = Path.Combine(accountRoot, CompletedMarkerName);
            System.IO.File.Move(markerPath, completedPath, overwrite: true);
        }
        catch
        {
            var rollbackErrors = RollBackLocalFiles(applied);
            rollbackErrors.AddRange(
                TryRestoreRemoteBaseline(
                    cloudStore.CloudStore,
                    baseline,
                    stagedFiles.Select(file => file.TargetRelativePath)));
            if (rollbackErrors.Count > 0)
            {
                throw new AggregateException(
                    "Save handoff failed and one or more rollback steps also failed.",
                    rollbackErrors);
            }
            throw;
        }
    }

    private static HandoffMarker LoadAndValidateMarker(
        string markerPath,
        string accountRoot)
    {
        var marker = JsonSerializer.Deserialize<HandoffMarker>(
            System.IO.File.ReadAllText(markerPath, Encoding.UTF8),
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = false,
                MaxDepth = 256,
            }) ?? throw new InvalidDataException("Empty uninstall handoff marker.");

        var accountId = Path.GetFileName(
            accountRoot.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar));
        var saveRoot = Directory.GetParent(
            Directory.GetParent(accountRoot)?.FullName
            ?? throw new InvalidDataException("Invalid Steam account path."))
            ?.FullName
            ?? throw new InvalidDataException("Invalid save root.");
        if (marker.SchemaVersion != SchemaVersion
            || !string.Equals(marker.Protocol, Protocol, StringComparison.Ordinal)
            || !string.Equals(marker.ModId, MainFile.ModId, StringComparison.Ordinal)
            || !string.Equals(marker.AccountId, accountId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(marker.TransactionId)
            || !DateTimeOffset.TryParse(marker.CreatedUtc, out _)
            || !PathEquals(marker.SaveRoot, saveRoot)
            || marker.Profiles is null
            || marker.Profiles.Count == 0
            || marker.Profiles.Count > 3
            || marker.Profiles.Any(profile =>
                profile.ProfileId is < 1 or > 3
                || string.IsNullOrWhiteSpace(profile.UniqueId))
            || marker.Profiles.Select(profile => profile.ProfileId).Distinct().Count()
                != marker.Profiles.Count
            || marker.VanillaBaselineFiles is null
            || marker.VanillaBaselineFiles.Count > MaximumFiles)
        {
            throw new InvalidDataException(
                "Uninstall handoff marker identity or structure is invalid.");
        }

        var backupRoot = Path.GetFullPath(marker.BackupDirectory);
        if (!IsPathUnder(backupRoot, saveRoot)
            || IsReparsePoint(saveRoot)
            || IsReparsePoint(accountRoot)
            || IsReparsePoint(backupRoot))
        {
            throw new InvalidDataException(
                "Uninstall handoff backup path is unsafe.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var baseline in marker.VanillaBaselineFiles)
        {
            var relative = NormalizeRelativePath(baseline.TargetRelativePath);
            if (!IsAllowedVanillaCloudTarget(relative)
                || !IsSha256(baseline.Sha256)
                || !IsSha256(baseline.CloudSha256)
                || !seen.Add(relative!))
            {
                throw new InvalidDataException(
                    "Uninstall handoff marker contains an unsafe baseline file.");
            }
        }
        return marker;
    }

    private static Dictionary<string, RemoteBaseline> ValidateLocalAndRemoteVanillaBaseline(
        HandoffMarker marker,
        string accountRoot,
        ICloudSaveStore cloud)
    {
        var result = new Dictionary<string, RemoteBaseline>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var baseline in marker.VanillaBaselineFiles)
        {
            var relative = NormalizeRelativePath(baseline.TargetRelativePath)!;
            var localPath = ResolveWithin(accountRoot, relative);
            if (!System.IO.File.Exists(localPath)
                || !HashFile(localPath).Equals(
                    baseline.Sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Vanilla local save changed after Setup prepared the handoff: {relative}");
            }
            if (!cloud.FileExists(relative))
            {
                throw new InvalidDataException(
                    $"Steam Cloud no longer contains the expected vanilla save: {relative}");
            }

            var remoteText = cloud.ReadFile(relative);
            var remoteHash = HashText(remoteText);
            if (!remoteHash.Equals(
                    baseline.CloudSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Steam Cloud vanilla save diverged on another machine: {relative}");
            }
            result.Add(
                relative,
                new RemoteBaseline(
                    remoteText ?? string.Empty,
                    cloud.GetLastModifiedTime(relative),
                    cloud.IsFilePersisted(relative)));
        }



        foreach (var remote in EnumerateRemoteVanillaCloudFiles(cloud))
        {
            if (!result.ContainsKey(remote))
            {
                throw new InvalidDataException(
                    $"Steam Cloud contains a vanilla save not present at preparation time: {remote}");
            }
        }
        return result;
    }

    private static IEnumerable<string> EnumerateRemoteVanillaCloudFiles(
        ICloudSaveStore cloud)
    {
        if (cloud.FileExists("profile.save"))
        {
            yield return "profile.save";
        }

        for (var profileId = 1; profileId <= 3; profileId++)
        {
            foreach (var name in DirectSaveNames)
            {
                var path = $"profile{profileId}/saves/{name}";
                if (cloud.FileExists(path))
                {
                    yield return path;
                }
            }

            var history = $"profile{profileId}/saves/history";
            if (!cloud.DirectoryExists(history))
            {
                continue;
            }
            foreach (var file in cloud.GetFilesInDirectory(history))
            {
                if (file.EndsWith(".run", StringComparison.OrdinalIgnoreCase))
                {
                    yield return $"{history}/{file}";
                }
            }
        }
    }

    private static void SnapshotRemoteBaseline(
        HandoffMarker marker,
        string accountRoot,
        ICloudSaveStore cloud,
        IReadOnlyDictionary<string, RemoteBaseline> baseline)
    {
        var remoteRoot = ResolveBackupPath(
            marker,
            "remote-before",
            marker.AccountId);
        Directory.CreateDirectory(remoteRoot);
        foreach (var (relative, item) in baseline)
        {
            var path = ResolveWithin(remoteRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(
                path,
                item.Contents,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            if (!HashText(
                    System.IO.File.ReadAllText(path, Encoding.UTF8)).Equals(
                    HashText(item.Contents),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException(
                    $"Remote save snapshot verification failed: {relative}");
            }
        }

        var manifestPath = Path.Combine(remoteRoot, "remote-manifest.json");
        var manifest = baseline.Select(pair => new
        {
            target_relative_path = pair.Key,
            sha256 = HashText(pair.Value.Contents),
            last_modified_utc = pair.Value.LastModifiedUtc,
            was_persisted = pair.Value.WasPersisted,
        });
        WriteJsonAtomically(manifestPath, manifest);
    }

    private static List<StagedFile> BuildAndStageMigration(
        HandoffMarker marker,
        string accountRoot)
    {
        var stageRoot = ResolveBackupPath(
            marker,
            "handoff-working",
            marker.TransactionId,
            "stage",
            marker.AccountId);
        if (Directory.Exists(stageRoot))
        {
            Directory.Delete(stageRoot, recursive: true);
        }
        Directory.CreateDirectory(stageRoot);

        var staged = new List<StagedFile>();
        var selectorSource = Path.Combine(accountRoot, "modded", "profile.save");
        if (System.IO.File.Exists(selectorSource))
        {
            StageFile(
                selectorSource,
                "profile.save",
                stageRoot,
                sanitizeRun: false,
                isHistory: false,
                staged);
        }

        foreach (var profile in marker.Profiles.OrderBy(profile => profile.ProfileId))
        {
            var sourceRoot = Path.Combine(
                accountRoot,
                "modded",
                $"profile{profile.ProfileId}");
            RejectTreeReparsePoints(sourceRoot);
            var progress = Path.Combine(sourceRoot, "saves", "progress.save");
            var identity = ReadProgressIdentity(progress);
            if (identity.SchemaVersion != CurrentProgressSchema
                || !string.Equals(
                    identity.UniqueId,
                    profile.UniqueId,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Modded profile identity/schema changed during native cloud sync: profile{profile.ProfileId}");
            }

            foreach (var name in DirectSaveNames)
            {
                var source = Path.Combine(sourceRoot, "saves", name);
                if (!System.IO.File.Exists(source))
                {
                    continue;
                }
                StageFile(
                    source,
                    $"profile{profile.ProfileId}/saves/{name}",
                    stageRoot,
                    sanitizeRun: name.StartsWith(
                        "current_run",
                        StringComparison.OrdinalIgnoreCase),
                    isHistory: false,
                    staged);
            }

            var historyRoot = Path.Combine(sourceRoot, "saves", "history");
            if (!Directory.Exists(historyRoot))
            {
                continue;
            }
            RejectTreeReparsePoints(historyRoot);
            foreach (var source in Directory.EnumerateFiles(
                         historyRoot,
                         "*.run",
                         SearchOption.TopDirectoryOnly))
            {
                var targetRelative =
                    $"profile{profile.ProfileId}/saves/history/{Path.GetFileName(source)}";
                var target = ResolveWithin(accountRoot, targetRelative);
                if (System.IO.File.Exists(target))
                {
                    if (!FilesEquivalent(source, target))
                    {
                        throw new InvalidDataException(
                            $"Run-history collision changed during native cloud sync: {targetRelative}");
                    }
                    continue;
                }

                StageFile(
                    source,
                    targetRelative,
                    stageRoot,
                    sanitizeRun: false,
                    isHistory: true,
                    staged);
            }
        }

        if (staged.Count > MaximumFiles
            || staged.Select(file => file.TargetRelativePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() != staged.Count)
        {
            throw new InvalidDataException(
                "The save handoff file plan is too large or contains duplicates.");
        }
        return staged;
    }

    private static void StageFile(
        string source,
        string targetRelative,
        string stageRoot,
        bool sanitizeRun,
        bool isHistory,
        ICollection<StagedFile> staged)
    {
        if (!IsAllowedVanillaCloudTarget(targetRelative)
            || IsReparsePoint(source))
        {
            throw new InvalidDataException(
                $"Unsafe save handoff source or target: {targetRelative}");
        }

        var stagePath = ResolveWithin(stageRoot, targetRelative);
        Directory.CreateDirectory(Path.GetDirectoryName(stagePath)!);
        if (sanitizeRun)
        {
            var root = JsonNode.Parse(
                System.IO.File.ReadAllText(source, Encoding.UTF8),
                documentOptions: new JsonDocumentOptions { MaxDepth = 512 })
                ?? throw new InvalidDataException($"Empty current-run save: {source}");
            SanitizeCurrentRun(root, source);
            System.IO.File.WriteAllText(
                stagePath,
                root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        else
        {
            System.IO.File.Copy(source, stagePath, overwrite: false);
        }

        using (JsonDocument.Parse(
                   System.IO.File.ReadAllBytes(stagePath),
                   new JsonDocumentOptions { MaxDepth = 512 }))
        {

        }
        staged.Add(
            new StagedFile(
                targetRelative,
                stagePath,
                HashFile(stagePath),
                HashText(System.IO.File.ReadAllText(stagePath, Encoding.UTF8)),
                isHistory));
    }

    internal static void SanitizeCurrentRun(JsonNode node, string sourceLabel)
    {
        if (node is JsonObject obj)
        {
            foreach (var property in obj.ToArray())
            {
                if (property.Key.StartsWith("save_dict_", StringComparison.Ordinal))
                {
                    if (string.Equals(
                            property.Key,
                            "save_dict_TDBank.TDBankCode.Banking.AccountState",
                            StringComparison.Ordinal))
                    {
                        obj.Remove(property.Key);
                        continue;
                    }

                    if (string.Equals(
                            property.Key,
                            "save_dict_List[BaseLib.Abstracts.CardModifier+ModifierSave]",
                            StringComparison.Ordinal)
                        && property.Value is JsonArray { Count: 0 })
                    {
                        obj.Remove(property.Key);
                        continue;
                    }

                    throw new InvalidDataException(
                        $"Another mod's custom current-run data was found in {sourceLabel}: {property.Key}");
                }

                if (property.Value is not null)
                {
                    SanitizeCurrentRun(property.Value, sourceLabel);
                }
            }
            return;
        }

        if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                if (item is not null)
                {
                    SanitizeCurrentRun(item, sourceLabel);
                }
            }
        }
    }

    private static void ApplyLocalFiles(
        HandoffMarker marker,
        string accountRoot,
        IReadOnlyList<StagedFile> staged,
        ICollection<AppliedFile> applied)
    {
        var rollbackRoot = ResolveBackupPath(
            marker,
            "handoff-working",
            marker.TransactionId,
            "rollback",
            marker.AccountId);
        Directory.CreateDirectory(rollbackRoot);

        foreach (var file in staged)
        {
            var target = ResolveWithin(accountRoot, file.TargetRelativePath);
            var rollback = ResolveWithin(rollbackRoot, file.TargetRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            Directory.CreateDirectory(Path.GetDirectoryName(rollback)!);

            var existed = System.IO.File.Exists(target);
            DateTime? previousWriteTimeUtc = null;
            if (existed)
            {
                if (IsReparsePoint(target))
                {
                    throw new InvalidDataException(
                        $"Refusing a reparse-point save target: {file.TargetRelativePath}");
                }
                previousWriteTimeUtc = System.IO.File.GetLastWriteTimeUtc(target);
                System.IO.File.Copy(target, rollback, overwrite: false);
            }

            var temporary = target + $".tdbank-handoff-{Guid.NewGuid():N}.tmp";
            System.IO.File.Copy(file.StagePath, temporary, overwrite: false);
            try
            {
                if (existed)
                {
                    System.IO.File.Move(temporary, target, overwrite: true);
                }
                else
                {
                    System.IO.File.Move(temporary, target);
                }
            }
            finally
            {
                if (System.IO.File.Exists(temporary))
                {
                    System.IO.File.Delete(temporary);
                }
            }



            applied.Add(
                new AppliedFile(
                    target,
                    rollback,
                    existed,
                    previousWriteTimeUtc));
            if (!HashFile(target).Equals(
                    file.LocalSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException(
                    $"Local save handoff verification failed: {file.TargetRelativePath}");
            }
        }
    }

    private static void WriteAndVerifyCloud(
        CloudSaveStore cloudStore,
        IReadOnlyList<StagedFile> staged)
    {
        foreach (var file in staged
                     .OrderBy(file => CommitOrder(file.TargetRelativePath)))
        {
            var contents = System.IO.File.ReadAllText(file.StagePath, Encoding.UTF8);
            if (file.IsHistory)
            {
                var history = file.TargetRelativePath[..file.TargetRelativePath.LastIndexOf('/')];
                var byteCount = Encoding.UTF8.GetByteCount(contents);
                if (byteCount <= HistoryByteLimit)
                {
                    cloudStore.ForgetFilesInDirectoryBeforeWritingIfNecessary(
                        history,
                        byteCount,
                        HistoryByteLimit,
                        HistoryFileLimit);
                }
            }

            cloudStore.WriteFile(file.TargetRelativePath, contents);
            if (!cloudStore.CloudStore.FileExists(file.TargetRelativePath)
                || !HashText(
                        cloudStore.CloudStore.ReadFile(file.TargetRelativePath))
                    .Equals(file.CloudSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException(
                    $"Steam Cloud did not verify the returned save: {file.TargetRelativePath}");
            }
            if (file.IsHistory
                && Encoding.UTF8.GetByteCount(contents) > HistoryByteLimit)
            {
                cloudStore.CloudStore.ForgetFile(file.TargetRelativePath);
                if (cloudStore.CloudStore.IsFilePersisted(file.TargetRelativePath))
                {
                    throw new IOException(
                        $"Oversized run history remained persisted in Steam Cloud: {file.TargetRelativePath}");
                }
            }
        }
    }

    private static int CommitOrder(string relative)
    {
        if (relative.EndsWith(".run", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }
        if (relative.Contains("current_run", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }
        if (relative.EndsWith("prefs.save", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }
        if (relative.Equals("profile.save", StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }
        return 4;
    }

    private static List<Exception> RollBackLocalFiles(
        IReadOnlyList<AppliedFile> applied)
    {
        var errors = new List<Exception>();
        foreach (var file in applied.Reverse())
        {
            try
            {
                if (file.Existed)
                {
                    if (!System.IO.File.Exists(file.RollbackPath))
                    {
                        throw new IOException(
                            $"Missing local save rollback copy: {file.RollbackPath}");
                    }
                    System.IO.File.Copy(
                        file.RollbackPath,
                        file.TargetPath,
                        overwrite: true);
                    if (file.PreviousWriteTimeUtc.HasValue)
                    {
                        System.IO.File.SetLastWriteTimeUtc(
                            file.TargetPath,
                            file.PreviousWriteTimeUtc.Value);
                    }
                }
                else if (System.IO.File.Exists(file.TargetPath))
                {
                    System.IO.File.Delete(file.TargetPath);
                }
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }
        }
        return errors;
    }

    private static IEnumerable<Exception> TryRestoreRemoteBaseline(
        ICloudSaveStore cloud,
        IReadOnlyDictionary<string, RemoteBaseline> baseline,
        IEnumerable<string> migratedTargets)
    {
        var errors = new List<Exception>();
        try
        {



            foreach (var target in baseline.Keys
                         .Concat(migratedTargets)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    if (baseline.TryGetValue(target, out var original))
                    {
                        cloud.WriteFile(target, original.Contents);
                        if (!HashText(cloud.ReadFile(target)).Equals(
                                HashText(original.Contents),
                                StringComparison.OrdinalIgnoreCase))
                        {
                            throw new IOException(
                                $"Remote rollback hash mismatch: {target}");
                        }
                        if (!original.WasPersisted)
                        {
                            cloud.ForgetFile(target);
                        }
                        if (cloud.IsFilePersisted(target) != original.WasPersisted)
                        {
                            throw new IOException(
                                $"Remote rollback persistence mismatch: {target}");
                        }
                    }
                    else if (cloud.FileExists(target))
                    {
                        cloud.DeleteFile(target);
                    }
                }
                catch (Exception exception)
                {
                    errors.Add(exception);
                }
            }
        }
        catch (Exception exception)
        {
            errors.Add(exception);
        }
        return errors;
    }

    private static void WriteSuccessReceipt(
        HandoffMarker marker,
        string markerPath,
        string accountRoot,
        IReadOnlyCollection<StagedFile> staged)
    {
        var receipt = new HandoffReceipt
        {
            SchemaVersion = SchemaVersion,
            Protocol = Protocol,
            ModId = MainFile.ModId,
            TransactionId = marker.TransactionId,
            AccountId = marker.AccountId,
            MarkerSha256 = HashFile(markerPath),
            CompletedUtc = DateTimeOffset.UtcNow.ToString("O"),
            Success = true,
            CloudStatus = "verified",
            Files = staged
                .OrderBy(file => file.TargetRelativePath, StringComparer.OrdinalIgnoreCase)
                .Select(file => new ReceiptFile
                {
                    TargetRelativePath = file.TargetRelativePath,
                    Sha256 = file.LocalSha256,
                })
                .ToList(),
        };
        WriteJsonAtomically(Path.Combine(accountRoot, ReceiptName), receipt);
    }

    private static void TryWriteFailureReceipt(
        string markerPath,
        HandoffMarker? marker,
        Exception exception)
    {
        try
        {
            if (marker is null || string.IsNullOrWhiteSpace(markerPath))
            {
                return;
            }
            var accountRoot = Path.GetDirectoryName(markerPath)!;
            var receipt = new HandoffReceipt
            {
                SchemaVersion = SchemaVersion,
                Protocol = Protocol,
                ModId = MainFile.ModId,
                TransactionId = marker.TransactionId,
                AccountId = marker.AccountId,
                MarkerSha256 = System.IO.File.Exists(markerPath)
                    ? HashFile(markerPath)
                    : string.Empty,
                CompletedUtc = DateTimeOffset.UtcNow.ToString("O"),
                Success = false,
                CloudStatus = "failed",
                Detail = exception.Message,
                Files = [],
            };
            WriteJsonAtomically(Path.Combine(accountRoot, ReceiptName), receipt);
        }
        catch (Exception receiptException)
        {
            MainFile.Logger.Warn(
                $"Could not write the failed save-handoff receipt: {receiptException.Message}");
        }
    }

    private static void RequestGameExit()
    {
        try
        {
            Callable.From(() =>
            {
                if (Engine.GetMainLoop() is SceneTree tree)
                {
                    tree.Quit();
                }
            }).CallDeferred();
        }
        catch (Exception exception)
        {
            MainFile.Logger.Warn(
                $"Save handoff completed, but automatic game exit failed: {exception.Message}");
        }
    }

    private static ProgressIdentity ReadProgressIdentity(string path)
    {
        using var document = JsonDocument.Parse(
            System.IO.File.ReadAllBytes(path),
            new JsonDocumentOptions { MaxDepth = 256 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("schema_version", out var schema)
            || !schema.TryGetInt32(out var schemaVersion)
            || !root.TryGetProperty("unique_id", out var uniqueId)
            || uniqueId.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(uniqueId.GetString()))
        {
            throw new InvalidDataException($"Invalid progress identity: {path}");
        }
        return new ProgressIdentity(schemaVersion, uniqueId.GetString()!);
    }

    internal static bool IsAllowedVanillaCloudTarget(string? relativePath)
    {
        var normalized = NormalizeRelativePath(relativePath);
        if (normalized is null)
        {
            return false;
        }
        if (normalized.Equals("profile.save", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var parts = normalized.Split('/');
        if (parts.Length == 3
            && IsProfile(parts[0])
            && parts[1].Equals("saves", StringComparison.OrdinalIgnoreCase)
            && DirectSaveNames.Contains(parts[2]))
        {
            return true;
        }
        return parts.Length == 4
            && IsProfile(parts[0])
            && parts[1].Equals("saves", StringComparison.OrdinalIgnoreCase)
            && parts[2].Equals("history", StringComparison.OrdinalIgnoreCase)
            && parts[3].EndsWith(".run", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsProfile(string value)
    {
        return value.Length == "profile1".Length
            && value.StartsWith("profile", StringComparison.OrdinalIgnoreCase)
            && value[^1] is >= '1' and <= '3';
    }

    private static string? NormalizeRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }
        var normalized = path.Replace('\\', '/').Trim();
        if (normalized.StartsWith("/", StringComparison.Ordinal)
            || normalized.Contains(':', StringComparison.Ordinal)
            || normalized.Split('/').Any(segment =>
                segment.Length == 0 || segment is "." or ".."))
        {
            return null;
        }
        return normalized;
    }

    private static string ResolveAccountRoot()
    {
        var path = ProjectSettings.GlobalizePath(
            UserDataPathProvider.GetAccountScopedBasePath(null));
        return Path.GetFullPath(path);
    }

    private static string ResolveWithin(string root, string relativePath)
    {
        var normalized = NormalizeRelativePath(relativePath)
            ?? throw new InvalidDataException($"Unsafe relative path: {relativePath}");
        var full = Path.GetFullPath(Path.Combine(
            root,
            normalized.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsPathUnder(full, root))
        {
            throw new InvalidDataException($"Path escaped its root: {relativePath}");
        }
        return full;
    }

    private static string ResolveBackupPath(
        HandoffMarker marker,
        params string[] parts)
    {
        var root = Path.GetFullPath(marker.BackupDirectory);
        var current = root;
        foreach (var part in parts)
        {
            current = ResolveWithin(current, part);
        }
        if (!IsPathUnder(current, root))
        {
            throw new InvalidDataException("Backup path escaped its root.");
        }
        return current;
    }

    private static bool IsPathUnder(string path, string root)
    {
        var fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)
            || PathEquals(fullPath, root);
    }

    private static bool PathEquals(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    private static void RejectTreeReparsePoints(string root)
    {
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException(root);
        }
        if (IsReparsePoint(root))
        {
            throw new InvalidDataException($"Reparse-point save root: {root}");
        }
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                if (IsReparsePoint(entry))
                {
                    throw new InvalidDataException(
                        $"Reparse point inside save tree: {entry}");
                }
                if (Directory.Exists(entry))
                {
                    pending.Push(entry);
                }
            }
        }
    }

    private static bool IsReparsePoint(string path)
    {
        return (System.IO.File.Exists(path) || Directory.Exists(path))
            && (System.IO.File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    }

    private static bool FilesEquivalent(string left, string right)
    {
        return new FileInfo(left).Length == new FileInfo(right).Length
            && CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(System.IO.File.ReadAllBytes(left)),
                SHA256.HashData(System.IO.File.ReadAllBytes(right)));
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

    private static bool IsSha256(string? hash)
    {
        return hash is { Length: 64 }
            && hash.All(character =>
                character is >= '0' and <= '9'
                or >= 'a' and <= 'f'
                or >= 'A' and <= 'F');
    }

    private static void WriteJsonAtomically<T>(string path, T value)
    {
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                System.IO.FileAccess.Write,
                FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(
                    stream,
                    value,
                    new JsonSerializerOptions { WriteIndented = true });
                stream.Flush(flushToDisk: true);
            }
            System.IO.File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (System.IO.File.Exists(temporary))
            {
                System.IO.File.Delete(temporary);
            }
        }
    }

    private sealed record ProgressIdentity(int SchemaVersion, string UniqueId);

    private sealed record RemoteBaseline(
        string Contents,
        DateTimeOffset LastModifiedUtc,
        bool WasPersisted);

    private sealed record StagedFile(
        string TargetRelativePath,
        string StagePath,
        string LocalSha256,
        string CloudSha256,
        bool IsHistory);

    private sealed record AppliedFile(
        string TargetPath,
        string RollbackPath,
        bool Existed,
        DateTime? PreviousWriteTimeUtc);

    private sealed class HandoffMarker
    {
        [JsonPropertyName("schema_version")]
        public int SchemaVersion { get; init; }

        [JsonPropertyName("protocol")]
        public string Protocol { get; init; } = string.Empty;

        [JsonPropertyName("mod_id")]
        public string ModId { get; init; } = string.Empty;

        [JsonPropertyName("transaction_id")]
        public string TransactionId { get; init; } = string.Empty;

        [JsonPropertyName("account_id")]
        public string AccountId { get; init; } = string.Empty;

        [JsonPropertyName("created_utc")]
        public string CreatedUtc { get; init; } = string.Empty;

        [JsonPropertyName("save_root")]
        public string SaveRoot { get; init; } = string.Empty;

        [JsonPropertyName("backup_directory")]
        public string BackupDirectory { get; init; } = string.Empty;

        [JsonPropertyName("profiles")]
        public List<HandoffProfile> Profiles { get; init; } = [];

        [JsonPropertyName("vanilla_baseline_files")]
        public List<HandoffBaselineFile> VanillaBaselineFiles { get; init; } = [];
    }

    private sealed class HandoffProfile
    {
        [JsonPropertyName("profile_id")]
        public int ProfileId { get; init; }

        [JsonPropertyName("unique_id")]
        public string UniqueId { get; init; } = string.Empty;
    }

    private sealed class HandoffBaselineFile
    {
        [JsonPropertyName("target_relative_path")]
        public string TargetRelativePath { get; init; } = string.Empty;

        [JsonPropertyName("sha256")]
        public string Sha256 { get; init; } = string.Empty;

        [JsonPropertyName("cloud_sha256")]
        public string CloudSha256 { get; init; } = string.Empty;
    }

    private sealed class HandoffReceipt
    {
        [JsonPropertyName("schema_version")]
        public int SchemaVersion { get; init; }

        [JsonPropertyName("protocol")]
        public string Protocol { get; init; } = string.Empty;

        [JsonPropertyName("mod_id")]
        public string ModId { get; init; } = string.Empty;

        [JsonPropertyName("transaction_id")]
        public string TransactionId { get; init; } = string.Empty;

        [JsonPropertyName("account_id")]
        public string AccountId { get; init; } = string.Empty;

        [JsonPropertyName("marker_sha256")]
        public string MarkerSha256 { get; init; } = string.Empty;

        [JsonPropertyName("completed_utc")]
        public string CompletedUtc { get; init; } = string.Empty;

        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("cloud_status")]
        public string CloudStatus { get; init; } = string.Empty;

        [JsonPropertyName("detail")]
        public string? Detail { get; init; }

        [JsonPropertyName("files")]
        public List<ReceiptFile> Files { get; init; } = [];
    }

    private sealed class ReceiptFile
    {
        [JsonPropertyName("target_relative_path")]
        public string TargetRelativePath { get; init; } = string.Empty;

        [JsonPropertyName("sha256")]
        public string Sha256 { get; init; } = string.Empty;
    }
}

[HarmonyPatch(typeof(NGame), "DoCloudSync")]
internal static class UninstallSaveHandoffCloudSyncPatch
{
    [HarmonyPostfix]
    private static void Postfix(ref Task __result)
    {
        if (UninstallSaveHandoffBridge.HasPendingMarker())
        {
            __result = UninstallSaveHandoffBridge.ChainAfterNativeCloudSync(
                __result);
        }
    }
}
