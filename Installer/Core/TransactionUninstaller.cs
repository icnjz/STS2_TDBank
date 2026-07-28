using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace CNJ.TowerDebt.Setup.Core;

internal static class TransactionUninstaller
{
    internal static Action<string, string>? AfterMoveTestHook { get; set; }
    internal static Func<bool>? GameRunningTestHook { get; set; }

    public static bool IsInstalled(string? gameDirectory)
    {
        try
        {
            var validation = GameValidator.Validate(gameDirectory);
            if (!CanUseForUninstall(validation) || string.IsNullOrWhiteSpace(gameDirectory))
            {
                return false;
            }

            var gameRoot = Path.GetFullPath(gameDirectory.Trim().Trim('"'));
            var modsRoot = CombineUnder(gameRoot, "mods");
            var tdTarget = CombineUnder(modsRoot, "TDBank");
            if (!Directory.Exists(tdTarget))
            {
                return false;
            }

            RejectReparsePoint(gameRoot);
            RejectReparsePoint(modsRoot);
            _ = InventoryTree(tdTarget);
            return IsRecognizedTDBankDirectory(tdTarget);
        }
        catch
        {
            return false;
        }
    }

    public static UninstallResult Uninstall(
        string gameDirectory,
        IProgress<UninstallStage>? progress = null)
    {
        var validation = GameValidator.Validate(gameDirectory);
        if (!CanUseForUninstall(validation))
        {
            throw new InstallerOperationException(
                InstallerErrorCode.ValidationRejected,
                validation: validation);
        }

        if (IsGameRunning())
        {
            throw new InstallerOperationException(InstallerErrorCode.GameRunning);
        }

        var gameRoot = Path.GetFullPath(gameDirectory.Trim().Trim('"'));
        var modsRoot = CombineUnder(gameRoot, "mods");
        var tdTarget = CombineUnder(modsRoot, "TDBank");
        var tdLibTarget = CombineUnder(modsRoot, "TDLib");
        if (!Directory.Exists(tdTarget))
        {
            return new UninstallResult(
                gameRoot,
                modsRoot,
                string.Empty,
                UninstallDisposition.AlreadyAbsent,
                0,
                Directory.Exists(tdLibTarget)
                    ? TDLibUninstallDisposition.PreservedUnmanaged
                    : TDLibUninstallDisposition.AlreadyAbsent,
                0);
        }

        progress?.Report(UninstallStage.Inventory);
        RejectReparsePoint(gameRoot);
        RejectReparsePoint(modsRoot);
        var originalInventory = InventoryTree(tdTarget);
        var removedFileCount = originalInventory.FileCount;
        if (!IsRecognizedTDBankDirectory(tdTarget))
        {
            throw new InstallerOperationException(
                InstallerErrorCode.UnrecognizedTDBankDirectory,
                targetPath: tdTarget);
        }

        var removeManagedTDLib =
            TDLibOwnership.CanRemoveManagedPayload(tdTarget, tdLibTarget);
        TreeInventory? originalTDLibInventory = null;
        if (removeManagedTDLib)
        {
            originalTDLibInventory = InventoryTree(tdLibTarget);
        }
        var tdLibDisposition = !Directory.Exists(tdLibTarget)
            ? TDLibUninstallDisposition.AlreadyAbsent
            : removeManagedTDLib
                ? TDLibUninstallDisposition.RemovedManagedToBackup
                : TDLibUninstallDisposition.PreservedUnmanaged;

        EnsureWritable(modsRoot);
        RejectLockedDll(Path.Combine(tdTarget, "TDBank.dll"));
        if (removeManagedTDLib)
        {
            RejectLockedDll(Path.Combine(tdLibTarget, "TDLib.dll"));
        }
        if (IsGameRunning())
        {
            throw new InstallerOperationException(InstallerErrorCode.GameRunning);
        }

        var transactionId = $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        var backupParent = CombineUnder(
            gameRoot,
            ".cnj-tower-debt-uninstall-backups");
        RejectReparsePoint(backupParent);
        Directory.CreateDirectory(backupParent);
        RejectReparsePoint(backupParent);
        var backupRoot = CombineUnder(backupParent, transactionId);
        var tdBackup = CombineUnder(backupRoot, "TDBank-Uninstalled");
        var tdLibBackup = CombineUnder(backupRoot, "TDLib-Uninstalled");
        Directory.CreateDirectory(backupRoot);
        RejectReparsePoint(backupRoot);

        var tdMoved = false;
        var tdLibMoved = false;
        InstallerLog.TryWrite(
            $"Starting uninstall. Game={gameRoot}; Target={tdTarget}; " +
            $"RemoveManagedTDLib={removeManagedTDLib}; " +
            "save paths are outside the uninstall transaction.");

        try
        {
            RejectReparsePoint(gameRoot);
            RejectReparsePoint(modsRoot);
            RejectReparsePoint(backupParent);
            RejectReparsePoint(backupRoot);
            var preMoveInventory = InventoryTree(tdTarget);
            EnsureInventoryMatches(
                originalInventory,
                preMoveInventory,
                "TD Bank changed while Setup was preparing the uninstall.");
            if (!IsRecognizedTDBankDirectory(tdTarget))
            {
                throw new InstallerOperationException(
                    InstallerErrorCode.UnrecognizedTDBankDirectory,
                    targetPath: tdTarget);
            }
            if (removeManagedTDLib)
            {
                if (!TDLibOwnership.CanRemoveManagedPayload(
                        tdTarget,
                        tdLibTarget))
                {
                    throw new IOException(
                        "TDLib ownership or payload changed while Setup was preparing uninstall.");
                }
                EnsureInventoryMatches(
                    originalTDLibInventory!,
                    InventoryTree(tdLibTarget),
                    "TDLib changed while Setup was preparing uninstall.");
            }

            progress?.Report(UninstallStage.MoveTDBank);
            Directory.Move(tdTarget, tdBackup);
            tdMoved = true;

            if (removeManagedTDLib)
            {
                Directory.Move(tdLibTarget, tdLibBackup);
                tdLibMoved = true;
            }

            AfterMoveTestHook?.Invoke(tdTarget, tdBackup);

            progress?.Report(UninstallStage.FinalAudit);
            if (Directory.Exists(tdTarget) || !Directory.Exists(tdBackup))
            {
                throw new IOException(
                    "TD Bank did not move completely into the uninstall backup.");
            }

            var backupInventory = InventoryTree(tdBackup);
            EnsureInventoryMatches(
                originalInventory,
                backupInventory,
                "The TD Bank recovery backup failed its content audit.");
            if (removeManagedTDLib)
            {
                if (Directory.Exists(tdLibTarget)
                    || !Directory.Exists(tdLibBackup))
                {
                    throw new IOException(
                        "Managed TDLib did not move completely into the uninstall backup.");
                }
                EnsureInventoryMatches(
                    originalTDLibInventory!,
                    InventoryTree(tdLibBackup),
                    "The TDLib recovery backup failed its content audit.");
            }
            InstallerLog.TryWrite(
                $"Uninstall succeeded. Files={removedFileCount}; " +
                $"TDLibFiles={originalTDLibInventory?.FileCount ?? 0}; " +
                $"RecoveryBackup={backupRoot}; save paths were not accessed.");
            return new UninstallResult(
                gameRoot,
                modsRoot,
                backupRoot,
                UninstallDisposition.RemovedToBackup,
                removedFileCount,
                tdLibDisposition,
                originalTDLibInventory?.FileCount ?? 0);
        }
        catch (Exception exception)
        {
            var rollbackErrors = new List<string>();
            var tdLibAppearsMoved = tdLibMoved
                || (removeManagedTDLib
                    && !Directory.Exists(tdLibTarget)
                    && Directory.Exists(tdLibBackup));
            if (tdLibAppearsMoved)
            {
                TryRollback(
                    () =>
                    {
                        if (Directory.Exists(tdLibTarget)
                            || File.Exists(tdLibTarget))
                        {
                            throw new IOException(
                                $"Cannot restore TDLib because the original path exists: {tdLibTarget}");
                        }
                        if (!Directory.Exists(tdLibBackup))
                        {
                            throw new DirectoryNotFoundException(
                                $"The TDLib recovery backup is missing: {tdLibBackup}");
                        }

                        EnsureInventoryMatches(
                            originalTDLibInventory!,
                            InventoryTree(tdLibBackup),
                            "The TDLib recovery backup changed before rollback.");
                        Directory.Move(tdLibBackup, tdLibTarget);
                        EnsureInventoryMatches(
                            originalTDLibInventory!,
                            InventoryTree(tdLibTarget),
                            "TDLib did not restore exactly.");
                    },
                    "TDLib",
                    rollbackErrors);
            }
            else if (removeManagedTDLib)
            {
                TryRollback(
                    () => EnsureInventoryMatches(
                        originalTDLibInventory!,
                        InventoryTree(tdLibTarget),
                        "TDLib changed before Setup could move it."),
                    "unchanged TDLib",
                    rollbackErrors);
            }

            var tdAppearsMoved = tdMoved
                || (!Directory.Exists(tdTarget) && Directory.Exists(tdBackup));
            if (tdAppearsMoved)
            {
                TryRollback(
                    () =>
                    {
                        if (Directory.Exists(tdTarget) || File.Exists(tdTarget))
                        {
                            throw new IOException(
                                $"Cannot restore TD Bank because the original path exists: {tdTarget}");
                        }
                        if (!Directory.Exists(tdBackup))
                        {
                            throw new DirectoryNotFoundException(
                                $"The TD Bank recovery backup is missing: {tdBackup}");
                        }

                        EnsureInventoryMatches(
                            originalInventory,
                            InventoryTree(tdBackup),
                            "The recovery backup changed before rollback.");
                        Directory.Move(tdBackup, tdTarget);
                        EnsureInventoryMatches(
                            originalInventory,
                            InventoryTree(tdTarget),
                            "TD Bank did not restore exactly.");
                    },
                    "TD Bank",
                    rollbackErrors);
            }
            else
            {
                TryRollback(
                    () => EnsureInventoryMatches(
                        originalInventory,
                        InventoryTree(tdTarget),
                        "TD Bank changed before Setup could move it."),
                    "unchanged TD Bank",
                    rollbackErrors);
            }

            if (rollbackErrors.Count > 0)
            {
                var detail = string.Join(Environment.NewLine, rollbackErrors);
                InstallerLog.TryWrite(
                    $"Uninstall failed: {exception}. Rollback errors: {detail}");
                throw new InstallerOperationException(
                    InstallerErrorCode.UninstallRollbackFailed,
                    exception,
                    targetPath: tdTarget,
                    backupDirectory: backupRoot,
                    detail: detail);
            }

            try
            {
                if (Directory.Exists(backupRoot)
                    && !Directory.EnumerateFileSystemEntries(backupRoot).Any())
                {
                    Directory.Delete(backupRoot);
                }
            }
            catch (Exception cleanupException)
            {
                InstallerLog.TryWrite(
                    $"Non-fatal uninstall workspace cleanup failed: {cleanupException}");
            }

            InstallerLog.TryWrite(
                $"Uninstall failed, but rollback completed: {exception}");
            throw new InstallerOperationException(
                InstallerErrorCode.UninstallRollbackCompleted,
                exception,
                targetPath: tdTarget,
                backupDirectory: backupRoot);
        }
    }

    private static bool CanUseForUninstall(GameValidation validation)
    {
        return validation.IsGameDirectory
            || validation.Status == ValidationStatus.ReleaseInfoUnreadable;
    }

    private static bool IsRecognizedTDBankDirectory(string directory)
    {
        if (HasManifestIdentity(Path.Combine(directory, "TDBank.json")))
        {
            return true;
        }

        try
        {
            var statePath = Path.Combine(directory, "install-state.json");
            using var state = JsonDocument.Parse(File.ReadAllText(statePath));
            return string.Equals(
                state.RootElement.GetProperty("installer").GetString(),
                "CNJ Tower Debt Setup",
                StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static bool HasManifestIdentity(string manifestPath)
    {
        try
        {
            using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
            return string.Equals(
                manifest.RootElement.GetProperty("id").GetString(),
                "TDBank",
                StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static TreeInventory InventoryTree(string root)
    {
        RejectReparsePoint(root);
        var entries = new SortedDictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            RejectReparsePoint(directory);
            var relativeDirectory = Path.GetRelativePath(root, directory);
            var directoryInfo = new DirectoryInfo(directory);
            directoryInfo.Refresh();
            entries.Add(
                $"D|{relativeDirectory}",
                $"{(int)directoryInfo.Attributes}");
            foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
            {
                entry.Refresh();
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InstallerOperationException(
                        InstallerErrorCode.ReparsePoint,
                        targetPath: entry.FullName);
                }

                if ((entry.Attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(entry.FullName);
                }
                else
                {
                    var relativeFile = Path.GetRelativePath(root, entry.FullName);
                    try
                    {
                        using var stream = new FileStream(
                            entry.FullName,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.Read);
                        var hash = Convert.ToHexString(SHA256.HashData(stream));
                        entries.Add(
                            $"F|{relativeFile}",
                            $"{stream.Length}|{(int)entry.Attributes}|{hash}");
                    }
                    catch (IOException exception)
                    {
                        throw new InstallerOperationException(
                            InstallerErrorCode.FileLocked,
                            exception,
                            targetPath: entry.FullName);
                    }
                }
            }
        }

        return new TreeInventory(
            entries,
            entries.Keys.Count(key => key.StartsWith("F|", StringComparison.Ordinal)));
    }

    private static void EnsureInventoryMatches(
        TreeInventory expected,
        TreeInventory actual,
        string message)
    {
        if (expected.Entries.Count != actual.Entries.Count
            || !expected.Entries.SequenceEqual(actual.Entries))
        {
            throw new IOException(message);
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

    private static void EnsureWritable(string directory)
    {
        var probe = CombineUnder(
            directory,
            $".cnj-uninstall-write-test-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllText(probe, "Tower Debt account closure");
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
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None);
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
            throw new InstallerOperationException(
                InstallerErrorCode.PathOutsideAllowedRoot,
                targetPath: combined);
        }

        return combined;
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

    private sealed record TreeInventory(
        IReadOnlyDictionary<string, string> Entries,
        int FileCount);
}
