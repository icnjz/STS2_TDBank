using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace CNJ.TowerDebt.Setup.Core;

internal static class TransactionInstaller
{
    private static readonly Version RequiredTDLibVersion =
        EmbeddedPayload.RequiredTDLibVersion;

    internal static Func<bool>? GameRunningTestHook { get; set; }

    public static InstallResult Install(string gameDirectory, IProgress<InstallStage>? progress = null)
    {
        return Install(gameDirectory, SaveProtection.DefaultSaveRoot, progress);
    }

    internal static InstallResult Install(
        string gameDirectory,
        string saveRoot,
        IProgress<InstallStage>? progress = null)
    {
        var validation = GameValidator.Validate(gameDirectory);
        if (!validation.IsGameDirectory || !validation.IsSupportedVersion)
        {
            throw new InstallerOperationException(
                InstallerErrorCode.ValidationRejected,
                validation: validation);
        }

        if (IsGameRunning())
        {
            throw new InstallerOperationException(InstallerErrorCode.GameRunning);
        }

        var gameRoot = Path.GetFullPath(gameDirectory);
        var modsRoot = Path.Combine(gameRoot, "mods");
        Directory.CreateDirectory(modsRoot);
        EnsureWritable(modsRoot);

        var tdTarget = CombineUnder(modsRoot, "TDBank");
        var tdLibTarget = CombineUnder(modsRoot, "TDLib");
        RejectReparsePoint(tdTarget);
        RejectReparsePoint(tdLibTarget);
        RejectLockedDll(Path.Combine(tdTarget, "TDBank.dll"));
        RejectLockedDll(Path.Combine(tdLibTarget, "TDLib.dll"));

        var tdLibAction = EvaluateTDLib(tdLibTarget);
        var tdLibManagedBySetup =
            tdLibAction is TDLibInstallAction.Install
                or TDLibInstallAction.UpgradeOrRepair
            || (tdLibAction == TDLibInstallAction.PreserveExact
                && TDLibOwnership.CanRemoveManagedPayload(
                    tdTarget,
                    tdLibTarget));
        var transactionId = $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";




        var stageParent = CombineUnder(gameRoot, ".cnj-tower-debt-staging");
        var backupParent = CombineUnder(gameRoot, ".cnj-tower-debt-mod-backups");
        RejectReparsePoint(stageParent);
        RejectReparsePoint(backupParent);
        var stageRoot = CombineUnder(stageParent, transactionId);
        var backupRoot = CombineUnder(backupParent, transactionId);
        var tdStage = CombineUnder(stageRoot, "TDBank");
        var tdLibStage = CombineUnder(stageRoot, "TDLib");
        var tdBackup = CombineUnder(backupRoot, "TDBank");
        var tdLibBackup = CombineUnder(backupRoot, "TDLib");

        var tdBackedUp = false;
        var tdInstalled = false;
        var tdLibBackedUp = false;
        var tdLibInstalled = false;
        var legacyRelocations = new List<DirectoryRelocation>();
        var saveProtection = SaveProtectionResult.Empty(Path.GetFullPath(saveRoot));

        InstallerLog.TryWrite(
            $"Starting install. Game={gameRoot}; TDLibAction={tdLibAction}");

        try
        {
            CheckFreeSpace(modsRoot);
            progress?.Report(InstallStage.Inventory);
            Directory.CreateDirectory(stageRoot);
            WritePayload(tdStage, isTDLib: false);

            if (tdLibAction is TDLibInstallAction.Install
                or TDLibInstallAction.UpgradeOrRepair)
            {
                progress?.Report(InstallStage.DeployTDLib);
                WritePayload(tdLibStage, isTDLib: true);
            }

            progress?.Report(InstallStage.VerifyStaging);
            VerifyPayload(tdStage, isTDLib: false);
            if (Directory.Exists(tdLibStage))
            {
                VerifyPayload(tdLibStage, isTDLib: true);
            }

            Directory.CreateDirectory(backupRoot);
            RelocateLegacyModArtifacts(modsRoot, backupRoot, legacyRelocations);

            if (tdLibAction is TDLibInstallAction.Install
                or TDLibInstallAction.UpgradeOrRepair)
            {
                if (Directory.Exists(tdLibTarget))
                {
                    Directory.Move(tdLibTarget, tdLibBackup);
                    tdLibBackedUp = true;
                }

                Directory.Move(tdLibStage, tdLibTarget);
                tdLibInstalled = true;
            }

            progress?.Report(InstallStage.InstallTDBank);
            if (Directory.Exists(tdTarget))
            {
                Directory.Move(tdTarget, tdBackup);
                tdBackedUp = true;
            }

            Directory.Move(tdStage, tdTarget);
            tdInstalled = true;

            VerifyPayload(tdTarget, isTDLib: false);
            if (tdLibInstalled)
            {
                VerifyPayload(tdLibTarget, isTDLib: true);
            }

            progress?.Report(InstallStage.FinalAudit);
            var retainedModBackup =
                tdBackedUp || tdLibBackedUp || legacyRelocations.Count > 0;
            var modBackupDirectory = retainedModBackup ? backupRoot : string.Empty;
            WriteInstallState(
                tdTarget,
                validation,
                tdLibAction,
                tdLibManagedBySetup,
                modBackupDirectory,
                saveProtection: null);
            SafeDeleteDirectory(stageRoot, stageParent);
            if (!retainedModBackup)
            {
                SafeDeleteDirectory(backupRoot, backupParent);
            }

            var installedCount = EmbeddedPayload.Files.Count(file => !file.IsTDLib)
                + (tdLibInstalled
                    ? EmbeddedPayload.Files.Count(file => file.IsTDLib)
                    : 0);





            progress?.Report(InstallStage.ProtectSaves);
            saveProtection = SaveProtection.ProtectAndInitialize(saveRoot, transactionId);

            TryWithoutThrow(
                () => WriteInstallState(
                    tdTarget,
                    validation,
                    tdLibAction,
                    tdLibManagedBySetup,
                    modBackupDirectory,
                    saveProtection));
            InstallerLog.TryWrite(
                $"Install succeeded. Files={installedCount}; Backup={modBackupDirectory}; " +
                $"SaveBackup={saveProtection.BackupDirectory}");
            return new InstallResult(
                gameRoot,
                modsRoot,
                modBackupDirectory,
                tdLibAction,
                installedCount,
                saveProtection);
        }
        catch (Exception exception)
        {
            var rollbackErrors = new List<string>();

            TryRollback(
                () =>
                {
                    if (tdInstalled)
                    {
                        SafeDeleteDirectory(tdTarget, modsRoot);
                    }
                    if (tdBackedUp && Directory.Exists(tdBackup))
                    {
                        Directory.Move(tdBackup, tdTarget);
                    }
                },
                "TD Bank",
                rollbackErrors);

            TryRollback(
                () =>
                {
                    if (tdLibInstalled)
                    {
                        SafeDeleteDirectory(tdLibTarget, modsRoot);
                    }
                    if (tdLibBackedUp && Directory.Exists(tdLibBackup))
                    {
                        Directory.Move(tdLibBackup, tdLibTarget);
                    }
                },
                "TDLib",
                rollbackErrors);

            TryRollback(
                () => RestoreLegacyModArtifacts(legacyRelocations),
                "legacy mod artifacts",
                rollbackErrors);
            TryRollback(
                () => SafeDeleteDirectory(stageRoot, stageParent),
                "staging",
                rollbackErrors);

            if (rollbackErrors.Count == 0)
            {
                TryRollback(
                    () => SafeDeleteDirectory(backupRoot, backupParent),
                    "empty backup workspace",
                    rollbackErrors);
            }

            if (rollbackErrors.Count > 0)
            {
                var joined = string.Join(Environment.NewLine, rollbackErrors);
                InstallerLog.TryWrite(
                    $"Install failed: {exception}. Rollback errors: {joined}");
                throw new InstallerOperationException(
                    InstallerErrorCode.RollbackFailed,
                    exception,
                    backupDirectory: backupRoot,
                    detail: joined);
            }

            InstallerLog.TryWrite(
                $"Install failed, but rollback completed: {exception}");
            throw new InstallerOperationException(
                InstallerErrorCode.RollbackCompleted,
                exception);
        }
    }

    private static void RelocateLegacyModArtifacts(
        string modsRoot,
        string backupRoot,
        ICollection<DirectoryRelocation> relocations)
    {
        var candidates = new List<string>();
        var legacyBackups = CombineUnder(modsRoot, ".cnj-tdbank-backups");
        if (Directory.Exists(legacyBackups))
        {
            candidates.Add(legacyBackups);
        }

        candidates.AddRange(
            Directory.EnumerateDirectories(
                modsRoot,
                ".cnj-tdbank-stage-*",
                SearchOption.TopDirectoryOnly));

        foreach (var source in candidates
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            RejectReparsePoint(source);
            var destination = CombineUnder(
                backupRoot,
                "legacy-mods-artifacts",
                Path.GetFileName(source));
            if (Directory.Exists(destination) || File.Exists(destination))
            {
                throw new IOException(
                    $"Legacy mod artifact destination already exists: {destination}");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            Directory.Move(source, destination);
            relocations.Add(new DirectoryRelocation(source, destination));
            InstallerLog.TryWrite(
                $"Moved legacy installer data out of the mod scan tree. " +
                $"Source={source}; Destination={destination}");
        }
    }

    private static void RestoreLegacyModArtifacts(
        IReadOnlyList<DirectoryRelocation> relocations)
    {
        for (var index = relocations.Count - 1; index >= 0; index--)
        {
            var relocation = relocations[index];
            if (!Directory.Exists(relocation.Destination))
            {
                continue;
            }
            if (Directory.Exists(relocation.Source) || File.Exists(relocation.Source))
            {
                throw new IOException(
                    $"Cannot restore legacy installer data because its original path exists: " +
                    relocation.Source);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(relocation.Source)!);
            Directory.Move(relocation.Destination, relocation.Source);
        }
    }

    private static void WriteInstallState(
        string tdTarget,
        GameValidation validation,
        TDLibInstallAction tdLibAction,
        bool tdLibManagedBySetup,
        string modBackupDirectory,
        SaveProtectionResult? saveProtection)
    {
        object saveState = saveProtection is null
            ? new
            {
                status = "pending",
            }
            : new
            {
                status = "completed",
                saveProtection.SaveRoot,
                saveProtection.BackupDirectory,
                saveProtection.AccountsScanned,
                saveProtection.ProfilesFound,
                saveProtection.ProfilesMigrated,
                saveProtection.ProfilesPreserved,
                saveProtection.ProfilesAlreadyEquivalent,
                saveProtection.ProfilesWithoutVanilla,
                saveProtection.ProfilesSkippedUnsafe,
            };
        var state = new
        {
            installer = "CNJ Tower Debt Setup",
            installerVersion = "v0.1.3",
            packageVersion = "v0.1.3",
            installedAt = DateTimeOffset.Now,
            gameVersion = validation.Version,
            tdLibAction = tdLibAction.ToString(),
            tdLibOwnership = new
            {
                schemaVersion = TDLibOwnership.SchemaVersion,
                managedBySetup = tdLibManagedBySetup,
                actionAtThisInstall = tdLibAction.ToString(),
                payloadVersion = RequiredTDLibVersion.ToString(),
                payloadFiles = TDLibOwnership.CreatePayloadProof().Select(file => new
                {
                    relativePath = file.RelativePath,
                    sha256 = file.Sha256,
                }),
            },
            backupDirectory = modBackupDirectory,
            saveProtection = saveState,
            files = EmbeddedPayload.Files.Select(file => new
            {
                file.RelativePath,
                sha256 = EmbeddedPayload.Hash(EmbeddedPayload.Read(file)),
            }),
        };

        var statePath = Path.Combine(tdTarget, "install-state.json");
        var temporaryPath = Path.Combine(
            tdTarget,
            $".install-state-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(
                    state,
                    new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, statePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void TryWithoutThrow(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            InstallerLog.TryWrite(
                $"Non-fatal post-save-protection update failed: {exception}");
        }
    }

    private static bool IsGameRunning()
    {
        if (GameRunningTestHook is not null)
        {
            return GameRunningTestHook();
        }

        var processes = Process.GetProcessesByName("SlayTheSpire2");
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    internal static TDLibInstallAction EvaluateTDLib(string tdLibDirectory)
    {
        if (!Directory.Exists(tdLibDirectory))
        {
            return TDLibInstallAction.Install;
        }

        var manifest = Path.Combine(tdLibDirectory, "TDLib.json");
        var version = ReadManifestVersion(manifest);
        if (version is not null && version > RequiredTDLibVersion
            && CoreTDLibFilesExist(tdLibDirectory))
        {
            return TDLibInstallAction.PreserveNewer;
        }

        if (version == RequiredTDLibVersion)
        {
            var exact = EmbeddedPayload.Files
                .Where(file => file.IsTDLib)
                .All(file => EmbeddedPayload.Matches(
                    file,
                    Path.Combine(
                        tdLibDirectory,
                        Path.GetRelativePath("TDLib", file.RelativePath))));
            if (exact)
            {
                return TDLibInstallAction.PreserveExact;
            }
        }

        return TDLibInstallAction.UpgradeOrRepair;
    }

    private static void WritePayload(string destination, bool isTDLib)
    {
        foreach (var file in EmbeddedPayload.Files.Where(
                     file => file.IsTDLib == isTDLib))
        {
            var relativeWithinMod = Path.GetRelativePath(
                isTDLib ? "TDLib" : "TDBank",
                file.RelativePath);
            var target = CombineUnder(destination, relativeWithinMod);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, EmbeddedPayload.Read(file));
        }
    }

    private static void VerifyPayload(string destination, bool isTDLib)
    {
        foreach (var file in EmbeddedPayload.Files.Where(
                     file => file.IsTDLib == isTDLib))
        {
            var relativeWithinMod = Path.GetRelativePath(
                isTDLib ? "TDLib" : "TDBank",
                file.RelativePath);
            var target = CombineUnder(destination, relativeWithinMod);
            if (!EmbeddedPayload.Matches(file, target))
            {
                throw new InstallerOperationException(
                    InstallerErrorCode.HashMismatch,
                    targetPath: target);
            }
        }
    }

    private static Version? ReadManifestVersion(string manifest)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifest));
            var raw = document.RootElement.GetProperty("version").GetString()?.Trim().TrimStart('v', 'V');
            return Version.TryParse(raw, out var version) ? version : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool CoreTDLibFilesExist(string directory)
    {
        return File.Exists(Path.Combine(directory, "TDLib.dll"))
            && File.Exists(Path.Combine(directory, "TDLib.json"));
    }

    private static void EnsureWritable(string directory)
    {
        var probe = CombineUnder(directory, $".cnj-write-test-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllText(probe, "Tower Debt");
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new InstallerOperationException(
                InstallerErrorCode.DirectoryNotWritable,
                exception,
                targetPath: directory);
        }
        finally
        {
            if (File.Exists(probe))
            {
                File.Delete(probe);
            }
        }
    }

    private static void RejectLockedDll(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException exception)
        {
            throw new InstallerOperationException(
                InstallerErrorCode.FileLocked,
                exception,
                targetPath: path);
        }
    }

    private static void RejectReparsePoint(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InstallerOperationException(
                InstallerErrorCode.ReparsePoint,
                targetPath: path);
        }
    }

    private static void CheckFreeSpace(string path)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(path));
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        var drive = new DriveInfo(root);
        const long requiredBytes = 32L * 1024 * 1024;
        if (drive.AvailableFreeSpace < requiredBytes)
        {
            throw new InstallerOperationException(InstallerErrorCode.InsufficientDiskSpace);
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
                fullRoot.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InstallerOperationException(
                InstallerErrorCode.PathOutsideAllowedRoot,
                targetPath: combined);
        }
        return combined;
    }

    private static void SafeDeleteDirectory(string path, string allowedRoot)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        var fullRoot = Path.GetFullPath(allowedRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InstallerOperationException(
                InstallerErrorCode.DeleteOutsideAllowedRoot,
                targetPath: fullPath);
        }

        Directory.Delete(fullPath, recursive: true);
    }

    private static void TryRollback(Action action, string label, ICollection<string> errors)
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

    private sealed record DirectoryRelocation(
        string Source,
        string Destination);
}
