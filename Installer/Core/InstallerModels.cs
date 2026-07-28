namespace CNJ.TowerDebt.Setup.Core;

internal enum ValidationStatus
{
    NoDirectory,
    InvalidPath,
    MissingGameFiles,
    Supported,
    UnsupportedVersion,
    ReleaseInfoUnreadable,
}

internal sealed record GameValidation(
    bool IsGameDirectory,
    bool IsSupportedVersion,
    string? Version,
    string? Branch,
    string? Commit,
    ValidationStatus Status,
    string? Detail = null);

internal enum InstallStage
{
    Inventory,
    DeployTDLib,
    VerifyStaging,
    InstallTDBank,
    ProtectSaves,
    FinalAudit,
}

internal enum UninstallStage
{
    Inventory,
    MoveTDBank,
    FinalAudit,
}

internal enum InstallerErrorCode
{
    ValidationRejected,
    GameRunning,
    RollbackCompleted,
    RollbackFailed,
    UninstallRollbackCompleted,
    UninstallRollbackFailed,
    UnrecognizedTDBankDirectory,
    HashMismatch,
    DirectoryNotWritable,
    FileLocked,
    ReparsePoint,
    InsufficientDiskSpace,
    PathOutsideAllowedRoot,
    DeleteOutsideAllowedRoot,
    MissingEmbeddedResource,
    SaveProtectionFailed,
    SaveRollbackFailed,
    UninstallSaveHandoffLaunchFailed,
    UninstallSaveHandoffFailed,
    UninstallSaveHandoffTimedOut,
}

internal sealed class InstallerOperationException : Exception
{
    public InstallerOperationException(
        InstallerErrorCode code,
        Exception? innerException = null,
        string? targetPath = null,
        string? backupDirectory = null,
        string? detail = null,
        GameValidation? validation = null)
        : base(code.ToString(), innerException)
    {
        Code = code;
        TargetPath = targetPath;
        BackupDirectory = backupDirectory;
        Detail = detail;
        Validation = validation;
    }

    public InstallerErrorCode Code { get; }

    public string? TargetPath { get; }

    public string? BackupDirectory { get; }

    public string? Detail { get; }

    public GameValidation? Validation { get; }
}

internal enum TDLibInstallAction
{
    Install,
    UpgradeOrRepair,
    PreserveExact,
    PreserveNewer,
}

internal enum SaveProfileDisposition
{
    Migrated,
    PreservedEstablished,
    AlreadyEquivalent,
    NoUsableVanilla,
    SkippedUnsafe,
}

internal sealed record SaveProfileResult(
    string SteamAccountId,
    string ProfileName,
    SaveProfileDisposition Disposition,
    string VanillaPath,
    string ModdedPath,
    string? Detail = null);

internal sealed record SaveProtectionResult(
    string SaveRoot,
    string BackupDirectory,
    int AccountsScanned,
    IReadOnlyList<SaveProfileResult> Profiles)
{
    public int ProfilesFound => Profiles.Count;

    public int ProfilesMigrated =>
        Profiles.Count(profile => profile.Disposition == SaveProfileDisposition.Migrated);

    public int ProfilesPreserved =>
        Profiles.Count(profile => profile.Disposition == SaveProfileDisposition.PreservedEstablished);

    public int ProfilesAlreadyEquivalent =>
        Profiles.Count(profile => profile.Disposition == SaveProfileDisposition.AlreadyEquivalent);

    public int ProfilesWithoutVanilla =>
        Profiles.Count(profile => profile.Disposition == SaveProfileDisposition.NoUsableVanilla);

    public int ProfilesSkippedUnsafe =>
        Profiles.Count(profile => profile.Disposition == SaveProfileDisposition.SkippedUnsafe);

    public static SaveProtectionResult Empty(string saveRoot) =>
        new(saveRoot, string.Empty, 0, []);
}

internal sealed record InstallResult(
    string GameDirectory,
    string ModsDirectory,
    string BackupDirectory,
    TDLibInstallAction TDLibAction,
    int InstalledFileCount,
    SaveProtectionResult SaveProtection);

internal enum UninstallDisposition
{
    RemovedToBackup,
    AlreadyAbsent,
}

internal enum TDLibUninstallDisposition
{
    RemovedManagedToBackup,
    PreservedUnmanaged,
    AlreadyAbsent,
}

internal sealed record UninstallResult(
    string GameDirectory,
    string ModsDirectory,
    string BackupDirectory,
    UninstallDisposition Disposition,
    int RemovedFileCount,
    TDLibUninstallDisposition TDLibDisposition,
    int RemovedTDLibFileCount)
{
    public bool Removed => Disposition == UninstallDisposition.RemovedToBackup;

    public bool RemovedTDLib =>
        TDLibDisposition == TDLibUninstallDisposition.RemovedManagedToBackup;
}

internal sealed record PayloadFile(
    string ResourceName,
    string RelativePath,
    bool IsTDLib);
