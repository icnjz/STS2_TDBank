using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace CNJ.TowerDebt.Setup.Core;

internal static partial class UninstallSaveHandoff
{
    internal const int SchemaVersion = 1;
    internal const string Protocol = "tdbank-uninstall-save-handoff";
    internal const string PendingMarkerName =
        "tdbank_uninstall_sync_v1.pending.json";
    internal const string ReceiptName =
        "tdbank_uninstall_sync_v1.receipt.json";
    private const string BackupFolderName =
        "cnj-tower-debt-save-backups";
    private const int MaximumSupportedProgressSchema = 22;
    private const int MaximumMarkerFiles = 4096;

    private static readonly HashSet<string> DirectSaveNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "progress.save",
            "prefs.save",
            "current_run.save",
            "current_run_mp.save",
        };

    internal static UninstallSaveHandoffPreparation Prepare(
   string saveRoot,
   string transactionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(saveRoot);
        ValidateTransactionId(transactionId);

        var fullSaveRoot = Path.GetFullPath(saveRoot);
        if (!Directory.Exists(fullSaveRoot))
        {
            return new UninstallSaveHandoffPreparation(
                fullSaveRoot,
                string.Empty,
                transactionId,
                []);
        }

        RejectReparsePoint(fullSaveRoot, "save root");
        var steamRoot = CombineUnder(fullSaveRoot, "steam");
        if (!Directory.Exists(steamRoot))
        {
            return new UninstallSaveHandoffPreparation(
                fullSaveRoot,
                string.Empty,
                transactionId,
                []);
        }
        RejectReparsePoint(steamRoot, "Steam save root");

        var accounts = Directory.EnumerateDirectories(steamRoot)
            .Where(path => SteamAccountRegex().IsMatch(Path.GetFileName(path)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var plans = new List<AccountPlan>();
        foreach (var accountRoot in accounts)
        {
            plans.Add(BuildAccountPlan(fullSaveRoot, accountRoot));
        }

        var eligiblePlans = plans
            .Where(plan => plan.Profiles.Count > 0)
            .ToArray();
        if (eligiblePlans.Length == 0)
        {
            return new UninstallSaveHandoffPreparation(
                fullSaveRoot,
                string.Empty,
                transactionId,
                []);
        }

        var backupRoot = AllocateBackupRoot(fullSaveRoot, transactionId);
        var writtenMarkers = new List<string>();
        try
        {
            Directory.CreateDirectory(backupRoot);
            RejectReparsePoint(backupRoot, "save handoff backup");

            foreach (var plan in plans)
            {
                SnapshotAccount(plan, backupRoot);
            }

            var markers = new List<UninstallSaveHandoffAccount>();
            foreach (var plan in eligiblePlans)
            {
                var marker = BuildMarker(
                    fullSaveRoot,
                    backupRoot,
                    transactionId,
                    plan);
                var markerPath = Path.Combine(plan.AccountRoot, PendingMarkerName);
                var receiptPath = Path.Combine(plan.AccountRoot, ReceiptName);
                if (File.Exists(receiptPath))
                {
                    CopyFileIfPresent(
                        receiptPath,
                        CombineUnder(
                            backupRoot,
                            "previous-protocol",
                            plan.AccountId,
                            ReceiptName));
                    File.Delete(receiptPath);
                }
                WriteJsonAtomically(markerPath, marker);
                writtenMarkers.Add(markerPath);
                markers.Add(
                    new UninstallSaveHandoffAccount(
                        plan.AccountId,
                        plan.AccountRoot,
                        markerPath,
                        receiptPath,
                        HashFile(markerPath),
                        plan.Profiles.Select(profile => profile.ProfileId).ToArray()));
            }

            WritePreparationManifest(
                backupRoot,
                fullSaveRoot,
                transactionId,
                plans,
                markers);
            return new UninstallSaveHandoffPreparation(
                fullSaveRoot,
                backupRoot,
                transactionId,
                markers);
        }
        catch
        {
            foreach (var markerPath in writtenMarkers)
            {
                TryDeleteMarkerForTransaction(markerPath, transactionId);
            }
            throw;
        }
    }

    internal static UninstallSaveHandoffInspection Inspect(
   UninstallSaveHandoffPreparation preparation)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        if (preparation.Accounts.Count == 0)
        {
            return new UninstallSaveHandoffInspection(
                UninstallSaveHandoffState.ReadyToRemoveMods,
                preparation,
                []);
        }

        var accountResults = new List<UninstallSaveHandoffAccountInspection>();
        var anyFailed = false;
        var anyPending = false;
        foreach (var account in preparation.Accounts)
        {
            var inspection = InspectAccount(preparation, account);
            accountResults.Add(inspection);
            anyFailed |= inspection.State == UninstallSaveHandoffState.Failed;
            anyPending |= inspection.State == UninstallSaveHandoffState.PendingGameHandoff;
        }

        var state = anyFailed
            ? UninstallSaveHandoffState.Failed
            : anyPending
                ? UninstallSaveHandoffState.PendingGameHandoff
                : UninstallSaveHandoffState.ReadyToRemoveMods;
        return new UninstallSaveHandoffInspection(
            state,
            preparation,
            accountResults);
    }

    private static UninstallSaveHandoffAccountInspection InspectAccount(
        UninstallSaveHandoffPreparation preparation,
        UninstallSaveHandoffAccount account)
    {
        if (!File.Exists(account.ReceiptPath))
        {
            return new UninstallSaveHandoffAccountInspection(
                account.AccountId,
                UninstallSaveHandoffState.PendingGameHandoff,
                "The game has not written a save-handoff receipt.");
        }

        try
        {
            using var document = JsonDocument.Parse(
                File.ReadAllBytes(account.ReceiptPath),
                new JsonDocumentOptions { MaxDepth = 256 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || root.GetProperty("schema_version").GetInt32() != SchemaVersion
                || !string.Equals(
                    root.GetProperty("protocol").GetString(),
                    Protocol,
                    StringComparison.Ordinal)
                || !string.Equals(
                    root.GetProperty("mod_id").GetString(),
                    "TDBank",
                    StringComparison.Ordinal)
                || !string.Equals(
                    root.GetProperty("transaction_id").GetString(),
                    preparation.TransactionId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    root.GetProperty("account_id").GetString(),
                    account.AccountId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    root.GetProperty("marker_sha256").GetString(),
                    account.MarkerSha256,
                    StringComparison.OrdinalIgnoreCase)
                || !root.GetProperty("success").GetBoolean()
                || !string.Equals(
                    root.GetProperty("cloud_status").GetString(),
                    "verified",
                    StringComparison.Ordinal))
            {
                return FailedReceipt(account, "Receipt identity or cloud verification is invalid.");
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in root.GetProperty("files").EnumerateArray())
            {
                var relative = NormalizeRelativePath(
                    item.GetProperty("target_relative_path").GetString());
                var hash = item.GetProperty("sha256").GetString();
                if (!IsAllowedVanillaCloudTarget(relative)
                    || !IsSha256(hash)
                    || !seen.Add(relative!))
                {
                    return FailedReceipt(account, "Receipt contains an unsafe or duplicate file.");
                }

                var target = ResolveUnder(account.AccountRoot, relative!);
                if (!File.Exists(target)
                    || !string.Equals(
                        HashFile(target),
                        hash,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return FailedReceipt(account, "A synchronized local save no longer matches its receipt.");
                }
            }

            if (seen.Count == 0)
            {
                return FailedReceipt(account, "Receipt contains no synchronized files.");
            }
            foreach (var profileId in account.ProfileIds)
            {
                if (!seen.Contains($"profile{profileId}/saves/progress.save"))
                {
                    return FailedReceipt(
                        account,
                        $"Receipt omitted profile{profileId} progress.");
                }
            }

            return new UninstallSaveHandoffAccountInspection(
                account.AccountId,
                UninstallSaveHandoffState.ReadyToRemoveMods,
                $"Verified {seen.Count} synchronized save files.");
        }
        catch (Exception exception)
        {
            return FailedReceipt(account, $"Unreadable receipt: {exception.Message}");
        }
    }

    private static UninstallSaveHandoffAccountInspection FailedReceipt(
        UninstallSaveHandoffAccount account,
        string detail)
    {
        return new UninstallSaveHandoffAccountInspection(
            account.AccountId,
            UninstallSaveHandoffState.Failed,
            detail);
    }

    private static AccountPlan BuildAccountPlan(
        string saveRoot,
        string accountRoot)
    {
        RejectTreeReparsePoints(accountRoot, "Steam account save");
        var accountId = Path.GetFileName(accountRoot);
        var profiles = new List<ProfilePlan>();

        for (var profileId = 1; profileId <= 3; profileId++)
        {
            var sourceRoot = Path.Combine(
                accountRoot,
                "modded",
                $"profile{profileId}");
            if (!Directory.Exists(sourceRoot))
            {
                continue;
            }
            RejectTreeReparsePoints(sourceRoot, "modded profile");

            var sourceProgress = Path.Combine(
                sourceRoot,
                "saves",
                "progress.save");
            if (!File.Exists(sourceProgress))
            {
                continue;
            }

            var sourceIdentity = ReadProgressIdentity(sourceProgress);
            if (!GameValidator.IsSupportedProgressSchema(
                    sourceIdentity.SchemaVersion))
            {
                throw new InvalidDataException(
                    $"Unsupported modded progress schema {sourceIdentity.SchemaVersion}: {sourceProgress}");
            }

            var targetRoot = Path.Combine(accountRoot, $"profile{profileId}");
            RejectTreeReparsePoints(targetRoot, "vanilla profile");
            var targetProgress = Path.Combine(
                targetRoot,
                "saves",
                "progress.save");
            if (File.Exists(targetProgress))
            {
                var targetIdentity = ReadProgressIdentity(targetProgress);
                if (targetIdentity.SchemaVersion is < 1
                        or > MaximumSupportedProgressSchema
                    || !string.Equals(
                        sourceIdentity.UniqueId,
                        targetIdentity.UniqueId,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        $"Vanilla/modded profile identity conflict for account {accountId}, profile{profileId}.");
                }
            }

            ValidateCurrentRuns(sourceRoot);
            ValidateHistoryCollisions(sourceRoot, targetRoot);
            profiles.Add(
                new ProfilePlan(
                    profileId,
                    sourceIdentity.UniqueId,
                    sourceRoot,
                    targetRoot));
        }

        return new AccountPlan(
            saveRoot,
            accountId,
            accountRoot,
            profiles);
    }

    private static void ValidateCurrentRuns(string sourceProfileRoot)
    {
        foreach (var name in new[]
                 {
                     "current_run.save",
                     "current_run_mp.save",
                     "current_run.save.backup",
                     "current_run_mp.save.backup",
                 })
        {
            var path = Path.Combine(sourceProfileRoot, "saves", name);
            if (!File.Exists(path))
            {
                continue;
            }
            using var document = JsonDocument.Parse(
                File.ReadAllBytes(path),
                new JsonDocumentOptions { MaxDepth = 512 });
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty(
                    "schema_version",
                    out var schema)
                || !schema.TryGetInt32(out var schemaVersion)
                || schemaVersion <= 0)
            {
                throw new InvalidDataException($"Unrecognized current-run save: {path}");
            }
            ValidateNoForeignSaveDictionary(document.RootElement, path);
        }
    }

    private static void ValidateNoForeignSaveDictionary(
        JsonElement element,
        string path)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.StartsWith(
                        "save_dict_",
                        StringComparison.Ordinal)
                    && !IsKnownRemovableSaveDictionary(
                        property.Name,
                        property.Value))
                {
                    throw new InvalidDataException(
                        $"Another mod's custom current-run data was found in {path}: {property.Name}");
                }
                ValidateNoForeignSaveDictionary(property.Value, path);
            }
            return;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                ValidateNoForeignSaveDictionary(item, path);
            }
        }
    }

    private static bool IsKnownRemovableSaveDictionary(
        string name,
        JsonElement value)
    {
        if (string.Equals(
                name,
                "save_dict_TDBank.TDBankCode.Banking.AccountState",
                StringComparison.Ordinal))
        {
            return value.ValueKind == JsonValueKind.Object;
        }

        return string.Equals(
                   name,
                   "save_dict_List[BaseLib.Abstracts.CardModifier+ModifierSave]",
                   StringComparison.Ordinal)
               && value.ValueKind == JsonValueKind.Array
               && value.GetArrayLength() == 0;
    }

    private static void ValidateHistoryCollisions(
        string sourceProfileRoot,
        string targetProfileRoot)
    {
        var sourceHistory = Path.Combine(sourceProfileRoot, "saves", "history");
        var targetHistory = Path.Combine(targetProfileRoot, "saves", "history");
        if (!Directory.Exists(sourceHistory) || !Directory.Exists(targetHistory))
        {
            return;
        }

        foreach (var source in Directory.EnumerateFiles(
                     sourceHistory,
                     "*.run",
                     SearchOption.TopDirectoryOnly))
        {
            var target = Path.Combine(targetHistory, Path.GetFileName(source));
            if (File.Exists(target) && !FilesEquivalent(source, target))
            {
                throw new InvalidDataException(
                    $"Run-history collision has different contents: {target}");
            }
        }
    }

    private static HandoffMarker BuildMarker(
        string saveRoot,
        string backupRoot,
        string transactionId,
        AccountPlan plan)
    {
        var baseline = EnumerateVanillaCloudFiles(plan.AccountRoot)
            .Select(pair => new HandoffBaselineFile
            {
                TargetRelativePath = pair.RelativePath,
                Sha256 = HashFile(pair.FullPath),
                CloudSha256 = HashTextFile(pair.FullPath),
            })
            .ToList();
        if (baseline.Count > MaximumMarkerFiles)
        {
            throw new InvalidDataException("The vanilla save inventory is too large for a safe handoff.");
        }

        return new HandoffMarker
        {
            SchemaVersion = SchemaVersion,
            Protocol = Protocol,
            ModId = "TDBank",
            TransactionId = transactionId,
            AccountId = plan.AccountId,
            CreatedUtc = DateTimeOffset.UtcNow.ToString("O"),
            SaveRoot = saveRoot,
            BackupDirectory = backupRoot,
            Profiles = plan.Profiles.Select(profile => new HandoffProfile
            {
                ProfileId = profile.ProfileId,
                UniqueId = profile.UniqueId,
            }).ToList(),
            VanillaBaselineFiles = baseline,
        };
    }

    private static IEnumerable<(string RelativePath, string FullPath)>
        EnumerateVanillaCloudFiles(string accountRoot)
    {
        var selector = Path.Combine(accountRoot, "profile.save");
        if (File.Exists(selector))
        {
            yield return ("profile.save", selector);
        }

        for (var profileId = 1; profileId <= 3; profileId++)
        {
            var saves = Path.Combine(accountRoot, $"profile{profileId}", "saves");
            foreach (var name in DirectSaveNames)
            {
                var path = Path.Combine(saves, name);
                if (File.Exists(path))
                {
                    yield return (
                        $"profile{profileId}/saves/{name}",
                        path);
                }
            }

            var history = Path.Combine(saves, "history");
            if (!Directory.Exists(history))
            {
                continue;
            }
            foreach (var path in Directory.EnumerateFiles(
                         history,
                         "*.run",
                         SearchOption.TopDirectoryOnly))
            {
                yield return (
                    $"profile{profileId}/saves/history/{Path.GetFileName(path)}",
                    path);
            }
        }
    }

    private static void SnapshotAccount(
        AccountPlan plan,
        string backupRoot)
    {
        var destination = CombineUnder(
            backupRoot,
            "snapshot",
            "steam",
            plan.AccountId);
        CopyFileIfPresent(
            Path.Combine(plan.AccountRoot, "profile.save"),
            Path.Combine(destination, "vanilla", "profile.save"));
        CopyFileIfPresent(
            Path.Combine(plan.AccountRoot, "profile.save.backup"),
            Path.Combine(destination, "vanilla", "profile.save.backup"));
        CopyFileIfPresent(
            Path.Combine(plan.AccountRoot, "modded", "profile.save"),
            Path.Combine(destination, "modded", "profile.save"));
        CopyFileIfPresent(
            Path.Combine(plan.AccountRoot, "modded", "profile.save.backup"),
            Path.Combine(destination, "modded", "profile.save.backup"));
        CopyFileIfPresent(
            Path.Combine(plan.AccountRoot, PendingMarkerName),
            CombineUnder(
                backupRoot,
                "previous-protocol",
                plan.AccountId,
                PendingMarkerName));

        for (var profileId = 1; profileId <= 3; profileId++)
        {
            var vanilla = Path.Combine(plan.AccountRoot, $"profile{profileId}");
            if (Directory.Exists(vanilla))
            {
                CopyDirectory(
                    vanilla,
                    Path.Combine(destination, "vanilla", $"profile{profileId}"));
            }

            var modded = Path.Combine(
                plan.AccountRoot,
                "modded",
                $"profile{profileId}");
            if (Directory.Exists(modded))
            {
                CopyDirectory(
                    modded,
                    Path.Combine(destination, "modded", $"profile{profileId}"));
            }
        }
    }

    private static void WritePreparationManifest(
        string backupRoot,
        string saveRoot,
        string transactionId,
        IReadOnlyCollection<AccountPlan> plans,
        IReadOnlyCollection<UninstallSaveHandoffAccount> markers)
    {
        var manifest = new
        {
            schemaVersion = SchemaVersion,
            protocol = Protocol,
            transactionId,
            createdAtUtc = DateTimeOffset.UtcNow,
            saveRoot,
            accountsSnapshotted = plans.Select(plan => plan.AccountId).ToArray(),
            pendingAccounts = markers.Select(marker => new
            {
                marker.AccountId,
                marker.MarkerSha256,
                marker.ProfileIds,
            }),
        };
        WriteJsonAtomically(
            Path.Combine(backupRoot, "uninstall-save-handoff-manifest.json"),
            manifest);
    }

    private static ProgressIdentity ReadProgressIdentity(string path)
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
                || schemaVersion <= 0
                || !root.TryGetProperty("unique_id", out var uniqueId)
                || uniqueId.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(uniqueId.GetString()))
            {
                throw new InvalidDataException("Missing progress identity.");
            }
            return new ProgressIdentity(schemaVersion, uniqueId.GetString()!);
        }
        catch (Exception exception) when (exception is not InvalidDataException)
        {
            throw new InvalidDataException($"Unreadable progress save: {path}", exception);
        }
    }

    internal static bool IsAllowedVanillaCloudTarget(string? relativePath)
    {
        var normalized = NormalizeRelativePath(relativePath);
        if (normalized is null)
        {
            return false;
        }
        if (string.Equals(normalized, "profile.save", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var parts = normalized.Split('/');
        if (parts.Length == 3
            && ProfileRegex().IsMatch(parts[0])
            && parts[1].Equals("saves", StringComparison.OrdinalIgnoreCase)
            && DirectSaveNames.Contains(parts[2]))
        {
            return true;
        }
        return parts.Length == 4
            && ProfileRegex().IsMatch(parts[0])
            && parts[1].Equals("saves", StringComparison.OrdinalIgnoreCase)
            && parts[2].Equals("history", StringComparison.OrdinalIgnoreCase)
            && parts[3].EndsWith(".run", StringComparison.OrdinalIgnoreCase)
            && !parts[3].Contains('/', StringComparison.Ordinal);
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
            || normalized.Split('/').Any(part => part is "" or "." or ".."))
        {
            return null;
        }
        return normalized;
    }

    private static string ResolveUnder(string root, string relativePath)
    {
        return CombineUnder(
            root,
            relativePath
                .Replace('/', Path.DirectorySeparatorChar)
                .Split(Path.DirectorySeparatorChar));
    }

    private static void CopyDirectory(string source, string destination)
    {
        RejectTreeReparsePoints(source, "snapshot source");
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(
                     source,
                     "*",
                     SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(
                Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }
        foreach (var file in Directory.EnumerateFiles(
                     source,
                     "*",
                     SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: false);
            File.SetLastWriteTimeUtc(target, File.GetLastWriteTimeUtc(file));
            if (!FilesEquivalent(file, target))
            {
                throw new IOException($"Snapshot verification failed: {file}");
            }
        }
    }

    private static void CopyFileIfPresent(string source, string destination)
    {
        if (!File.Exists(source))
        {
            return;
        }
        RejectReparsePoint(source, "snapshot file");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite: false);
        File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(source));
        if (!FilesEquivalent(source, destination))
        {
            throw new IOException($"Snapshot verification failed: {source}");
        }
    }

    private static void RejectTreeReparsePoints(string path, string label)
    {
        if (!Directory.Exists(path))
        {
            return;
        }
        RejectReparsePoint(path, label);
        var pending = new Stack<string>();
        pending.Push(path);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                RejectReparsePoint(entry, label);
                if (Directory.Exists(entry))
                {
                    pending.Push(entry);
                }
            }
        }
    }

    private static void RejectReparsePoint(string path, string label)
    {
        if ((File.Exists(path) || Directory.Exists(path))
            && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException($"Refusing unsafe {label} reparse point: {path}");
        }
    }

    private static bool FilesEquivalent(string left, string right)
    {
        return new FileInfo(left).Length == new FileInfo(right).Length
            && CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(File.ReadAllBytes(left)),
                SHA256.HashData(File.ReadAllBytes(right)));
    }

    private static string HashFile(string path)
    {
        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
            .ToLowerInvariant();
    }

    private static string HashTextFile(string path)
    {
        return Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(
                        File.ReadAllText(path, Encoding.UTF8))))
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
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var stream = new FileStream(
                       temporary,
                       FileMode.CreateNew,
                       FileAccess.Write,
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
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static void TryDeleteMarkerForTransaction(
        string markerPath,
        string transactionId)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(markerPath));
            if (string.Equals(
                    document.RootElement
                        .GetProperty("transaction_id")
                        .GetString(),
                    transactionId,
                    StringComparison.Ordinal))
            {
                File.Delete(markerPath);
            }
        }
        catch
        {


        }
    }

    private static string AllocateBackupRoot(
        string saveRoot,
        string transactionId)
    {
        var parent = CombineUnder(saveRoot, BackupFolderName);
        var candidate = CombineUnder(
            parent,
            $"{transactionId}-uninstall-handoff");
        for (var suffix = 1;
             Directory.Exists(candidate) || File.Exists(candidate);
             suffix++)
        {
            candidate = CombineUnder(
                parent,
                $"{transactionId}-uninstall-handoff-{suffix}");
        }
        return candidate;
    }

    private static string CombineUnder(string root, params string[] parts)
    {
        var fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var combined = Path.GetFullPath(Path.Combine([root, .. parts]));
        if (!combined.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(
                combined.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar),
                fullRoot.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException($"Save handoff path escaped its root: {combined}");
        }
        return combined;
    }

    private static void ValidateTransactionId(string transactionId)
    {
        if (!TransactionIdRegex().IsMatch(transactionId)
            || transactionId.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Unsafe save-handoff transaction ID.",
                nameof(transactionId));
        }
    }

    [GeneratedRegex(@"^\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex SteamAccountRegex();

    [GeneratedRegex(@"^profile([1-3])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProfileRegex();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex TransactionIdRegex();

    private sealed record ProgressIdentity(int SchemaVersion, string UniqueId);

    private sealed record ProfilePlan(
        int ProfileId,
        string UniqueId,
        string SourceRoot,
        string TargetRoot);

    private sealed record AccountPlan(
        string SaveRoot,
        string AccountId,
        string AccountRoot,
        IReadOnlyList<ProfilePlan> Profiles);

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
}

internal enum UninstallSaveHandoffState
{
    PendingGameHandoff,
    ReadyToRemoveMods,
    Failed,
}

internal sealed record UninstallSaveHandoffAccount(
    string AccountId,
    string AccountRoot,
    string MarkerPath,
    string ReceiptPath,
    string MarkerSha256,
    IReadOnlyList<int> ProfileIds);

internal sealed record UninstallSaveHandoffPreparation(
    string SaveRoot,
    string BackupDirectory,
    string TransactionId,
    IReadOnlyList<UninstallSaveHandoffAccount> Accounts)
{
    public bool RequiresGameHandoff => Accounts.Count > 0;
}

internal sealed record UninstallSaveHandoffAccountInspection(
    string AccountId,
    UninstallSaveHandoffState State,
    string Detail);

internal sealed record UninstallSaveHandoffInspection(
    UninstallSaveHandoffState State,
    UninstallSaveHandoffPreparation Preparation,
    IReadOnlyList<UninstallSaveHandoffAccountInspection> Accounts)
{
    public bool MayRemoveMods =>
   State == UninstallSaveHandoffState.ReadyToRemoveMods;
}
