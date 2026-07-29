using System.Globalization;
using CNJ.TowerDebt.Setup.Core;

namespace CNJ.TowerDebt.Setup;

internal enum UiLanguage
{
    ZhCn,
    En,
}

internal enum UiText
{
    WindowTitle,
    HeaderTitle,
    HeaderTagline,
    Disclaimer,
    PathHeading,
    Browse,
    PayloadSummary,
    PayloadSummaryInstalled,
    Consent,
    StatusPrivacy,
    LicenseLink,
    Cancel,
    Install,
    Reinstall,
    Installing,
    Installed,
    Uninstall,
    Uninstalling,
    InstalledDetected,
    UninstallConfirmTitle,
    UninstallConfirmBody,
    StatusUninstallSuccess,
    StatusUninstallAlreadyAbsent,
    StatusUninstallFailure,
    UninstallSuccessDialogTitle,
    UninstallSuccessDialogBody,
    UninstallAlreadyAbsentDialogBody,
    TDLibRemovedManaged,
    TDLibPreservedUnmanaged,
    TDLibAlreadyAbsent,
    UninstallFailureDialogTitle,
    UninstallFailureDialogBody,
    Detecting,
    DetectedOne,
    DetectedMany,
    DetectionNotFound,
    DetectionFailed,
    FolderBrowserDescription,
    StatusSuccess,
    StatusFailure,
    SuccessBaseExact,
    SuccessBaseNewer,
    SuccessBaseInstalled,
    SuccessBaseRepaired,
    SuccessDialogBody,
    SuccessDialogTitle,
    FailureDialogBody,
    FailureDialogTitle,
    SetupLog,
    LicenseDialogTitle,
    LicenseDialogClose,
    UnexpectedError,
}

internal static class InstallerStrings
{
    public static UiLanguage DetectInitialLanguage()
    {
        return string.Equals(
            CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,
            "zh",
            StringComparison.OrdinalIgnoreCase)
            ? UiLanguage.ZhCn
            : UiLanguage.En;
    }

    public static string Get(UiLanguage language, UiText text, params object?[] args)
    {
        var template = language == UiLanguage.ZhCn ? GetChinese(text) : GetEnglish(text);
        var localizedArguments = args
            .Select(argument => argument is UiText nestedText
                ? Get(language, nestedText)
                : argument)
            .ToArray();
        return args.Length == 0
            ? template
            : string.Format(CultureInfo.InvariantCulture, template, localizedArguments);
    }

    public static UiText GetTDLibUninstallText(
        TDLibUninstallDisposition disposition)
    {
        return disposition switch
        {
            TDLibUninstallDisposition.RemovedManagedToBackup =>
                UiText.TDLibRemovedManaged,
            TDLibUninstallDisposition.PreservedUnmanaged =>
                UiText.TDLibPreservedUnmanaged,
            TDLibUninstallDisposition.AlreadyAbsent =>
                UiText.TDLibAlreadyAbsent,
            _ => throw new ArgumentOutOfRangeException(nameof(disposition)),
        };
    }

    public static string FormatValidation(UiLanguage language, GameValidation validation)
    {
        return validation.Status switch
        {
            ValidationStatus.NoDirectory => language == UiLanguage.ZhCn
                ? "尚未选择尖塔营业厅地址。"
                : "No Slay the Spire 2 folder selected.",
            ValidationStatus.InvalidPath => language == UiLanguage.ZhCn
                ? "路径格式不对，银行柜员看不懂。"
                : "The path format has confused the bank teller.",
            ValidationStatus.MissingGameFiles => language == UiLanguage.ZhCn
                ? "这里不像《杀戮尖塔 2》目录：缺少 SlayTheSpire2.exe、release_info.json 或 data 目录。"
                : "This does not look like the Slay the Spire 2 folder: SlayTheSpire2.exe, release_info.json, or the data directory is missing.",
            ValidationStatus.Supported => language == UiLanguage.ZhCn
                ? validation.Version == GameValidator.LatestVersion
                    ? $"发现 Steam Latest Version {validation.Version}，尖塔金融监管机构点头了。"
                    : $"发现 public-beta {validation.Version}，尖塔金融监管机构点头了。"
                : validation.Version == GameValidator.LatestVersion
                    ? $"Found Steam Latest Version {validation.Version}. The Spire financial regulator approves."
                    : $"Found public-beta {validation.Version}. The Spire financial regulator approves.",
            ValidationStatus.UnsupportedVersion => language == UiLanguage.ZhCn
                ? $"检测到 {validation.Version ?? "未知版本"}；本安装包只支持 Steam Latest {GameValidator.LatestVersion} 和 public-beta {GameValidator.PublicBetaVersion}。请先在 Steam 切换或更新游戏分支。"
                : $"Detected {validation.Version ?? "an unknown version"}; this setup supports Steam Latest {GameValidator.LatestVersion} and public-beta {GameValidator.PublicBetaVersion}. Switch or update the game branch in Steam first.",
            ValidationStatus.ReleaseInfoUnreadable => language == UiLanguage.ZhCn
                ? $"release_info.json 无法读取：{validation.Detail}"
                : $"Cannot read release_info.json: {validation.Detail}",
            _ => throw new ArgumentOutOfRangeException(nameof(validation)),
        };
    }

    public static string FormatProgress(UiLanguage language, InstallStage stage)
    {
        return (language, stage) switch
        {
            (UiLanguage.ZhCn, InstallStage.Inventory) => "正在清点不良资产……",
            (UiLanguage.ZhCn, InstallStage.DeployTDLib) => "正在部署 TDLib 私人金库基础设施……",
            (UiLanguage.ZhCn, InstallStage.VerifyStaging) => "正在核对每一枚金币的 SHA-256……",
            (UiLanguage.ZhCn, InstallStage.InstallTDBank) => "正在开设 Tower Debt 营业厅……",
            (UiLanguage.ZhCn, InstallStage.ProtectSaves) => "正在给原存档上保险，并打通 Mod 专用金库……",
            (UiLanguage.ZhCn, InstallStage.FinalAudit) => "正在进行最终坏账审计……",
            (UiLanguage.En, InstallStage.Inventory) => "Counting distressed assets…",
            (UiLanguage.En, InstallStage.DeployTDLib) => "Deploying the private TDLib vault…",
            (UiLanguage.En, InstallStage.VerifyStaging) => "Checking every gold piece with SHA-256…",
            (UiLanguage.En, InstallStage.InstallTDBank) => "Opening the Tower Debt branch…",
            (UiLanguage.En, InstallStage.ProtectSaves) => "Insuring original saves and opening the modded vault…",
            (UiLanguage.En, InstallStage.FinalAudit) => "Performing the final bad-debt audit…",
            _ => throw new ArgumentOutOfRangeException(nameof(stage)),
        };
    }

    public static string FormatProgress(UiLanguage language, UninstallStage stage)
    {
        return (language, stage) switch
        {
            (UiLanguage.ZhCn, UninstallStage.Inventory) =>
                "正在核对本 Setup 管理的 TD Bank 与 TDLib……",
            (UiLanguage.ZhCn, UninstallStage.MoveTDBank) =>
                "正在移除本 Setup 管理的 TD Bank 与 TDLib；其他 Mod 一律不碰……",
            (UiLanguage.ZhCn, UninstallStage.FinalAudit) =>
                "正在确认 TD Bank/TDLib 已离开加载区；其他 Mod 保持原样，存档交接备份继续保留……",
            (UiLanguage.En, UninstallStage.Inventory) =>
                "Checking the TD Bank and TDLib managed by this Setup…",
            (UiLanguage.En, UninstallStage.MoveTDBank) =>
                "Removing the TD Bank and TDLib managed by this Setup without touching other mods…",
            (UiLanguage.En, UninstallStage.FinalAudit) =>
                "Confirming TD Bank/TDLib left the scan path while other mods remain unchanged and the save-handoff backup stays available…",
            _ => throw new ArgumentOutOfRangeException(nameof(stage)),
        };
    }

    public static string FormatError(UiLanguage language, Exception exception)
    {
        if (exception is not InstallerOperationException installerException)
        {
            return Get(language, UiText.UnexpectedError, exception.Message);
        }

        var path = installerException.TargetPath ?? string.Empty;
        return installerException.Code switch
        {
            InstallerErrorCode.ValidationRejected when installerException.Validation is not null =>
                FormatValidation(language, installerException.Validation),
            InstallerErrorCode.GameRunning => language == UiLanguage.ZhCn
                ? "检测到 SlayTheSpire2.exe 正在运行。银行拒绝在客户打架时装修金库，请完全退出游戏后重试。"
                : "SlayTheSpire2.exe is running. The bank refuses to remodel the vault while customers are fighting. Fully exit the game and retry.",
            InstallerErrorCode.RollbackCompleted => FormatRollbackCompleted(language, installerException),
            InstallerErrorCode.RollbackFailed => language == UiLanguage.ZhCn
                ? $"放贷失败，而且回滚遇到问题。请保留此目录并联系 cnj lab：{installerException.BackupDirectory}{Environment.NewLine}{installerException.Detail}"
                : $"Lending failed and rollback also encountered a problem. Keep this folder and contact cnj lab: {installerException.BackupDirectory}{Environment.NewLine}{installerException.Detail}",
            InstallerErrorCode.UninstallRollbackCompleted =>
                FormatUninstallRollbackCompleted(language, installerException),
            InstallerErrorCode.UninstallRollbackFailed => language == UiLanguage.ZhCn
                ? $"销户失败，而且自动恢复也遇到问题。TD Bank 备份仍保留在：{installerException.BackupDirectory}{Environment.NewLine}{installerException.Detail}"
                : $"Account closure failed and automatic restoration also encountered a problem. The TD Bank recovery backup remains at: {installerException.BackupDirectory}{Environment.NewLine}{installerException.Detail}",
            InstallerErrorCode.UnrecognizedTDBankDirectory => language == UiLanguage.ZhCn
                ? $"为防止误删，安装器拒绝移除身份不明的目录：{path}{Environment.NewLine}其中没有可识别的 TDBank.json 或 cnj lab 安装记录。"
                : $"To prevent deleting an unrelated folder, setup refused to remove this unrecognized directory: {path}{Environment.NewLine}No recognizable TDBank.json or cnj lab install record was found.",
            InstallerErrorCode.HashMismatch => language == UiLanguage.ZhCn
                ? $"文件校验失败：{path}"
                : $"File verification failed: {path}",
            InstallerErrorCode.DirectoryNotWritable => language == UiLanguage.ZhCn
                ? $"Steam 游戏目录不可写：{path}{Environment.NewLine}请右键安装器并选择“以管理员身份运行”。"
                : $"The Steam game folder is not writable: {path}{Environment.NewLine}Right-click the installer and choose “Run as administrator.”",
            InstallerErrorCode.FileLocked => language == UiLanguage.ZhCn
                ? $"文件正在被占用：{path}{Environment.NewLine}请完全退出游戏和其他 Mod 工具后重试。"
                : $"A file is in use: {path}{Environment.NewLine}Fully exit the game and other mod tools, then retry.",
            InstallerErrorCode.ReparsePoint => language == UiLanguage.ZhCn
                ? $"为防止误删，安装器拒绝操作符号链接或目录联接：{path}"
                : $"To prevent accidental deletion, setup refuses to operate on a symbolic link or directory junction: {path}",
            InstallerErrorCode.InsufficientDiskSpace => language == UiLanguage.ZhCn
                ? "磁盘剩余空间不足 32 MB，银行连坏账都放不下了。"
                : "Less than 32 MB of disk space remains. The bank cannot even fit the bad debt.",
            InstallerErrorCode.PathOutsideAllowedRoot => language == UiLanguage.ZhCn
                ? $"拒绝访问预期目录之外的路径：{path}"
                : $"Refusing to access a path outside the expected folder: {path}",
            InstallerErrorCode.DeleteOutsideAllowedRoot => language == UiLanguage.ZhCn
                ? $"拒绝删除预期目录之外的路径：{path}"
                : $"Refusing to delete a path outside the expected folder: {path}",
            InstallerErrorCode.MissingEmbeddedResource => language == UiLanguage.ZhCn
                ? $"安装包内部缺少资源：{path}"
                : $"The setup package is missing an embedded resource: {path}",
            InstallerErrorCode.SaveProtectionFailed => language == UiLanguage.ZhCn
                ? $"存档保险柜操作失败，已停止安装且不会用半截存档糊弄你。原始备份位于：{installerException.BackupDirectory}{Environment.NewLine}{installerException.InnerException?.Message}"
                : $"Save-vault protection failed. Setup stopped instead of leaving a half-migrated profile. Backup: {installerException.BackupDirectory}{Environment.NewLine}{installerException.InnerException?.Message}",
            InstallerErrorCode.SaveRollbackFailed => language == UiLanguage.ZhCn
                ? $"存档迁移失败且自动回滚遇到问题。请勿启动游戏，并保留此备份联系 cnj lab：{installerException.BackupDirectory}{Environment.NewLine}{installerException.Detail}"
                : $"Save migration failed and its automatic rollback also encountered a problem. Do not launch the game; keep this backup and contact cnj lab: {installerException.BackupDirectory}{Environment.NewLine}{installerException.Detail}",
            InstallerErrorCode.UninstallSaveHandoffLaunchFailed =>
                language == UiLanguage.ZhCn
                    ? $"无法通过 Steam 启动游戏完成存档交接。TD Bank 和 TDLib 没有卸载；完整存档备份保留在：{installerException.BackupDirectory}{Environment.NewLine}{installerException.Detail}"
                    : $"The game could not be launched through Steam for the save handoff. TD Bank and TDLib remain installed; the complete save backup is at: {installerException.BackupDirectory}{Environment.NewLine}{installerException.Detail}",
            InstallerErrorCode.UninstallSaveHandoffFailed =>
                language == UiLanguage.ZhCn
                    ? $"存档交接未通过安全校验，因此没有卸载任何 Mod。TD Bank、TDLib 和两套存档都保留。备份：{installerException.BackupDirectory}{Environment.NewLine}{installerException.Detail}"
                    : $"The save handoff did not pass safety verification, so no mod was removed. TD Bank, TDLib, and both save namespaces remain available. Backup: {installerException.BackupDirectory}{Environment.NewLine}{installerException.Detail}",
            InstallerErrorCode.UninstallSaveHandoffTimedOut =>
                language == UiLanguage.ZhCn
                    ? $"等待游戏完成存档交接超时。安装器不会强行结束游戏，也没有卸载 TD Bank/TDLib。请退出游戏后重新运行本 Setup。备份：{installerException.BackupDirectory}{Environment.NewLine}{installerException.Detail}"
                    : $"Timed out waiting for the game to finish the save handoff. Setup did not force-close the game or remove TD Bank/TDLib. Exit the game and run this Setup again. Backup: {installerException.BackupDirectory}{Environment.NewLine}{installerException.Detail}",
            _ => Get(language, UiText.UnexpectedError, exception.Message),
        };
    }

    public static string FormatSaveProtection(
        UiLanguage language,
        SaveProtectionResult result)
    {
        var parts = new List<string>();
        if (result.ProfilesMigrated > 0)
        {
            parts.Add(
                language == UiLanguage.ZhCn
                    ? $"已备份并迁移 {result.ProfilesMigrated} 个普通存档到 Mod 金库"
                    : $"Backed up and migrated {result.ProfilesMigrated} vanilla profile(s) into the modded vault");
        }
        if (result.ProfilesAlreadyEquivalent > 0)
        {
            parts.Add(
                language == UiLanguage.ZhCn
                    ? $"{result.ProfilesAlreadyEquivalent} 个存档已经一致，并已登记首启云保护"
                    : $"{result.ProfilesAlreadyEquivalent} profile(s) already matched and were registered for first-launch cloud protection");
        }
        if (result.ProfilesPreserved > 0)
        {
            parts.Add(
                language == UiLanguage.ZhCn
                    ? $"发现 {result.ProfilesPreserved} 个已有 Mod 进度，全部原样保留"
                    : $"Preserved {result.ProfilesPreserved} established modded profile(s) without changes");
        }
        if (result.ProfilesWithoutVanilla > 0)
        {
            parts.Add(
                language == UiLanguage.ZhCn
                    ? $"{result.ProfilesWithoutVanilla} 个档位没有可用普通存档，未强行处理"
                    : $"Left {result.ProfilesWithoutVanilla} slot(s) alone because no usable vanilla save was found");
        }
        if (result.ProfilesSkippedUnsafe > 0)
        {
            parts.Add(
                language == UiLanguage.ZhCn
                    ? $"{result.ProfilesSkippedUnsafe} 个不安全链接路径已跳过"
                    : $"Skipped {result.ProfilesSkippedUnsafe} unsafe linked path(s)");
        }
        if (parts.Count == 0)
        {
            parts.Add(
                language == UiLanguage.ZhCn
                    ? "当前 Windows 用户下没有找到可迁移的 Steam 普通存档，未改动任何存档"
                    : "No migratable Steam vanilla save was found for this Windows user; no save was changed");
        }
        if (!string.IsNullOrWhiteSpace(result.BackupDirectory))
        {
            parts.Add(
                language == UiLanguage.ZhCn
                    ? $"保险柜备份：{result.BackupDirectory}"
                    : $"Save-vault backup: {result.BackupDirectory}");
        }

        return string.Join(
            language == UiLanguage.ZhCn ? "；" : "; ",
            parts) + (language == UiLanguage.ZhCn ? "。" : ".");
    }

    private static string FormatRollbackCompleted(
        UiLanguage language,
        InstallerOperationException exception)
    {
        var heading = language == UiLanguage.ZhCn
            ? "放贷失败，资产已原路退回（回滚完成）。"
            : "Lending failed. All assets were returned to their original locations (rollback completed).";
        if (exception.InnerException is null)
        {
            return heading;
        }
        return $"{heading}{Environment.NewLine}{FormatError(language, exception.InnerException)}";
    }

    private static string FormatUninstallRollbackCompleted(
        UiLanguage language,
        InstallerOperationException exception)
    {
        var heading = language == UiLanguage.ZhCn
            ? "销户失败，但所有已移动的 TD Bank/TDLib 文件均已完整搬回原位；其他 Mod 未改动。存档交接备份仍然保留。"
            : "Account closure failed, but every moved TD Bank/TDLib file was fully restored. Other mods were unchanged, and the save-handoff backup remains available.";
        if (exception.InnerException is null)
        {
            return heading;
        }

        return $"{heading}{Environment.NewLine}{FormatError(language, exception.InnerException)}";
    }

    private static string GetChinese(UiText text)
    {
        return text switch
        {
            UiText.WindowTitle => "TD Bank v0.1 — Tower Debt Setup v0.1 一键安装/卸载器",
            UiText.HeaderTitle => "Tower Debt Setup v0.1 — 安装 / 销户",
            UiText.HeaderTagline => "把今天的金币，变成明天的财务问题。",
            UiText.Disclaimer =>
                "本安装包由 cnj lab 制作。\r\n" +
                "TD 代表 Tower Debt，保证和任何叫 TD 的银行没有关联。\r\n" +
                "如有巧合，纯属雷同；cnj lab 不承担任何责任。\r\n" +
                "首次启动仍需亲自确认游戏官方的“加载 Mod”安全提示。",
            UiText.PathHeading => "尖塔营业厅地址",
            UiText.Browse => "人工指定营业厅",
            UiText.PayloadSummary =>
                "本 Setup 不安装任何 Windows 软件，只把以下两个 Mod 放入游戏的 mods 文件夹：\r\n" +
                "• TD Bank v0.1\r\n" +
                "给《杀戮尖塔 2》增加一个银行，它会改变游戏玩法。\r\n" +
                "• TDLib v0.1\r\n" +
                "TD Bank 专用的存档与多人同步组件。它只负责保存银行账户数据，不会替换或影响 BaseLib。\r\n" +
                "由本 Setup 安装的 TDLib 会随 TD Bank 一起卸载；普通 BaseLib 和其他 Mod 一律不动。\r\n" +
                "TDLib（MIT） 的存档扩展代码改编自 BaseLib（MIT），许可证随安装包提供。\r\n" +
                "安装前会备份普通档和 Mod 档。只初始化缺失或确认空白的 Mod 档，已有进度绝不覆盖。",
            UiText.PayloadSummaryInstalled =>
                "检测到 TD Bank 已开户：可以绿色重新安装，也可以红色办理销户。\r\n" +
                "销户只移除本 Setup 管理的 TD Bank 和 TDLib；绝不会清空其他 Mod。",
            UiText.Consent => "我确认 TD 是 Tower Debt，并自愿加入尖塔负债计划，同时无条件同意 8600 页霸王条款。",
            UiText.StatusPrivacy => "安装不上传数据；销户存档交接只使用游戏自己的 Steam Cloud。",
            UiText.LicenseLink => "查看 TDLib / BaseLib MIT 许可",
            UiText.Cancel => "暂时不欠钱",
            UiText.Install => "开始放贷 / 安装",
            UiText.Reinstall => "重新装修 / 重装",
            UiText.Installing => "正在安装……",
            UiText.Installed => "安装完成",
            UiText.Uninstall => "注销账户 / 卸载",
            UiText.Uninstalling => "正在清算……",
            UiText.InstalledDetected => "检测到 TD Bank 已开户，可以重新安装或办理销户。",
            UiText.UninstallConfirmTitle => "Tower Debt 销户确认",
            UiText.UninstallConfirmBody =>
                "确认注销 TD Bank？\r\n\r\n" +
                "将移除：本 Setup 管理的 mods\\TDBank 与 mods\\TDLib（包括你替换过的图片）。\r\n" +
                "绝不会清空其他 Mod。需要回到普通档时，会先完整备份；Setup 会自动启动一次游戏，等游戏把普通档与 Steam Cloud 交接并验明正身后才卸载。失败就保留 Mod。\r\n\r\n" +
                "请先完全退出游戏；卸载失败时会自动恢复已经移动的 Mod 文件。",
            UiText.StatusUninstallSuccess => "销户成功：TD Bank 已从 Mods 加载区移除。{0} 其他 Mod 未清空，存档交接已安全完成或无需进行。恢复备份：{1}",
            UiText.StatusUninstallAlreadyAbsent => "TD Bank 本来就不在 Mods 加载区。{0} 其他 Mod 和所有存档均未改动。",
            UiText.StatusUninstallFailure => "销户失败；请查看错误和安装日志。",
            UiText.UninstallSuccessDialogTitle => "Tower Debt 销户回执",
            UiText.UninstallSuccessDialogBody =>
                "本 Setup 管理的 TD Bank 已从游戏 Mods 目录移除。\r\n" +
                "{0}\r\n\r\n" +
                "其他 Mod 没有清空；需要的存档交接已在备份和校验后完成。\r\n" +
                "可恢复的 Mod 文件备份：\r\n{1}",
            UiText.UninstallAlreadyAbsentDialogBody =>
                "TD Bank 本来就不在 Mods 目录中。\r\n" +
                "{0}\r\n" +
                "其他 Mod 和所有存档均未改动。",
            UiText.TDLibRemovedManaged =>
                "TDLib 确认由本 Setup 安装且文件完全一致，已安全移出 Mods 加载区。",
            UiText.TDLibPreservedUnmanaged =>
                "TDLib 已被修改或无法证明归属，为防止误删已保留；普通 BaseLib 从未被操作。",
            UiText.TDLibAlreadyAbsent => "TDLib 在注销前本来就不存在。",
            UiText.UninstallFailureDialogTitle => "Tower Debt 销户失败",
            UiText.UninstallFailureDialogBody => "{0}\r\n\r\n安装/卸载日志：\r\n{1}",
            UiText.Detecting => "正在翻 Steam 的账本寻找 app 2868840……",
            UiText.DetectedOne => "Steam 营业厅已自动定位。",
            UiText.DetectedMany => "找到 {0} 个候选目录，当前使用第一个；如不对可手动选择。",
            UiText.DetectionNotFound => "没有自动找到游戏，请点击“人工指定营业厅”。",
            UiText.DetectionFailed => "自动定位失败：{0}",
            UiText.FolderBrowserDescription => "请选择包含 SlayTheSpire2.exe 的《杀戮尖塔 2》游戏目录",
            UiText.StatusSuccess => "放贷成功：TDLib 与 TD Bank 已就位。{0}",
            UiText.StatusFailure => "放贷失败；请查看错误和安装日志。",
            UiText.SuccessBaseExact => "现有 TDLib v0.1 完全正常，银行一根手指都没碰它。",
            UiText.SuccessBaseNewer => "检测到更新版 TDLib，银行识趣地保留了它。",
            UiText.SuccessBaseInstalled => "TDLib v0.1 已安装。",
            UiText.SuccessBaseRepaired => "TDLib 已备份并升级或修复为 v0.1。",
            UiText.SuccessDialogBody =>
                "放贷成功！\r\n\r\n{0}\r\n" +
                "TD Bank v0.1 已安装并通过 SHA-256 核对。\r\n" +
                "A3–A10 会动态发放舒适辅助：A3+ 最低卡开户即批；额度、首次免息、储蓄补贴、KK 收益、菊部风控生存保护和抄家保护随进阶增强，欠款利率随进阶降低。重要银行结果现在会中央弹窗。\r\n" +
                "进游戏首次点开 TD 图标后，请看完规则并勾选两项；“同意并开户”和“被迫同意并开户”都会完成开户。\r\n\r\n" +
                "存档处理：{1}\r\n\r\n" +
                "首次启动出现官方 Mod 安全提示时，请亲自点击“加载 Mod”，游戏会退出并重启一次。",
            UiText.SuccessDialogTitle => "Tower Debt 批款通知",
            UiText.FailureDialogBody => "{0}\r\n\r\n安装日志：\r\n{1}",
            UiText.FailureDialogTitle => "Tower Debt 拒批通知",
            UiText.SetupLog => "安装日志",
            UiText.LicenseDialogTitle => "TDLib v0.1 / BaseLib — MIT 许可证",
            UiText.LicenseDialogClose => "看完了，大概吧",
            UiText.UnexpectedError => "发生了意外错误：{0}",
            _ => throw new ArgumentOutOfRangeException(nameof(text)),
        };
    }

    private static string GetEnglish(UiText text)
    {
        return text switch
        {
            UiText.WindowTitle => "TD Bank v0.1 — Tower Debt Setup v0.1",
            UiText.HeaderTitle => "Tower Debt Setup v0.1 — Install / Uninstall",
            UiText.HeaderTagline => "Turn today’s gold into tomorrow’s financial problems.",
            UiText.Disclaimer =>
                "This installer was made by cnj lab.\r\n" +
                "In this mod, TD means Tower Debt. It is guaranteed to have no connection with any bank called TD.\r\n" +
                "Any resemblance is purely coincidental; cnj lab accepts no responsibility.\r\n" +
                "On first launch, you must personally confirm the game’s official “Load Mods” safety prompt.",
            UiText.PathHeading => "Slay the Spire 2 folder",
            UiText.Browse => "Choose Folder",
            UiText.PayloadSummary =>
                "This Setup installs no Windows software. It only places the following two mods in the game’s mods folder:\r\n" +
                "• TD Bank v0.1\r\n" +
                "Adds a bank to Slay the Spire 2 and changes gameplay.\r\n" +
                "• TDLib v0.1\r\n" +
                "TD Bank’s dedicated save and multiplayer-sync component. It only stores bank-account data and does not replace or affect BaseLib.\r\n" +
                "TDLib installed by this Setup is removed together with TD Bank; ordinary BaseLib and other mods are never touched.\r\n" +
                "TDLib’s save-extension code is adapted from BaseLib under the MIT License; the license is included with Setup.\r\n" +
                "Before installation, Setup backs up vanilla and modded saves. It only initializes a missing or provably blank modded profile and never overwrites existing progress.",
            UiText.PayloadSummaryInstalled =>
                "TD Bank is installed: use green to reinstall or red to close the account.\r\n" +
                "Uninstall removes only TD Bank and TDLib managed by this Setup. It never clears other mods.",
            UiText.Consent => "I confirm that TD means Tower Debt, voluntarily join the Spire debt program, and unconditionally accept all 8,600 pages of tyrannical terms.",
            UiText.StatusPrivacy => "Install uploads no data; account closure uses only the game’s own Steam Cloud for its save handoff.",
            UiText.LicenseLink => "View TDLib / BaseLib MIT License",
            UiText.Cancel => "Avoid Debt for Now",
            UiText.Install => "Start Lending / Install",
            UiText.Reinstall => "Renovate / Reinstall",
            UiText.Installing => "Installing…",
            UiText.Installed => "Installed",
            UiText.Uninstall => "Close Account / Uninstall",
            UiText.Uninstalling => "Liquidating…",
            UiText.InstalledDetected => "TD Bank is installed. Reinstall it or close the account.",
            UiText.UninstallConfirmTitle => "Tower Debt Account Closure",
            UiText.UninstallConfirmBody =>
                "Close the TD Bank account?\r\n\r\n" +
                "Will be removed: mods\\TDBank and mods\\TDLib managed by this Setup, including replacement artwork.\r\n" +
                "Other mods are never cleared. When a vanilla handoff is needed, Setup creates a full backup, automatically launches the game once, and removes the mods only after the game verifies the vanilla/Steam Cloud transfer. Failure keeps the mods installed.\r\n\r\n" +
                "Fully exit the game first. If uninstall fails, moved mod files are restored automatically.",
            UiText.StatusUninstallSuccess => "Account closed: TD Bank was removed from the Mods scan path. {0} Other mods were not cleared; save handoff completed safely or was not needed. Recovery backup: {1}",
            UiText.StatusUninstallAlreadyAbsent => "TD Bank was already absent from the Mods scan path. {0} Other mods and all saves remain unchanged.",
            UiText.StatusUninstallFailure => "Account closure failed. See the error and setup log.",
            UiText.UninstallSuccessDialogTitle => "Tower Debt Account Closure Receipt",
            UiText.UninstallSuccessDialogBody =>
                "The TD Bank managed by this Setup was removed from the game's Mods folder.\r\n" +
                "{0}\r\n\r\n" +
                "Other mods were not cleared. Any required save handoff completed only after backup and verification.\r\n" +
                "Recoverable mod-file backup:\r\n{1}",
            UiText.UninstallAlreadyAbsentDialogBody =>
                "TD Bank was already absent from the Mods folder.\r\n" +
                "{0}\r\n" +
                "Other mods and all saves remain unchanged.",
            UiText.TDLibRemovedManaged =>
                "TDLib was verified as installed by this Setup with an exact file match, so it was safely moved out of the Mods scan path.",
            UiText.TDLibPreservedUnmanaged =>
                "TDLib was modified or could not be proven to belong to this Setup, so it was preserved; ordinary BaseLib was never touched.",
            UiText.TDLibAlreadyAbsent => "TDLib was already absent before account closure.",
            UiText.UninstallFailureDialogTitle => "Tower Debt Account Closure Failed",
            UiText.UninstallFailureDialogBody => "{0}\r\n\r\nSetup/uninstall log:\r\n{1}",
            UiText.Detecting => "Searching Steam’s books for app 2868840…",
            UiText.DetectedOne => "Steam branch found automatically.",
            UiText.DetectedMany => "Found {0} candidate folders. Using the first; choose another folder if needed.",
            UiText.DetectionNotFound => "Game not found automatically. Click “Choose Folder”.",
            UiText.DetectionFailed => "Auto-detection failed: {0}",
            UiText.FolderBrowserDescription => "Select the Slay the Spire 2 folder containing SlayTheSpire2.exe.",
            UiText.StatusSuccess => "Lending approved: TDLib and TD Bank are in place. {0}",
            UiText.StatusFailure => "Lending failed. See the error and setup log.",
            UiText.SuccessBaseExact => "TDLib v0.1 was already healthy; the bank did not lay a finger on it.",
            UiText.SuccessBaseNewer => "A newer TDLib was found and wisely preserved.",
            UiText.SuccessBaseInstalled => "TDLib v0.1 was installed.",
            UiText.SuccessBaseRepaired => "TDLib was backed up and upgraded or repaired to v0.1.",
            UiText.SuccessDialogBody =>
                "Loan approved!\r\n\r\n{0}\r\n" +
                "TD Bank v0.1 was installed and verified with SHA-256.\r\n" +
                "A3–A10 now receive dynamic comfort assists: on A3+ the lowest card is approved at account opening; limits, the one-time grace period, savings subsidies, KK payouts, risk-control survivability, and relic-seizure protection improve with Ascension while debt rates fall. Critical bank results now use centered dialogs.\r\n" +
                "In game, open TD for the first time, read the rules, and check both boxes. Both “Agree and open” and “Forced to agree and open” complete account opening.\r\n\r\n" +
                "Save handling: {1}\r\n\r\n" +
                "On first launch, click “Load Mods” on the official safety prompt. The game will exit and restart once.",
            UiText.SuccessDialogTitle => "Tower Debt Loan Approval",
            UiText.FailureDialogBody => "{0}\r\n\r\nSetup log:\r\n{1}",
            UiText.FailureDialogTitle => "Tower Debt Application Denied",
            UiText.SetupLog => "Setup log",
            UiText.LicenseDialogTitle => "TDLib v0.1 / BaseLib — MIT License",
            UiText.LicenseDialogClose => "Read It. Probably.",
            UiText.UnexpectedError => "An unexpected error occurred: {0}",
            _ => throw new ArgumentOutOfRangeException(nameof(text)),
        };
    }
}
