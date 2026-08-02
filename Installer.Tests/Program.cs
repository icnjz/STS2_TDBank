using CNJ.TowerDebt.Setup;
using CNJ.TowerDebt.Setup.Core;
using CNJ.TowerDebt.Setup.Tests;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

var testRoot = Path.Combine(
    Path.GetTempPath(),
    $"cnj-tower-debt-installer-tests-{Guid.NewGuid():N}");
var ignoreLiveGameForFakeFixtures = args.Contains(
    "--test-ignore-live-game",
    StringComparer.OrdinalIgnoreCase);
if (ignoreLiveGameForFakeFixtures)
{
    TransactionInstaller.GameRunningTestHook = static () => false;
    TransactionUninstaller.GameRunningTestHook = static () => false;
}

try
{
    Directory.CreateDirectory(testRoot);
    var installerSaveRoot = Path.Combine(testRoot, "installer-install-flow-saves");
    AssertEmbeddedReleaseVersions(testRoot);
    SaveProtectionTests.Run(Path.Combine(testRoot, "save-protection"));
    UninstallSaveHandoffTests.Run(Path.Combine(testRoot, "uninstall-save-handoff"));
    if (args.Contains("--dependency-only", StringComparer.OrdinalIgnoreCase))
    {
        RunUninstallMatrix(Path.Combine(testRoot, "uninstall"));
        Console.WriteLine(
            "TD Bank Setup TDLib/BaseLib-isolation matrix passed.");
        return;
    }

    AssertAllLocalizedText();
    AssertUiLanguageSwitchPreservesState();
    if (args.Contains("--uninstall-only", StringComparer.OrdinalIgnoreCase))
    {
        TransactionInstaller.GameRunningTestHook = static () => false;
        TransactionUninstaller.GameRunningTestHook = static () => false;
        try
        {
            RunUninstallMatrix(Path.Combine(testRoot, "uninstall"));
        }
        finally
        {
            TransactionInstaller.GameRunningTestHook = null;
            TransactionUninstaller.GameRunningTestHook = null;
        }

        Console.WriteLine(
            "TD Bank Setup v0.1.4 uninstall/save-preservation matrix passed.");
        return;
    }

    RunUninstallMatrix(Path.Combine(testRoot, "uninstall"));

    var latestGame = CreateFakeGame(Path.Combine(testRoot, "latest"), "v0.110.0");
    var latestValidation = GameValidator.Validate(latestGame);
    Assert(latestValidation.IsGameDirectory, "Valid Steam Latest fixture was not recognized.");
    Assert(latestValidation.IsSupportedVersion, "Supported Steam Latest version was rejected.");
    Assert(
        InstallerStrings.FormatValidation(UiLanguage.ZhCn, latestValidation).Contains("LTS"),
        "Chinese verified-version validation omitted the LTS result.");
    Assert(
        InstallerStrings.FormatValidation(UiLanguage.En, latestValidation).Contains("LTS"),
        "English verified-version validation omitted the LTS result.");

    var freshGame = CreateFakeGame(Path.Combine(testRoot, "fresh"), "v0.109.1");
    var validation = GameValidator.Validate(freshGame);
    Assert(validation.IsGameDirectory, "Valid fake game was not recognized.");
    Assert(validation.IsSupportedVersion, "Supported public-beta version was rejected.");
    Assert(validation.Status == ValidationStatus.Supported, "Supported validation status was incorrect.");
    Assert(
        InstallerStrings.FormatValidation(UiLanguage.ZhCn, validation).Contains("LTS"),
        "Chinese supported-version validation was not localized.");
    Assert(
        InstallerStrings.FormatValidation(UiLanguage.En, validation).Contains("LTS"),
        "English supported-version validation was not localized.");

    var reportedStages = new List<InstallStage>();
    var first = TransactionInstaller.Install(
        freshGame,
        installerSaveRoot,
        new RecordingProgress<InstallStage>(reportedStages.Add));
    Assert(first.TDLibAction == TDLibInstallAction.Install, "Fresh install did not install TDLib.");
    Assert(
        reportedStages.SequenceEqual(
        [
            InstallStage.Inventory,
            InstallStage.DeployTDLib,
            InstallStage.VerifyStaging,
            InstallStage.InstallTDBank,
            InstallStage.FinalAudit,
            InstallStage.ProtectSaves,
        ]),
        "Fresh install did not report the expected language-neutral progress stages.");
    AssertPayload(freshGame);
    AssertInstallerVersion(freshGame);
    AssertTDLibOwnershipState(
        freshGame,
        expectedManaged: true,
        expectedAction: TDLibInstallAction.Install);

    var customFile = Path.Combine(freshGame, "mods", "TDBank", "Assets", "friend-custom-note.txt");
    File.WriteAllText(customFile, "custom asset marker");
    var logoPath = Path.Combine(freshGame, "mods", "TDBank", "Assets", "bank_logo.png");
    File.WriteAllBytes(logoPath, [1, 2, 3, 4]);

    var legacyBackupTd = Path.Combine(
        freshGame,
        "mods",
        ".cnj-tdbank-backups",
        "old-transaction",
        "TDBank");
    Directory.CreateDirectory(legacyBackupTd);
    File.WriteAllText(
        Path.Combine(legacyBackupTd, "TDBank.json"),
        """{"id":"TDBank","version":"v0.2.0","has_dll":true}""");
    File.WriteAllText(
        Path.Combine(legacyBackupTd, "legacy-backup-sentinel.txt"),
        "must be retained outside mods");

    var legacyStageTd = Path.Combine(
        freshGame,
        "mods",
        ".cnj-tdbank-stage-crashed-install",
        "TDBank");
    Directory.CreateDirectory(legacyStageTd);
    File.WriteAllText(
        Path.Combine(legacyStageTd, "TDBank.json"),
        """{"id":"TDBank","version":"v0.1.0","has_dll":true}""");
    File.WriteAllText(
        Path.Combine(legacyStageTd, "legacy-stage-sentinel.txt"),
        "must be retained outside mods");

    var second = TransactionInstaller.Install(freshGame, installerSaveRoot);
    Assert(second.TDLibAction == TDLibInstallAction.PreserveExact, "Exact TDLib should be preserved.");
    AssertPayload(freshGame);
    AssertTDLibOwnershipState(
        freshGame,
        expectedManaged: true,
        expectedAction: TDLibInstallAction.PreserveExact);
    Assert(!string.IsNullOrWhiteSpace(second.BackupDirectory), "Reinstall did not create a backup.");
    Assert(
        !IsPathUnder(second.BackupDirectory, Path.Combine(freshGame, "mods")),
        "The retained mod backup is still inside the recursive mod scan tree.");
    Assert(
        File.Exists(Path.Combine(second.BackupDirectory, "TDBank", "Assets", "friend-custom-note.txt")),
        "Existing custom TD Bank file was not preserved in the backup.");
    Assert(
        File.Exists(Path.Combine(
            second.BackupDirectory,
            "legacy-mods-artifacts",
            ".cnj-tdbank-backups",
            "old-transaction",
            "TDBank",
            "legacy-backup-sentinel.txt")),
        "The legacy backup tree was not retained outside mods.");
    Assert(
        File.Exists(Path.Combine(
            second.BackupDirectory,
            "legacy-mods-artifacts",
            ".cnj-tdbank-stage-crashed-install",
            "TDBank",
            "legacy-stage-sentinel.txt")),
        "The legacy staging tree was not retained outside mods.");
    AssertNoShadowTDBankManifests(freshGame);

    var baseDll = Path.Combine(freshGame, "mods", "TDLib", "TDLib.dll");
    File.WriteAllBytes(baseDll, [9, 9, 9]);
    var repaired = TransactionInstaller.Install(freshGame, installerSaveRoot);
    Assert(
        repaired.TDLibAction == TDLibInstallAction.UpgradeOrRepair,
        "Tampered TDLib was not repaired.");
    AssertPayload(freshGame);
    AssertTDLibOwnershipState(
        freshGame,
        expectedManaged: true,
        expectedAction: TDLibInstallAction.UpgradeOrRepair);

    using (var locked = new FileStream(
        Path.Combine(freshGame, "mods", "TDBank", "TDBank.dll"),
        FileMode.Open,
        FileAccess.ReadWrite,
        FileShare.None))
    {
        var blocked = false;
        try
        {
            TransactionInstaller.Install(freshGame, installerSaveRoot);
        }
        catch (InstallerOperationException exception)
        {
            blocked = exception.Code == InstallerErrorCode.FileLocked;
        }
        Assert(blocked, "Locked TD Bank DLL was not rejected.");
    }
    AssertPayload(freshGame);

    var rollbackGame = CreateFakeGame(Path.Combine(testRoot, "post-swap-rollback"), "v0.109.1");
    var rollbackSaveRoot = Path.Combine(testRoot, "post-swap-rollback-saves");
    TransactionInstaller.Install(rollbackGame, rollbackSaveRoot);
    var rollbackSentinel = Path.Combine(
        rollbackGame,
        "mods",
        "TDBank",
        "must-survive-rollback.txt");
    File.WriteAllText(rollbackSentinel, "previous TD Bank install");
    var rollbackLegacySentinel = Path.Combine(
        rollbackGame,
        "mods",
        ".cnj-tdbank-backups",
        "old",
        "TDBank",
        "must-return-on-rollback.txt");
    Directory.CreateDirectory(Path.GetDirectoryName(rollbackLegacySentinel)!);
    File.WriteAllText(rollbackLegacySentinel, "legacy backup");

    var rollbackAccount = Path.Combine(
        rollbackSaveRoot,
        "steam",
        "76561198000000003");
    File.WriteAllText(
        CreateParent(Path.Combine(rollbackAccount, "profile.save")),
        """{"last_profile_id":1,"schema_version":2}""");
    var rollbackProgress = CreateParent(Path.Combine(
        rollbackAccount,
        "profile1",
        "saves",
        "progress.save"));
    File.WriteAllText(
        rollbackProgress,
        """{"schema_version":22,"unique_id":"ROLLBACK"}""");
    var lockedSaveFile = CreateParent(Path.Combine(
        rollbackAccount,
        "profile1",
        "saves",
        "locked-secondary.save"));
    File.WriteAllText(lockedSaveFile, "hold this file open");

    using (var lockedSave = new FileStream(
               lockedSaveFile,
               FileMode.Open,
               FileAccess.ReadWrite,
               FileShare.None))
    {
        var rolledBack = false;
        try
        {
            TransactionInstaller.Install(rollbackGame, rollbackSaveRoot);
        }
        catch (InstallerOperationException exception)
        {
            rolledBack = exception.Code == InstallerErrorCode.RollbackCompleted;
        }
        Assert(
            rolledBack,
            "A post-swap save-protection failure did not complete the outer rollback.");
    }
    Assert(
        File.ReadAllText(rollbackSentinel) == "previous TD Bank install",
        "Post-swap rollback did not restore the previous TD Bank directory.");
    Assert(
        File.ReadAllText(rollbackLegacySentinel) == "legacy backup",
        "Post-swap rollback did not return legacy installer data to its original path.");

    var newerGame = CreateFakeGame(Path.Combine(testRoot, "newer-baselib"), "v0.109.1");
    var newerBase = Path.Combine(newerGame, "mods", "TDLib");
    Directory.CreateDirectory(newerBase);
    File.WriteAllText(
        Path.Combine(newerBase, "TDLib.json"),
        """{"id":"TDLib","version":"v9.0.0","has_dll":true,"has_pck":true}""");
    File.WriteAllBytes(Path.Combine(newerBase, "TDLib.dll"), [7, 7, 7]);
    File.WriteAllBytes(Path.Combine(newerBase, "TDLib.pck"), [8, 8, 8]);

    var newer = TransactionInstaller.Install(newerGame, installerSaveRoot);
    Assert(newer.TDLibAction == TDLibInstallAction.PreserveNewer, "Newer TDLib was not preserved.");
    Assert(
        File.ReadAllBytes(Path.Combine(newerBase, "TDLib.dll")).SequenceEqual(new byte[] { 7, 7, 7 }),
        "Newer TDLib DLL was overwritten.");
    AssertTDBankPayload(newerGame);

    var futureGame = CreateFakeGame(Path.Combine(testRoot, "future"), "v9.9.9");
    var future = GameValidator.Validate(futureGame);
    Assert(future.IsGameDirectory, "Future fake game directory was not recognized.");
    Assert(future.IsSupportedVersion, "Future game version was not accepted in LTS mode.");
    Assert(
        future.Status == ValidationStatus.ForwardCompatible,
        "Future validation status was not forward-compatible.");
    Assert(
        InstallerStrings.FormatValidation(UiLanguage.En, future).Contains("forward-compatible"),
        "English future-version message omitted LTS forward compatibility.");

    var unsupportedGame = CreateFakeGame(Path.Combine(testRoot, "unsupported"), "v0.106.9");
    var unsupported = GameValidator.Validate(unsupportedGame);
    Assert(unsupported.IsGameDirectory, "Unsupported fake game directory should still be recognized.");
    Assert(!unsupported.IsSupportedVersion, "Unsupported game version was accepted.");
    Assert(
        unsupported.Status == ValidationStatus.UnsupportedVersion,
        "Unsupported validation status was incorrect.");
    Assert(
        InstallerStrings.FormatValidation(UiLanguage.En, unsupported).Contains("v0.106.9"),
        "English unsupported-version message omitted the detected version.");

    Console.WriteLine("TD Bank setup transactional and bilingual tests passed.");
}
finally
{
    TransactionInstaller.GameRunningTestHook = null;
    TransactionUninstaller.GameRunningTestHook = null;
    var fullTestRoot = Path.GetFullPath(testRoot);
    var expectedPrefix = Path.GetFullPath(Path.GetTempPath())
        .TrimEnd(Path.DirectorySeparatorChar)
        + Path.DirectorySeparatorChar
        + "cnj-tower-debt-installer-tests-";
    if (fullTestRoot.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase)
        && Directory.Exists(fullTestRoot))
    {
        Directory.Delete(fullTestRoot, recursive: true);
    }
}

return;

static string CreateFakeGame(string path, string version)
{
    Directory.CreateDirectory(path);
    Directory.CreateDirectory(Path.Combine(path, "data_sts2_windows_x86_64"));
    File.WriteAllBytes(Path.Combine(path, "SlayTheSpire2.exe"), [0x4D, 0x5A]);
    File.WriteAllText(
        Path.Combine(path, "release_info.json"),
        JsonSerializer.Serialize(new
        {
            commit = "installer-test",
            version,
            date = "2026-07-26",
            branch = version,
            main_assembly_hash = 195020890,
        }));
    return path;
}

static string CreateParent(string path)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    return path;
}

static void AssertPayload(string game)
{
    AssertTDBankPayload(game);
    var mods = Path.Combine(game, "mods");
    foreach (var file in EmbeddedPayload.Files.Where(file => file.IsTDLib))
    {
        var target = Path.Combine(mods, file.RelativePath);
        Assert(EmbeddedPayload.Matches(file, target), $"Payload mismatch: {target}");
    }
}

static void AssertTDBankPayload(string game)
{
    var mods = Path.Combine(game, "mods");
    foreach (var file in EmbeddedPayload.Files.Where(file => !file.IsTDLib))
    {
        var target = Path.Combine(mods, file.RelativePath);
        Assert(EmbeddedPayload.Matches(file, target), $"Payload mismatch: {target}");
    }
    Assert(
        File.Exists(Path.Combine(mods, "TDBank", "install-state.json")),
        "Installer state file is missing.");
}

static void AssertInstallerVersion(string game)
{
    var statePath = Path.Combine(game, "mods", "TDBank", "install-state.json");
    using var state = JsonDocument.Parse(File.ReadAllText(statePath));
    Assert(
        state.RootElement.GetProperty("packageVersion").GetString() == "v0.1.4",
        "Installer state did not record package version v0.1.4.");
    Assert(
        state.RootElement.GetProperty("installerVersion").GetString() == "v0.1.4",
        "Installer state did not record setup version v0.1.4.");
}

static void AssertTDLibOwnershipState(
    string game,
    bool expectedManaged,
    TDLibInstallAction expectedAction)
{
    var statePath = Path.Combine(game, "mods", "TDBank", "install-state.json");
    using var state = JsonDocument.Parse(File.ReadAllText(statePath));
    var ownership = state.RootElement.GetProperty("tdLibOwnership");
    Assert(
        ownership.GetProperty("schemaVersion").GetInt32()
            == TDLibOwnership.SchemaVersion,
        "Installer state did not record the supported TDLib ownership schema.");
    Assert(
        ownership.GetProperty("managedBySetup").GetBoolean() == expectedManaged,
        "Installer state recorded the wrong TDLib ownership.");
    Assert(
        ownership.GetProperty("actionAtThisInstall").GetString()
            == expectedAction.ToString(),
        "Installer state recorded the wrong TDLib action.");
    Assert(
        ownership.GetProperty("payloadVersion").GetString()
            == EmbeddedPayload.RequiredTDLibVersion.ToString(),
        "Installer state recorded the wrong TDLib payload version.");

    var expectedProof = EmbeddedPayload.Files
        .Where(file => file.IsTDLib)
        .ToDictionary(
            file => Path.GetRelativePath("TDLib", file.RelativePath)
                .Replace(Path.DirectorySeparatorChar, '/'),
            file => EmbeddedPayload.Hash(EmbeddedPayload.Read(file)),
            StringComparer.OrdinalIgnoreCase);
    var actualProof = ownership.GetProperty("payloadFiles")
        .EnumerateArray()
        .ToDictionary(
            item => item.GetProperty("relativePath").GetString()!,
            item => item.GetProperty("sha256").GetString()!,
            StringComparer.OrdinalIgnoreCase);
    Assert(
        actualProof.Count == expectedProof.Count
        && expectedProof.All(pair =>
            actualProof.TryGetValue(pair.Key, out var hash)
            && string.Equals(hash, pair.Value, StringComparison.OrdinalIgnoreCase)),
        "Installer state did not record the exact TDLib payload proof.");
}

static void AssertNoShadowTDBankManifests(string game)
{
    var mods = Path.Combine(game, "mods");
    var expected = Path.GetFullPath(Path.Combine(mods, "TDBank", "TDBank.json"));
    var manifests = Directory.EnumerateFiles(
            mods,
            "TDBank.json",
            SearchOption.AllDirectories)
        .Select(Path.GetFullPath)
        .ToArray();
    Assert(
        manifests.Length == 1
            && manifests[0].Equals(expected, StringComparison.OrdinalIgnoreCase),
        "A backup or staging TDBank manifest remains in the recursive mod scan tree.");
}

static bool IsPathUnder(string path, string root)
{
    var fullRoot = Path.GetFullPath(root)
        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        + Path.DirectorySeparatorChar;
    var fullPath = Path.GetFullPath(path);
    return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
}

static void AssertEmbeddedReleaseVersions(string testRoot)
{
    var expectedCardAssets = new HashSet<string>(
        [
            "bisa_broke_zh.png",
            "bisa_middle_zh.png",
            "bisa_rich_zh.png",
            "bisa_broke_en.png",
            "bisa_middle_en.png",
            "bisa_rich_en.png",
        ],
        StringComparer.OrdinalIgnoreCase);
    var embeddedCardAssets = EmbeddedPayload.Files
        .Where(file => Path.GetFileName(file.RelativePath)
            .StartsWith("bisa_", StringComparison.OrdinalIgnoreCase))
        .Select(file => Path.GetFileName(file.RelativePath))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    Assert(
        embeddedCardAssets.SetEquals(expectedCardAssets),
        "Embedded TD Bank card artwork must contain exactly six localized Chinese/English card images.");
    Assert(
        EmbeddedPayload.Files.Any(file => file.RelativePath.EndsWith(
            Path.Combine("Assets", "bank_logo.png"),
            StringComparison.OrdinalIgnoreCase))
        && EmbeddedPayload.Files.Any(file => file.RelativePath.EndsWith(
            Path.Combine("Assets", "bank_background.png"),
            StringComparison.OrdinalIgnoreCase)),
        "Embedded TD Bank payload must retain bank_logo.png and bank_background.png.");

    var manifestFile = EmbeddedPayload.Files.Single(file =>
        file.RelativePath.EndsWith(
            Path.Combine("TDBank", "TDBank.json"),
            StringComparison.OrdinalIgnoreCase));
    using (var manifest = JsonDocument.Parse(EmbeddedPayload.Read(manifestFile)))
    {
        Assert(
            manifest.RootElement.GetProperty("version").GetString() == "0.1.4",
            "Embedded TD Bank manifest is not 0.1.4.");
        Assert(
            manifest.RootElement.GetProperty("author").GetString() == "cnj lab",
            "Embedded TD Bank manifest must spell the user-visible author cnj lab in lowercase.");
        Assert(
            manifest.RootElement.GetProperty("affects_gameplay").GetBoolean(),
            "Embedded TD Bank manifest must require the exact gameplay-mod "
            + "version in multiplayer.");
        var dependencies = manifest.RootElement
            .GetProperty("dependencies")
            .EnumerateArray()
            .ToArray();
        Assert(
            dependencies.Length == 1
            && dependencies[0].GetProperty("id").GetString() == "TDLib"
            && dependencies[0].GetProperty("min_version").GetString()
                == "0.1.0",
            "Embedded TD Bank manifest does not depend exclusively on TDLib 0.1.0.");
    }

    var tdLibPayloadFiles = EmbeddedPayload.Files
        .Where(file => file.IsTDLib)
        .ToArray();
    var expectedTDLibFiles = new HashSet<string>(
        [
            "TDLib.dll",
            "TDLib.json",
            "THIRD_PARTY_LICENSES/BaseLib-LICENSE.txt",
        ],
        StringComparer.OrdinalIgnoreCase);
    Assert(
        tdLibPayloadFiles
            .Select(file => Path.GetRelativePath("TDLib", file.RelativePath)
                .Replace(Path.DirectorySeparatorChar, '/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            .SetEquals(expectedTDLibFiles)
        && EmbeddedPayload.Files.All(file =>
            !file.RelativePath.StartsWith(
                $"BaseLib{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase)),
        "Embedded dependency payload is not exactly TDLib DLL, manifest, and license.");

    var tdLibManifestFile = tdLibPayloadFiles.Single(file =>
        file.RelativePath.EndsWith(
            Path.Combine("TDLib", "TDLib.json"),
            StringComparison.OrdinalIgnoreCase));
    using (var tdLibManifest = JsonDocument.Parse(
               EmbeddedPayload.Read(tdLibManifestFile)))
    {
        Assert(
            tdLibManifest.RootElement.GetProperty("id").GetString() == "TDLib"
            && tdLibManifest.RootElement.GetProperty("version").GetString()
                == "0.1.0"
            && !tdLibManifest.RootElement.GetProperty("has_pck").GetBoolean(),
            "Embedded TDLib manifest identity or no-PCK contract is incorrect.");
    }

    var dllFile = EmbeddedPayload.Files.Single(file =>
        file.RelativePath.EndsWith(
            Path.Combine("TDBank", "TDBank.dll"),
            StringComparison.OrdinalIgnoreCase));
    var embeddedDllPath = Path.Combine(testRoot, "embedded-version-check.dll");
    File.WriteAllBytes(embeddedDllPath, EmbeddedPayload.Read(dllFile));
    Assert(
        AssemblyName.GetAssemblyName(embeddedDllPath).Version
            == new Version(0, 1, 4, 0),
        "Embedded TD Bank DLL assembly version is not 0.1.4.0.");
    var tdBankReferences = Assembly.Load(EmbeddedPayload.Read(dllFile))
        .GetReferencedAssemblies();
    Assert(
        tdBankReferences.Any(reference => reference.Name == "TDLib")
        && tdBankReferences.All(reference => reference.Name != "BaseLib"),
        "Embedded TD Bank DLL does not reference TDLib exclusively.");

    var tdLibDllFile = tdLibPayloadFiles.Single(file =>
        file.RelativePath.EndsWith(
            Path.Combine("TDLib", "TDLib.dll"),
            StringComparison.OrdinalIgnoreCase));
    var embeddedTDLibDllPath = Path.Combine(
        testRoot,
        "embedded-tdlib-version-check.dll");
    File.WriteAllBytes(
        embeddedTDLibDllPath,
        EmbeddedPayload.Read(tdLibDllFile));
    Assert(
        AssemblyName.GetAssemblyName(embeddedTDLibDllPath).Name == "TDLib"
        && AssemblyName.GetAssemblyName(embeddedTDLibDllPath).Version
            == new Version(0, 1, 0, 0)
        && Assembly.Load(EmbeddedPayload.Read(tdLibDllFile))
            .GetReferencedAssemblies()
            .All(reference => reference.Name != "BaseLib"),
        "Embedded TDLib DLL identity or BaseLib isolation is incorrect.");
    Assert(
        typeof(TransactionInstaller).Assembly.GetName().Version
            == new Version(0, 1, 4, 0),
        "Installer assembly version is not 0.1.4.0.");
    Assert(
        string.Equals(
            typeof(TransactionInstaller).Assembly
                .GetCustomAttribute<AssemblyCompanyAttribute>()
                ?.Company,
            "cnj lab",
            StringComparison.Ordinal),
        "Installer's user-visible company metadata must spell cnj lab in lowercase.");
}

static void AssertAllLocalizedText()
{
    foreach (var text in Enum.GetValues<UiText>())
    {
        var chinese = InstallerStrings.Get(UiLanguage.ZhCn, text);
        var english = InstallerStrings.Get(UiLanguage.En, text);
        Assert(!string.IsNullOrWhiteSpace(chinese), $"Missing Chinese text: {text}");
        Assert(!string.IsNullOrWhiteSpace(english), $"Missing English text: {text}");
        Assert(
            !ContainsHanCharacter(english),
            $"English text unexpectedly contains a Chinese character: {text}");
        Assert(
            !chinese.Contains("CNJ", StringComparison.Ordinal)
            && !english.Contains("CNJ", StringComparison.Ordinal),
            $"User-visible installer text still contains uppercase CNJ: {text}");
    }

    foreach (var stage in Enum.GetValues<InstallStage>())
    {
        var chinese = InstallerStrings.FormatProgress(UiLanguage.ZhCn, stage);
        var english = InstallerStrings.FormatProgress(UiLanguage.En, stage);
        Assert(!string.IsNullOrWhiteSpace(chinese), $"Missing Chinese progress text: {stage}");
        Assert(!string.IsNullOrWhiteSpace(english), $"Missing English progress text: {stage}");
        Assert(!ContainsHanCharacter(english), $"English progress contains Chinese: {stage}");
    }

    foreach (var stage in Enum.GetValues<UninstallStage>())
    {
        var chinese = InstallerStrings.FormatProgress(UiLanguage.ZhCn, stage);
        var english = InstallerStrings.FormatProgress(UiLanguage.En, stage);
        Assert(!string.IsNullOrWhiteSpace(chinese), $"Missing Chinese uninstall progress: {stage}");
        Assert(!string.IsNullOrWhiteSpace(english), $"Missing English uninstall progress: {stage}");
        Assert(
            !ContainsHanCharacter(english),
            $"English uninstall progress contains Chinese: {stage}");
    }

    Assert(
        InstallerStrings.Get(UiLanguage.ZhCn, UiText.PayloadSummary).Contains("v0.1"),
        "Chinese payload summary does not advertise TD Bank v0.1.");
    Assert(
        InstallerStrings.Get(UiLanguage.En, UiText.PayloadSummary).Contains("v0.1"),
        "English payload summary does not advertise TD Bank v0.1.");
    Assert(
        InstallerStrings.Get(UiLanguage.ZhCn, UiText.PayloadSummary).Contains(
            "本 Setup 不安装任何 Windows 软件，只把以下两个 Mod 放入游戏的 mods 文件夹",
            StringComparison.Ordinal),
        "Chinese payload summary does not explain the Setup's limited install scope.");
    Assert(
        InstallerStrings.Get(UiLanguage.En, UiText.PayloadSummary).Contains(
            "This Setup installs no Windows software",
            StringComparison.Ordinal)
        && InstallerStrings.Get(UiLanguage.En, UiText.PayloadSummary).Contains(
            "the following two mods in the game’s mods folder",
            StringComparison.Ordinal),
        "English payload summary does not explain the Setup's limited install scope.");
    Assert(
        InstallerStrings.Get(UiLanguage.ZhCn, UiText.Disclaimer).Contains(
            "保证和任何叫 TD 的银行没有关联。",
            StringComparison.Ordinal)
        && InstallerStrings.Get(UiLanguage.En, UiText.Disclaimer).Contains(
            "no connection with any bank called TD",
            StringComparison.Ordinal),
        "The bilingual TD-bank disclaimer is missing or outdated.");
    var chinesePayloadSummary =
        InstallerStrings.Get(UiLanguage.ZhCn, UiText.PayloadSummary);
    var englishPayloadSummary =
        InstallerStrings.Get(UiLanguage.En, UiText.PayloadSummary);
    Assert(
        chinesePayloadSummary.Contains(
            "给《杀戮尖塔 2》增加一个银行，它会改变游戏玩法。",
            StringComparison.Ordinal)
        && chinesePayloadSummary.Contains(
            "TD Bank 专用的存档与多人同步组件",
            StringComparison.Ordinal)
        && chinesePayloadSummary.Contains(
            "不会替换或影响 BaseLib",
            StringComparison.Ordinal)
        && chinesePayloadSummary.Contains(
            "TDLib（MIT） 的存档扩展代码改编自 BaseLib（MIT）",
            StringComparison.Ordinal),
        "Chinese payload summary does not explain TD Bank and TDLib as approved.");
    Assert(
        englishPayloadSummary.Contains(
            "Adds a bank to Slay the Spire 2 and changes gameplay.",
            StringComparison.Ordinal)
        && englishPayloadSummary.Contains(
            "dedicated save and multiplayer-sync component",
            StringComparison.Ordinal)
        && englishPayloadSummary.Contains(
            "does not replace or affect BaseLib",
            StringComparison.Ordinal)
        && englishPayloadSummary.Contains(
            "adapted from BaseLib under the MIT License",
            StringComparison.Ordinal),
        "English payload summary does not explain TD Bank and TDLib.");
    Assert(
        !InstallerStrings.Get(
                UiLanguage.ZhCn,
                UiText.PayloadSummaryInstalled)
            .Contains(
                "需要回到普通档时，会先备份并核验存档交接；失败就保留 Mod。",
                StringComparison.Ordinal)
        && !InstallerStrings.Get(
                UiLanguage.En,
                UiText.PayloadSummaryInstalled)
            .Contains(
                "If a vanilla handoff is needed",
                StringComparison.Ordinal),
        "The removed save-handoff sentence is still present in the installed summary.");
    Assert(
        InstallerStrings.Get(UiLanguage.ZhCn, UiText.SuccessDialogBody).Contains("同意并开户")
        && InstallerStrings.Get(UiLanguage.ZhCn, UiText.SuccessDialogBody).Contains("被迫同意并开户")
        && InstallerStrings.Get(UiLanguage.En, UiText.SuccessDialogBody).Contains("Agree and open")
        && InstallerStrings.Get(UiLanguage.En, UiText.SuccessDialogBody).Contains("Forced to agree and open"),
        "Install-success copy does not explain both first-time in-game account-opening actions.");
    Assert(
        InstallerStrings.Get(UiLanguage.ZhCn, UiText.UninstallConfirmBody).Contains(
            "mods\\TDBank 与 mods\\TDLib",
            StringComparison.Ordinal)
        && InstallerStrings.Get(UiLanguage.ZhCn, UiText.UninstallConfirmBody).Contains(
            "绝不会清空其他 Mod",
            StringComparison.Ordinal)
        && InstallerStrings.Get(UiLanguage.ZhCn, UiText.UninstallConfirmBody).Contains(
            "会先完整备份",
            StringComparison.Ordinal),
        "Chinese uninstall confirmation does not describe the bounded mod and save scope.");
    Assert(
        InstallerStrings.Get(UiLanguage.En, UiText.UninstallConfirmBody).Contains(
            "mods\\TDBank and mods\\TDLib",
            StringComparison.Ordinal)
        && InstallerStrings.Get(UiLanguage.En, UiText.UninstallConfirmBody).Contains(
            "Other mods are never cleared",
            StringComparison.Ordinal)
        && InstallerStrings.Get(UiLanguage.En, UiText.UninstallConfirmBody).Contains(
            "creates a full backup",
            StringComparison.Ordinal),
        "English uninstall confirmation does not describe the bounded mod and save scope.");

    var tdLibReceiptCases = new[]
    {
        (
            TDLibUninstallDisposition.RemovedManagedToBackup,
            Chinese: "文件完全一致，已安全移出",
            English: "exact file match",
            ForbiddenChinese: "已保留",
            ForbiddenEnglish: "was preserved"),
        (
            TDLibUninstallDisposition.PreservedUnmanaged,
            Chinese: "为防止误删已保留",
            English: "so it was preserved",
            ForbiddenChinese: "已安全移出",
            ForbiddenEnglish: "was safely moved"),
        (
            TDLibUninstallDisposition.AlreadyAbsent,
            Chinese: "本来就不存在",
            English: "already absent",
            ForbiddenChinese: "已安全移出",
            ForbiddenEnglish: "was safely moved"),
    };
    foreach (var receiptCase in tdLibReceiptCases)
    {
        var detailKey = InstallerStrings.GetTDLibUninstallText(
            receiptCase.Item1);
        var chineseDetail = InstallerStrings.Get(UiLanguage.ZhCn, detailKey);
        var englishDetail = InstallerStrings.Get(UiLanguage.En, detailKey);
        Assert(
            chineseDetail.Contains(receiptCase.Chinese, StringComparison.Ordinal)
            && !chineseDetail.Contains(
                receiptCase.ForbiddenChinese,
                StringComparison.Ordinal),
            $"Chinese TDLib uninstall receipt is inaccurate: {receiptCase.Item1}");
        Assert(
            englishDetail.Contains(receiptCase.English, StringComparison.Ordinal)
            && !englishDetail.Contains(
                receiptCase.ForbiddenEnglish,
                StringComparison.Ordinal),
            $"English TDLib uninstall receipt is inaccurate: {receiptCase.Item1}");

        var chineseSuccess = InstallerStrings.Get(
            UiLanguage.ZhCn,
            UiText.UninstallSuccessDialogBody,
            detailKey,
            @"C:\Recovery");
        var englishSuccess = InstallerStrings.Get(
            UiLanguage.En,
            UiText.UninstallSuccessDialogBody,
            detailKey,
            @"C:\Recovery");
        var chineseAlreadyAbsent = InstallerStrings.Get(
            UiLanguage.ZhCn,
            UiText.UninstallAlreadyAbsentDialogBody,
            detailKey);
        var englishAlreadyAbsent = InstallerStrings.Get(
            UiLanguage.En,
            UiText.UninstallAlreadyAbsentDialogBody,
            detailKey);
        var chineseStatus = InstallerStrings.Get(
            UiLanguage.ZhCn,
            UiText.StatusUninstallSuccess,
            detailKey,
            @"C:\Recovery");
        var englishStatus = InstallerStrings.Get(
            UiLanguage.En,
            UiText.StatusUninstallSuccess,
            detailKey,
            @"C:\Recovery");
        var chineseAbsentStatus = InstallerStrings.Get(
            UiLanguage.ZhCn,
            UiText.StatusUninstallAlreadyAbsent,
            detailKey);
        var englishAbsentStatus = InstallerStrings.Get(
            UiLanguage.En,
            UiText.StatusUninstallAlreadyAbsent,
            detailKey);
        Assert(
            chineseSuccess.Contains(chineseDetail, StringComparison.Ordinal)
            && chineseAlreadyAbsent.Contains(chineseDetail, StringComparison.Ordinal)
            && chineseStatus.Contains(chineseDetail, StringComparison.Ordinal)
            && chineseAbsentStatus.Contains(chineseDetail, StringComparison.Ordinal),
            $"Chinese uninstall receipt ignored TDLib disposition: {receiptCase.Item1}");
        Assert(
            englishSuccess.Contains(englishDetail, StringComparison.Ordinal)
            && englishAlreadyAbsent.Contains(englishDetail, StringComparison.Ordinal)
            && englishStatus.Contains(englishDetail, StringComparison.Ordinal)
            && englishAbsentStatus.Contains(englishDetail, StringComparison.Ordinal),
            $"English uninstall receipt ignored TDLib disposition: {receiptCase.Item1}");
    }

    var lockedError = new InstallerOperationException(
        InstallerErrorCode.FileLocked,
        targetPath: @"C:\Game\mods\TDBank\TDBank.dll");
    Assert(
        InstallerStrings.FormatError(UiLanguage.ZhCn, lockedError).Contains("占用"),
        "Chinese locked-file error was not localized.");
    Assert(
        InstallerStrings.FormatError(UiLanguage.En, lockedError).Contains("in use"),
        "English locked-file error was not localized.");

    var invalid = new GameValidation(
        false,
        false,
        null,
        null,
        null,
        ValidationStatus.MissingGameFiles);
    var validationError = new InstallerOperationException(
        InstallerErrorCode.ValidationRejected,
        validation: invalid);
    Assert(
        InstallerStrings.FormatError(UiLanguage.ZhCn, validationError).Contains("不像"),
        "Chinese validation error was not localized.");
    Assert(
        InstallerStrings.FormatError(UiLanguage.En, validationError).Contains("does not look"),
        "English validation error was not localized.");

    var unrecognized = new InstallerOperationException(
        InstallerErrorCode.UnrecognizedTDBankDirectory,
        targetPath: @"C:\Game\mods\TDBank");
    Assert(
        InstallerStrings.FormatError(UiLanguage.ZhCn, unrecognized).Contains("身份不明"),
        "Chinese unrecognized-uninstall error was not localized.");
    Assert(
        InstallerStrings.FormatError(UiLanguage.En, unrecognized).Contains("unrecognized"),
        "English unrecognized-uninstall error was not localized.");
}

static string CreateUninstallSaveFixture(string saveRoot, string sentinel)
{
    File.WriteAllText(
        CreateParent(Path.Combine(
            saveRoot,
            "steam",
            "76561198000000999",
            "profile1",
            "saves",
            "progress.save")),
        $$"""{"schema_version":22,"unique_id":"{{sentinel}}-VANILLA","ascension":10}""");
    File.WriteAllText(
        CreateParent(Path.Combine(
            saveRoot,
            "steam",
            "76561198000000999",
            "modded",
            "profile1",
            "saves",
            "progress.save")),
        $$"""{"schema_version":22,"unique_id":"{{sentinel}}-MODDED","ascension":10}""");
    return saveRoot;
}

static void WriteEmbeddedTDLib(string game)
{
    var mods = Path.Combine(game, "mods");
    foreach (var file in EmbeddedPayload.Files.Where(file => file.IsTDLib))
    {
        var target = Path.Combine(mods, file.RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllBytes(target, EmbeddedPayload.Read(file));
    }
}

static void RewriteInstallStateAsLegacy(
    string game,
    TDLibInstallAction action)
{
    var statePath = Path.Combine(game, "mods", "TDBank", "install-state.json");
    var state = JsonNode.Parse(File.ReadAllText(statePath))!.AsObject();
    state.Remove("tdLibOwnership");
    state["tdLibAction"] = action.ToString();
    File.WriteAllText(
        statePath,
        state.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
}

static void CorruptTDLibOwnershipProof(string game)
{
    var statePath = Path.Combine(game, "mods", "TDBank", "install-state.json");
    var state = JsonNode.Parse(File.ReadAllText(statePath))!.AsObject();
    var ownership = state["tdLibOwnership"]!.AsObject();
    var files = ownership["payloadFiles"]!.AsArray();
    files[0]!["sha256"] = new string('0', 64);
    File.WriteAllText(
        statePath,
        state.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
}

static void RunUninstallMatrix(string root)
{
    Directory.CreateDirectory(root);

    var game = CreateFakeGame(Path.Combine(root, "success-game"), "v0.109.1");
    var mods = Path.Combine(game, "mods");
    var ordinaryBaseLib = Path.Combine(mods, "BaseLib");
    Directory.CreateDirectory(ordinaryBaseLib);
    File.WriteAllText(
        Path.Combine(ordinaryBaseLib, "BaseLib.json"),
        """{"id":"BaseLib","version":"v9.9.9","has_dll":true,"has_pck":true}""");
    var ordinaryBaseLibDll = Path.Combine(
        ordinaryBaseLib,
        "BaseLib.dll");
    File.WriteAllBytes(ordinaryBaseLibDll, [0x42, 0x41, 0x53, 0x45]);
    File.WriteAllBytes(
        Path.Combine(ordinaryBaseLib, "BaseLib.pck"),
        [0x4C, 0x49, 0x42]);
    File.WriteAllText(
        Path.Combine(ordinaryBaseLib, "user-owned-sentinel.txt"),
        "ordinary BaseLib must never be inspected or changed");
    var ordinaryBaseLibBefore = SnapshotTreeExact(ordinaryBaseLib);

    var saveRoot = Path.Combine(root, "success-saves");
    File.WriteAllText(
        CreateParent(Path.Combine(
            saveRoot,
            "steam",
            "76561198000000100",
            "profile1",
            "saves",
            "progress.save")),
        """{"schema_version":22,"unique_id":"UNINSTALL-VANILLA"}""");
    File.WriteAllText(
        CreateParent(Path.Combine(
            saveRoot,
            "steam",
            "76561198000000100",
            "modded",
            "profile1",
            "saves",
            "progress.save")),
        """{"schema_version":22,"unique_id":"UNINSTALL-MODDED"}""");
    using (var lockedOrdinaryBaseLib = new FileStream(
               ordinaryBaseLibDll,
               FileMode.Open,
               FileAccess.ReadWrite,
               FileShare.None))
    {
        _ = TransactionInstaller.Install(game, saveRoot);
    }

    var tdTarget = Path.Combine(mods, "TDBank");
    var baseTarget = Path.Combine(mods, "TDLib");
    var otherMod = Path.Combine(mods, "SomeOtherMod");
    Directory.CreateDirectory(otherMod);
    File.WriteAllText(
        Path.Combine(otherMod, "do-not-touch.txt"),
        "unrelated mod");
    var customArtwork = Path.Combine(
        tdTarget,
        "Assets",
        "friend-uninstall-art.txt");
    File.WriteAllText(customArtwork, "custom artwork must remain in recovery backup");

    Assert(
        TransactionUninstaller.IsInstalled(game),
        "Installed TD Bank was not detected for uninstall.");
    AssertUiDetectsInstalledGame(game);

    var savesBefore = SnapshotTreeExact(saveRoot);
    var baseBefore = SnapshotTreeExact(baseTarget);
    var otherModBefore = SnapshotTreeExact(otherMod);
    var uninstallStages = new List<UninstallStage>();
    UninstallResult removed;
    using (var lockedOrdinaryBaseLib = new FileStream(
               ordinaryBaseLibDll,
               FileMode.Open,
               FileAccess.ReadWrite,
               FileShare.None))
    {
        removed = TransactionUninstaller.Uninstall(
            game,
            new RecordingProgress<UninstallStage>(uninstallStages.Add));
    }

    Assert(removed.Removed, "Successful uninstall did not report removal.");
    Assert(removed.RemovedTDLib, "Setup-managed TDLib was not reported as removed.");
    Assert(!Directory.Exists(tdTarget), "TD Bank remained inside the Mods directory.");
    Assert(!Directory.Exists(baseTarget), "Setup-managed TDLib remained inside Mods.");
    Assert(
        uninstallStages.SequenceEqual(
        [
            UninstallStage.Inventory,
            UninstallStage.MoveTDBank,
            UninstallStage.FinalAudit,
        ]),
        "Uninstall did not report the expected progress stages.");
    Assert(
        !IsPathUnder(removed.BackupDirectory, mods),
        "Uninstall recovery backup remained inside the recursive mod scan tree.");
    Assert(
        File.ReadAllText(Path.Combine(
            removed.BackupDirectory,
            "TDBank-Uninstalled",
            "Assets",
            "friend-uninstall-art.txt"))
            == "custom artwork must remain in recovery backup",
        "Uninstall recovery backup lost a user-customized TD Bank file.");
    AssertTreeExactlyUnchanged(
        "managed TDLib recovery backup",
        baseBefore,
        SnapshotTreeExact(Path.Combine(
            removed.BackupDirectory,
            "TDLib-Uninstalled")));
    AssertTreeExactlyUnchanged("save tree", savesBefore, SnapshotTreeExact(saveRoot));
    AssertTreeExactlyUnchanged("unrelated mod", otherModBefore, SnapshotTreeExact(otherMod));
    AssertTreeExactlyUnchanged(
        "ordinary BaseLib",
        ordinaryBaseLibBefore,
        SnapshotTreeExact(ordinaryBaseLib));
    Assert(
        !TransactionUninstaller.IsInstalled(game),
        "TD Bank still reports installed after successful uninstall.");

    var uninstallBackupParent = Path.Combine(
        game,
        ".cnj-tower-debt-uninstall-backups");
    var backupCount = Directory.EnumerateDirectories(
        uninstallBackupParent,
        "*",
        SearchOption.TopDirectoryOnly).Count();
    var absent = TransactionUninstaller.Uninstall(game);
    Assert(
        absent.Disposition == UninstallDisposition.AlreadyAbsent,
        "A repeated uninstall was not an idempotent AlreadyAbsent result.");
    Assert(
        Directory.EnumerateDirectories(
            uninstallBackupParent,
            "*",
            SearchOption.TopDirectoryOnly).Count() == backupCount,
        "A repeated uninstall created another backup directory.");
    AssertTreeExactlyUnchanged(
        "save tree after repeated uninstall",
        savesBefore,
        SnapshotTreeExact(saveRoot));

    var legacyBaseLibGame = CreateFakeGame(
        Path.Combine(root, "legacy-baselib-metadata-game"),
        "v0.109.1");
    var legacyMods = Path.Combine(legacyBaseLibGame, "mods");
    var legacyTDBank = Path.Combine(legacyMods, "TDBank");
    var legacyOrdinaryBaseLib = Path.Combine(legacyMods, "BaseLib");
    Directory.CreateDirectory(legacyTDBank);
    Directory.CreateDirectory(legacyOrdinaryBaseLib);
    File.WriteAllText(
        Path.Combine(legacyTDBank, "TDBank.json"),
        """{"id":"TDBank","version":"v0.0.9","has_dll":true}""");
    File.WriteAllBytes(
        Path.Combine(legacyTDBank, "TDBank.dll"),
        [0x54, 0x44]);
    File.WriteAllText(
        Path.Combine(legacyTDBank, "install-state.json"),
        """
        {
          "installer": "CNJ Tower Debt Setup",
          "installerVersion": "v0.0.9",
          "baseLibAction": "Install",
          "baseLibOwnership": {
            "schemaVersion": 1,
            "managedBySetup": true,
            "actionAtThisInstall": "Install",
            "payloadVersion": "3.3.8",
            "payloadFiles": []
          }
        }
        """);
    File.WriteAllText(
        Path.Combine(legacyOrdinaryBaseLib, "BaseLib.json"),
        """{"id":"BaseLib","version":"v3.3.8","has_dll":true,"has_pck":true}""");
    var legacyOrdinaryBaseLibDll = Path.Combine(
        legacyOrdinaryBaseLib,
        "BaseLib.dll");
    File.WriteAllBytes(
        legacyOrdinaryBaseLibDll,
        [0x4C, 0x45, 0x47, 0x41, 0x43, 0x59]);
    File.WriteAllBytes(
        Path.Combine(legacyOrdinaryBaseLib, "BaseLib.pck"),
        [0x50, 0x43, 0x4B]);
    var legacyOrdinaryBaseLibBefore =
        SnapshotTreeExact(legacyOrdinaryBaseLib);
    UninstallResult legacyBaseLibRemoval;
    using (var lockedLegacyBaseLib = new FileStream(
               legacyOrdinaryBaseLibDll,
               FileMode.Open,
               FileAccess.ReadWrite,
               FileShare.None))
    {
        legacyBaseLibRemoval =
            TransactionUninstaller.Uninstall(legacyBaseLibGame);
    }
    Assert(
        legacyBaseLibRemoval.Removed
        && legacyBaseLibRemoval.TDLibDisposition
            == TDLibUninstallDisposition.AlreadyAbsent,
        "Legacy BaseLib metadata was incorrectly treated as TDLib ownership.");
    AssertTreeExactlyUnchanged(
        "legacy ordinary BaseLib",
        legacyOrdinaryBaseLibBefore,
        SnapshotTreeExact(legacyOrdinaryBaseLib));

    var preinstalledExactGame = CreateFakeGame(
        Path.Combine(root, "preinstalled-exact-baselib-game"),
        "v0.109.1");
    var preinstalledExactSaves = CreateUninstallSaveFixture(
        Path.Combine(root, "preinstalled-exact-baselib-saves"),
        "PREINSTALLED-EXACT");
    WriteEmbeddedTDLib(preinstalledExactGame);
    var preinstalledExactBase = Path.Combine(
        preinstalledExactGame,
        "mods",
        "TDLib");
    var preinstalledExactBefore = SnapshotTreeExact(preinstalledExactBase);
    var exactInstall = TransactionInstaller.Install(
        preinstalledExactGame,
        preinstalledExactSaves);
    Assert(
        exactInstall.TDLibAction == TDLibInstallAction.PreserveExact,
        "A preinstalled exact TDLib was not preserved during install.");
    AssertTDLibOwnershipState(
        preinstalledExactGame,
        expectedManaged: false,
        expectedAction: TDLibInstallAction.PreserveExact);
    var preinstalledExactSaveBefore = SnapshotTreeExact(preinstalledExactSaves);
    var preinstalledExactRemoval =
        TransactionUninstaller.Uninstall(preinstalledExactGame);
    Assert(
        !preinstalledExactRemoval.RemovedTDLib
        && preinstalledExactRemoval.TDLibDisposition
            == TDLibUninstallDisposition.PreservedUnmanaged,
        "A user-preinstalled exact TDLib was treated as Setup-owned.");
    AssertTreeExactlyUnchanged(
        "preinstalled exact TDLib",
        preinstalledExactBefore,
        SnapshotTreeExact(preinstalledExactBase));
    AssertTreeExactlyUnchanged(
        "preinstalled exact TDLib saves",
        preinstalledExactSaveBefore,
        SnapshotTreeExact(preinstalledExactSaves));

    var preinstalledNewerGame = CreateFakeGame(
        Path.Combine(root, "preinstalled-newer-baselib-game"),
        "v0.109.1");
    var preinstalledNewerSaves = CreateUninstallSaveFixture(
        Path.Combine(root, "preinstalled-newer-baselib-saves"),
        "PREINSTALLED-NEWER");
    var preinstalledNewerBase = Path.Combine(
        preinstalledNewerGame,
        "mods",
        "TDLib");
    Directory.CreateDirectory(preinstalledNewerBase);
    File.WriteAllText(
        Path.Combine(preinstalledNewerBase, "TDLib.json"),
        """{"id":"TDLib","version":"v9.0.0","has_dll":true,"has_pck":true}""");
    File.WriteAllBytes(Path.Combine(preinstalledNewerBase, "TDLib.dll"), [9, 1, 2]);
    File.WriteAllBytes(Path.Combine(preinstalledNewerBase, "TDLib.pck"), [9, 3, 4]);
    var preinstalledNewerBefore = SnapshotTreeExact(preinstalledNewerBase);
    var newerInstall = TransactionInstaller.Install(
        preinstalledNewerGame,
        preinstalledNewerSaves);
    Assert(
        newerInstall.TDLibAction == TDLibInstallAction.PreserveNewer,
        "A preinstalled newer TDLib was not preserved during install.");
    AssertTDLibOwnershipState(
        preinstalledNewerGame,
        expectedManaged: false,
        expectedAction: TDLibInstallAction.PreserveNewer);
    var preinstalledNewerSaveBefore = SnapshotTreeExact(preinstalledNewerSaves);
    var preinstalledNewerRemoval =
        TransactionUninstaller.Uninstall(preinstalledNewerGame);
    Assert(
        !preinstalledNewerRemoval.RemovedTDLib,
        "A user-preinstalled newer TDLib was removed.");
    AssertTreeExactlyUnchanged(
        "preinstalled newer TDLib",
        preinstalledNewerBefore,
        SnapshotTreeExact(preinstalledNewerBase));
    AssertTreeExactlyUnchanged(
        "preinstalled newer TDLib saves",
        preinstalledNewerSaveBefore,
        SnapshotTreeExact(preinstalledNewerSaves));

    var modifiedBaseGame = CreateFakeGame(
        Path.Combine(root, "modified-managed-baselib-game"),
        "v0.109.1");
    var modifiedBaseSaves = CreateUninstallSaveFixture(
        Path.Combine(root, "modified-managed-baselib-saves"),
        "MODIFIED-MANAGED");
    _ = TransactionInstaller.Install(modifiedBaseGame, modifiedBaseSaves);
    var modifiedBase = Path.Combine(modifiedBaseGame, "mods", "TDLib");
    File.WriteAllText(
        Path.Combine(modifiedBase, "user-added-sentinel.txt"),
        "an extra file makes ownership unprovable");
    var modifiedBaseBefore = SnapshotTreeExact(modifiedBase);
    var modifiedBaseSaveBefore = SnapshotTreeExact(modifiedBaseSaves);
    var modifiedBaseRemoval = TransactionUninstaller.Uninstall(modifiedBaseGame);
    Assert(
        !modifiedBaseRemoval.RemovedTDLib,
        "A modified TDLib was removed despite no exact-payload proof.");
    AssertTreeExactlyUnchanged(
        "modified managed TDLib",
        modifiedBaseBefore,
        SnapshotTreeExact(modifiedBase));
    AssertTreeExactlyUnchanged(
        "modified managed TDLib saves",
        modifiedBaseSaveBefore,
        SnapshotTreeExact(modifiedBaseSaves));

    var updatedBaseGame = CreateFakeGame(
        Path.Combine(root, "updated-managed-baselib-game"),
        "v0.109.1");
    var updatedBaseSaves = CreateUninstallSaveFixture(
        Path.Combine(root, "updated-managed-baselib-saves"),
        "UPDATED-MANAGED");
    _ = TransactionInstaller.Install(updatedBaseGame, updatedBaseSaves);
    var updatedBase = Path.Combine(updatedBaseGame, "mods", "TDLib");
    File.WriteAllText(
        Path.Combine(updatedBase, "TDLib.json"),
        """{"id":"TDLib","version":"v9.0.0","has_dll":true,"has_pck":true}""");
    File.WriteAllBytes(Path.Combine(updatedBase, "TDLib.dll"), [4, 5, 6, 7]);
    File.WriteAllBytes(Path.Combine(updatedBase, "TDLib.pck"), [8, 9, 10]);
    var updatedBaseBefore = SnapshotTreeExact(updatedBase);
    var updatedBaseSaveBefore = SnapshotTreeExact(updatedBaseSaves);
    var updatedBaseRemoval = TransactionUninstaller.Uninstall(updatedBaseGame);
    Assert(
        !updatedBaseRemoval.RemovedTDLib,
        "A TDLib updated after Setup install was removed.");
    AssertTreeExactlyUnchanged(
        "updated managed TDLib",
        updatedBaseBefore,
        SnapshotTreeExact(updatedBase));
    AssertTreeExactlyUnchanged(
        "updated managed TDLib saves",
        updatedBaseSaveBefore,
        SnapshotTreeExact(updatedBaseSaves));

    var unreadableBaseGame = CreateFakeGame(
        Path.Combine(root, "unreadable-managed-baselib-game"),
        "v0.109.1");
    var unreadableBaseSaves = CreateUninstallSaveFixture(
        Path.Combine(root, "unreadable-managed-baselib-saves"),
        "UNREADABLE-MANAGED");
    _ = TransactionInstaller.Install(unreadableBaseGame, unreadableBaseSaves);
    var unreadableBase = Path.Combine(unreadableBaseGame, "mods", "TDLib");
    var unreadableBaseBefore = SnapshotTreeExact(unreadableBase);
    var unreadableSaveBefore = SnapshotTreeExact(unreadableBaseSaves);
    using (var lockedBase = new FileStream(
               Path.Combine(unreadableBase, "TDLib.dll"),
               FileMode.Open,
               FileAccess.Read,
               FileShare.None))
    {
        var unreadableRemoval =
            TransactionUninstaller.Uninstall(unreadableBaseGame);
        Assert(
            !unreadableRemoval.RemovedTDLib,
            "TDLib was removed when its current hash could not be proven.");
    }
    AssertTreeExactlyUnchanged(
        "unreadable managed TDLib",
        unreadableBaseBefore,
        SnapshotTreeExact(unreadableBase));
    AssertTreeExactlyUnchanged(
        "unreadable managed TDLib saves",
        unreadableSaveBefore,
        SnapshotTreeExact(unreadableBaseSaves));

    var legacyCarryGame = CreateFakeGame(
        Path.Combine(root, "legacy-state-carry-forward-game"),
        "v0.109.1");
    var legacyCarrySaves = CreateUninstallSaveFixture(
        Path.Combine(root, "legacy-state-carry-forward-saves"),
        "LEGACY-CARRY");
    _ = TransactionInstaller.Install(legacyCarryGame, legacyCarrySaves);
    RewriteInstallStateAsLegacy(
        legacyCarryGame,
        TDLibInstallAction.Install);
    var legacyCarryReinstall =
        TransactionInstaller.Install(legacyCarryGame, legacyCarrySaves);
    Assert(
        legacyCarryReinstall.TDLibAction == TDLibInstallAction.PreserveExact,
        "Legacy TDLib was not exact during reinstall.");
    AssertTDLibOwnershipState(
        legacyCarryGame,
        expectedManaged: false,
        expectedAction: TDLibInstallAction.PreserveExact);
    var legacyCarrySaveBefore = SnapshotTreeExact(legacyCarrySaves);
    Assert(
        !TransactionUninstaller.Uninstall(legacyCarryGame).RemovedTDLib,
        "An action-only legacy TDLib state was incorrectly promoted to ownership.");
    AssertTreeExactlyUnchanged(
        "legacy ownership carry-forward saves",
        legacyCarrySaveBefore,
        SnapshotTreeExact(legacyCarrySaves));

    foreach (var legacyAction in new[]
             {
                 TDLibInstallAction.Install,
                 TDLibInstallAction.UpgradeOrRepair,
                 TDLibInstallAction.PreserveExact,
                 TDLibInstallAction.PreserveNewer,
             })
    {
        var legacyName = legacyAction.ToString().ToLowerInvariant();
        var legacyGame = CreateFakeGame(
            Path.Combine(root, $"legacy-state-{legacyName}-game"),
            "v0.109.1");
        var legacySaves = CreateUninstallSaveFixture(
            Path.Combine(root, $"legacy-state-{legacyName}-saves"),
            $"LEGACY-{legacyAction}");
        _ = TransactionInstaller.Install(legacyGame, legacySaves);
        RewriteInstallStateAsLegacy(legacyGame, legacyAction);
        var legacyBase = Path.Combine(legacyGame, "mods", "TDLib");
        var legacyBaseBefore = SnapshotTreeExact(legacyBase);
        var legacySaveBefore = SnapshotTreeExact(legacySaves);
        var legacyRemoval = TransactionUninstaller.Uninstall(legacyGame);
        Assert(
            !legacyRemoval.RemovedTDLib,
            $"Legacy {legacyAction} metadata was incorrectly treated as ownership.");
        AssertTreeExactlyUnchanged(
            $"legacy {legacyAction} preserved TDLib",
            legacyBaseBefore,
            SnapshotTreeExact(legacyBase));
        AssertTreeExactlyUnchanged(
            $"legacy {legacyAction} saves",
            legacySaveBefore,
            SnapshotTreeExact(legacySaves));
    }

    var corruptProofGame = CreateFakeGame(
        Path.Combine(root, "corrupt-ownership-proof-game"),
        "v0.109.1");
    var corruptProofSaves = CreateUninstallSaveFixture(
        Path.Combine(root, "corrupt-ownership-proof-saves"),
        "CORRUPT-PROOF");
    _ = TransactionInstaller.Install(corruptProofGame, corruptProofSaves);
    CorruptTDLibOwnershipProof(corruptProofGame);
    var corruptProofBase = Path.Combine(corruptProofGame, "mods", "TDLib");
    var corruptProofBaseBefore = SnapshotTreeExact(corruptProofBase);
    var corruptProofSaveBefore = SnapshotTreeExact(corruptProofSaves);
    var corruptProofRemoval = TransactionUninstaller.Uninstall(corruptProofGame);
    Assert(
        !corruptProofRemoval.RemovedTDLib,
        "TDLib was removed with a malformed ownership proof.");
    AssertTreeExactlyUnchanged(
        "corrupt ownership proof TDLib",
        corruptProofBaseBefore,
        SnapshotTreeExact(corruptProofBase));
    AssertTreeExactlyUnchanged(
        "corrupt ownership proof saves",
        corruptProofSaveBefore,
        SnapshotTreeExact(corruptProofSaves));

    _ = TransactionInstaller.Install(game, saveRoot);
    File.WriteAllText(
        Path.Combine(game, "release_info.json"),
        JsonSerializer.Serialize(new
        {
            commit = "future-public-beta",
            version = "v9.9.9",
            date = "2027-01-01",
            branch = "future",
            main_assembly_hash = 1,
        }));
    Assert(
        TransactionUninstaller.IsInstalled(game),
        "TD Bank on an unsupported future game version was not detected.");
    Assert(
        TransactionUninstaller.Uninstall(game).Removed,
        "TD Bank could not be removed after the game version changed.");

    var lockedGame = CreateFakeGame(Path.Combine(root, "locked-game"), "v0.109.1");
    var lockedSaves = Path.Combine(root, "locked-saves");
    _ = TransactionInstaller.Install(lockedGame, lockedSaves);
    var lockedTd = Path.Combine(lockedGame, "mods", "TDBank");
    var lockedSaveSnapshot = SnapshotTreeExact(lockedSaves);
    using (var lockedDll = new FileStream(
               Path.Combine(lockedTd, "TDBank.dll"),
               FileMode.Open,
               FileAccess.ReadWrite,
               FileShare.None))
    {
        var rejected = false;
        try
        {
            _ = TransactionUninstaller.Uninstall(lockedGame);
        }
        catch (InstallerOperationException exception)
        {
            rejected = exception.Code == InstallerErrorCode.FileLocked;
        }

        Assert(rejected, "Uninstall did not reject a locked TD Bank DLL.");
    }
    Assert(Directory.Exists(lockedTd), "Locked uninstall changed the TD Bank directory.");
    AssertTreeExactlyUnchanged(
        "locked-uninstall saves",
        lockedSaveSnapshot,
        SnapshotTreeExact(lockedSaves));

    var foreignGame = CreateFakeGame(Path.Combine(root, "foreign-game"), "v0.109.1");
    var foreignTd = Path.Combine(foreignGame, "mods", "TDBank");
    Directory.CreateDirectory(foreignTd);
    File.WriteAllText(
        Path.Combine(foreignTd, "TDBank.json"),
        """{"id":"SomeoneElsesFolder","version":"v1.0.0"}""");
    File.WriteAllText(Path.Combine(foreignTd, "sentinel.txt"), "must survive");
    var foreignRejected = false;
    try
    {
        _ = TransactionUninstaller.Uninstall(foreignGame);
    }
    catch (InstallerOperationException exception)
    {
        foreignRejected =
            exception.Code == InstallerErrorCode.UnrecognizedTDBankDirectory;
    }
    Assert(foreignRejected, "Uninstall accepted an unrecognized TDBank directory.");
    Assert(
        File.ReadAllText(Path.Combine(foreignTd, "sentinel.txt")) == "must survive",
        "Uninstall changed an unrecognized directory.");

    var stateOnlyGame = CreateFakeGame(
        Path.Combine(root, "install-state-only-game"),
        "v0.109.1");
    var stateOnlyTd = Path.Combine(stateOnlyGame, "mods", "TDBank");
    Directory.CreateDirectory(stateOnlyTd);
    File.WriteAllText(
        Path.Combine(stateOnlyTd, "TDBank.json"),
        "{ broken manifest");
    File.WriteAllText(
        Path.Combine(stateOnlyTd, "install-state.json"),
        """{"installer":"CNJ Tower Debt Setup","installerVersion":"v0.1"}""");
    File.WriteAllText(
        Path.Combine(stateOnlyTd, "state-only-sentinel.txt"),
        "recognized by install state");
    Assert(
        TransactionUninstaller.IsInstalled(stateOnlyGame),
        "A valid install state did not identify TD Bank when its manifest was damaged.");
    var stateOnlyResult = TransactionUninstaller.Uninstall(stateOnlyGame);
    Assert(
        File.ReadAllText(Path.Combine(
            stateOnlyResult.BackupDirectory,
            "TDBank-Uninstalled",
            "state-only-sentinel.txt")) == "recognized by install state",
        "Install-state-only uninstall lost its sentinel.");

    var unreadableVersionGame = CreateFakeGame(
        Path.Combine(root, "unreadable-version-game"),
        "v0.109.1");
    File.WriteAllText(
        Path.Combine(unreadableVersionGame, "release_info.json"),
        "{ not valid release info");
    var unreadableTd = Path.Combine(unreadableVersionGame, "mods", "TDBank");
    Directory.CreateDirectory(unreadableTd);
    File.WriteAllText(
        Path.Combine(unreadableTd, "TDBank.json"),
        """{"id":"TDBank","version":"v0.1"}""");
    Assert(
        GameValidator.Validate(unreadableVersionGame).Status
            == ValidationStatus.ReleaseInfoUnreadable,
        "Unreadable release-info fixture did not produce the expected status.");
    Assert(
        TransactionUninstaller.Uninstall(unreadableVersionGame).Removed,
        "A recognized TD Bank could not be removed when release_info.json was unreadable.");

    var rollbackGame = CreateFakeGame(Path.Combine(root, "rollback-game"), "v0.109.1");
    var rollbackSaves = Path.Combine(root, "rollback-saves");
    _ = TransactionInstaller.Install(rollbackGame, rollbackSaves);
    var rollbackTd = Path.Combine(rollbackGame, "mods", "TDBank");
    File.WriteAllText(
        Path.Combine(rollbackTd, "rollback-sentinel.txt"),
        "restore this exact install");
    var rollbackBase = Path.Combine(rollbackGame, "mods", "TDLib");
    var rollbackTdSnapshot = SnapshotTreeExact(rollbackTd);
    var rollbackBaseSnapshot = SnapshotTreeExact(rollbackBase);
    var rollbackSaveSnapshot = SnapshotTreeExact(rollbackSaves);
    TransactionUninstaller.AfterMoveTestHook = (_, _) =>
        throw new IOException("Injected post-move audit failure.");
    try
    {
        var rolledBack = false;
        try
        {
            _ = TransactionUninstaller.Uninstall(rollbackGame);
        }
        catch (InstallerOperationException exception)
        {
            rolledBack =
                exception.Code == InstallerErrorCode.UninstallRollbackCompleted;
        }

        Assert(rolledBack, "Injected uninstall failure did not complete rollback.");
    }
    finally
    {
        TransactionUninstaller.AfterMoveTestHook = null;
    }
    Assert(
        File.ReadAllText(Path.Combine(rollbackTd, "rollback-sentinel.txt"))
            == "restore this exact install",
        "Uninstall rollback did not restore the previous TD Bank directory.");
    AssertTreeExactlyUnchanged(
        "rollback TD Bank",
        rollbackTdSnapshot,
        SnapshotTreeExact(rollbackTd));
    AssertTreeExactlyUnchanged(
        "rollback managed TDLib",
        rollbackBaseSnapshot,
        SnapshotTreeExact(rollbackBase));
    AssertTreeExactlyUnchanged(
        "rollback saves",
        rollbackSaveSnapshot,
        SnapshotTreeExact(rollbackSaves));

    var failedRollbackGame = CreateFakeGame(
        Path.Combine(root, "failed-rollback-game"),
        "v0.109.1");
    _ = TransactionInstaller.Install(
        failedRollbackGame,
        Path.Combine(root, "failed-rollback-saves"));
    var failedRollbackBase = Path.Combine(
        failedRollbackGame,
        "mods",
        "TDLib");
    var failedRollbackBaseSnapshot = SnapshotTreeExact(failedRollbackBase);
    TransactionUninstaller.AfterMoveTestHook = (source, _) =>
    {
        Directory.CreateDirectory(source);
        File.WriteAllText(
            Path.Combine(source, "rollback-path-collision.txt"),
            "injected collision");
        throw new IOException("Injected failure with rollback-path collision.");
    };
    try
    {
        InstallerOperationException? rollbackFailure = null;
        try
        {
            _ = TransactionUninstaller.Uninstall(failedRollbackGame);
        }
        catch (InstallerOperationException exception)
        {
            rollbackFailure = exception;
        }

        Assert(
            rollbackFailure?.Code == InstallerErrorCode.UninstallRollbackFailed,
            "A blocked uninstall rollback did not report UninstallRollbackFailed.");
        Assert(
            Directory.Exists(Path.Combine(
                rollbackFailure!.BackupDirectory!,
                "TDBank-Uninstalled")),
            "A failed rollback did not retain the TD Bank recovery backup.");
        AssertTreeExactlyUnchanged(
            "TDLib after TD Bank rollback collision",
            failedRollbackBaseSnapshot,
            SnapshotTreeExact(failedRollbackBase));
    }
    finally
    {
        TransactionUninstaller.AfterMoveTestHook = null;
    }

    var auditFailureGame = CreateFakeGame(
        Path.Combine(root, "post-move-audit-game"),
        "v0.109.1");
    var auditFailureSaves = Path.Combine(root, "post-move-audit-saves");
    _ = TransactionInstaller.Install(auditFailureGame, auditFailureSaves);
    var auditFailureBase = Path.Combine(
        auditFailureGame,
        "mods",
        "TDLib");
    var auditFailureBaseSnapshot = SnapshotTreeExact(auditFailureBase);
    var auditSaveSnapshot = SnapshotTreeExact(auditFailureSaves);
    TransactionUninstaller.AfterMoveTestHook = (_, backup) =>
    {
        File.Delete(Path.Combine(backup, "Assets", "bank_logo.png"));
    };
    try
    {
        InstallerOperationException? auditFailure = null;
        try
        {
            _ = TransactionUninstaller.Uninstall(auditFailureGame);
        }
        catch (InstallerOperationException exception)
        {
            auditFailure = exception;
        }

        Assert(
            auditFailure?.Code == InstallerErrorCode.UninstallRollbackFailed,
            "A corrupted recovery backup passed the final audit.");
        Assert(
            Directory.Exists(Path.Combine(
                auditFailure!.BackupDirectory!,
                "TDBank-Uninstalled")),
            "A corrupted recovery backup was not retained for manual recovery.");
        AssertTreeExactlyUnchanged(
            "post-move-audit saves",
            auditSaveSnapshot,
            SnapshotTreeExact(auditFailureSaves));
        AssertTreeExactlyUnchanged(
            "post-move-audit TDLib",
            auditFailureBaseSnapshot,
            SnapshotTreeExact(auditFailureBase));
    }
    finally
    {
        TransactionUninstaller.AfterMoveTestHook = null;
    }

    RunTDLibUninstallReparseTests(root);
    RunUninstallReparseTests(root);
}

static void RunTDLibUninstallReparseTests(string root)
{
    var rootLinkGame = CreateFakeGame(
        Path.Combine(root, "baselib-root-reparse-game"),
        "v0.109.1");
    var rootLinkSaves = CreateUninstallSaveFixture(
        Path.Combine(root, "baselib-root-reparse-saves"),
        "BASELIB-ROOT-REPARSE");
    _ = TransactionInstaller.Install(rootLinkGame, rootLinkSaves);
    AssertTDLibOwnershipState(
        rootLinkGame,
        expectedManaged: true,
        expectedAction: TDLibInstallAction.Install);
    var rootLinkTd = Path.Combine(rootLinkGame, "mods", "TDBank");
    var rootLinkBase = Path.Combine(rootLinkGame, "mods", "TDLib");
    var rootLinkExternal = Path.Combine(
        root,
        "baselib-root-reparse-external");
    Directory.Move(rootLinkBase, rootLinkExternal);
    File.WriteAllText(
        Path.Combine(rootLinkExternal, "external-sentinel.txt"),
        "TDLib root link target must survive");
    try
    {
        CreateDirectoryLink(rootLinkBase, rootLinkExternal);
    }
    catch (Exception exception)
        when (exception is UnauthorizedAccessException
            or IOException
            or PlatformNotSupportedException)
    {
        Console.WriteLine(
            $"TDLib uninstall reparse tests skipped: {exception.GetType().Name}");
        if (!Directory.Exists(rootLinkBase))
        {
            Directory.Move(rootLinkExternal, rootLinkBase);
        }
        return;
    }

    var rootLinkSnapshot = SnapshotTreeWithoutFollowingReparsePoints(rootLinkBase);
    var rootExternalSnapshot = SnapshotTreeExact(rootLinkExternal);
    var rootLinkSaveSnapshot = SnapshotTreeExact(rootLinkSaves);
    var rootLinkRemoval = TransactionUninstaller.Uninstall(rootLinkGame);
    Assert(
        rootLinkRemoval.Removed && !rootLinkRemoval.RemovedTDLib,
        "A managed ownership claim followed and removed a TDLib root link.");
    Assert(
        rootLinkRemoval.TDLibDisposition
            == TDLibUninstallDisposition.PreservedUnmanaged,
        "A TDLib root link was not conservatively classified for preservation.");
    Assert(
        !Directory.Exists(rootLinkTd),
        "TD Bank was not safely removed while TDLib was a root link.");
    AssertTreeExactlyUnchanged(
        "TDLib root reparse entry",
        rootLinkSnapshot,
        SnapshotTreeWithoutFollowingReparsePoints(rootLinkBase));
    AssertTreeExactlyUnchanged(
        "TDLib root link external target",
        rootExternalSnapshot,
        SnapshotTreeExact(rootLinkExternal));
    AssertTreeExactlyUnchanged(
        "TDLib root link saves",
        rootLinkSaveSnapshot,
        SnapshotTreeExact(rootLinkSaves));
    Directory.Delete(rootLinkBase);

    var nestedLinkGame = CreateFakeGame(
        Path.Combine(root, "baselib-nested-reparse-game"),
        "v0.109.1");
    var nestedLinkSaves = CreateUninstallSaveFixture(
        Path.Combine(root, "baselib-nested-reparse-saves"),
        "BASELIB-NESTED-REPARSE");
    _ = TransactionInstaller.Install(nestedLinkGame, nestedLinkSaves);
    AssertTDLibOwnershipState(
        nestedLinkGame,
        expectedManaged: true,
        expectedAction: TDLibInstallAction.Install);
    var nestedLinkTd = Path.Combine(nestedLinkGame, "mods", "TDBank");
    var nestedLinkBase = Path.Combine(nestedLinkGame, "mods", "TDLib");
    var nestedExternal = Path.Combine(
        root,
        "baselib-nested-reparse-external");
    Directory.CreateDirectory(nestedExternal);
    File.WriteAllText(
        Path.Combine(nestedExternal, "external-sentinel.txt"),
        "nested TDLib link target must survive");
    var nestedLink = Path.Combine(nestedLinkBase, "linked-outside");
    try
    {
        CreateDirectoryLink(nestedLink, nestedExternal);
    }
    catch (Exception exception)
        when (exception is UnauthorizedAccessException
            or IOException
            or PlatformNotSupportedException)
    {
        Console.WriteLine(
            $"Nested TDLib uninstall reparse test skipped: {exception.GetType().Name}");
        return;
    }

    var nestedBaseSnapshot =
        SnapshotTreeWithoutFollowingReparsePoints(nestedLinkBase);
    var nestedExternalSnapshot = SnapshotTreeExact(nestedExternal);
    var nestedSaveSnapshot = SnapshotTreeExact(nestedLinkSaves);
    var nestedRemoval = TransactionUninstaller.Uninstall(nestedLinkGame);
    Assert(
        nestedRemoval.Removed && !nestedRemoval.RemovedTDLib,
        "A managed ownership claim followed and removed a nested TDLib link.");
    Assert(
        !Directory.Exists(nestedLinkTd),
        "TD Bank was not safely removed while TDLib contained a nested link.");
    AssertTreeExactlyUnchanged(
        "TDLib tree containing a nested reparse entry",
        nestedBaseSnapshot,
        SnapshotTreeWithoutFollowingReparsePoints(nestedLinkBase));
    AssertTreeExactlyUnchanged(
        "nested TDLib link external target",
        nestedExternalSnapshot,
        SnapshotTreeExact(nestedExternal));
    AssertTreeExactlyUnchanged(
        "nested TDLib link saves",
        nestedSaveSnapshot,
        SnapshotTreeExact(nestedLinkSaves));
    Directory.Delete(nestedLink);
}

static void RunUninstallReparseTests(string root)
{
    var external = Path.Combine(root, "reparse-external");
    Directory.CreateDirectory(external);
    File.WriteAllText(Path.Combine(external, "sentinel.txt"), "outside must survive");
    File.WriteAllText(
        Path.Combine(external, "TDBank.json"),
        """{"id":"TDBank","version":"v0.1"}""");

    var game = CreateFakeGame(Path.Combine(root, "reparse-game"), "v0.109.1");
    var mods = Path.Combine(game, "mods");
    Directory.CreateDirectory(mods);
    var tdLink = Path.Combine(mods, "TDBank");
    try
    {
        CreateDirectoryLink(tdLink, external);
    }
    catch (Exception exception)
        when (exception is UnauthorizedAccessException
            or IOException
            or PlatformNotSupportedException)
    {
        Console.WriteLine(
            $"TD Bank uninstall reparse tests skipped: {exception.GetType().Name}");
        return;
    }

    var rejected = false;
    try
    {
        _ = TransactionUninstaller.Uninstall(game);
    }
    catch (InstallerOperationException exception)
    {
        rejected = exception.Code == InstallerErrorCode.ReparsePoint;
    }
    Assert(rejected, "Uninstall followed a TDBank directory symlink.");
    Assert(
        File.ReadAllText(Path.Combine(external, "sentinel.txt")) == "outside must survive",
        "Uninstall changed the target of a TDBank directory symlink.");
    Directory.Delete(tdLink);

    var nestedGame = CreateFakeGame(Path.Combine(root, "nested-reparse-game"), "v0.109.1");
    _ = TransactionInstaller.Install(
        nestedGame,
        Path.Combine(root, "nested-reparse-saves"));
    var nestedLink = Path.Combine(
        nestedGame,
        "mods",
        "TDBank",
        "Assets",
        "linked-outside");
    CreateDirectoryLink(nestedLink, external);
    rejected = false;
    try
    {
        _ = TransactionUninstaller.Uninstall(nestedGame);
    }
    catch (InstallerOperationException exception)
    {
        rejected = exception.Code == InstallerErrorCode.ReparsePoint;
    }
    Assert(rejected, "Uninstall followed a nested symlink inside TD Bank.");
    Assert(
        File.ReadAllText(Path.Combine(external, "sentinel.txt")) == "outside must survive",
        "Uninstall changed the target of a nested TD Bank symlink.");
    Directory.Delete(nestedLink);

    var backupExternal = Path.Combine(root, "backup-reparse-external");
    Directory.CreateDirectory(backupExternal);
    File.WriteAllText(
        Path.Combine(backupExternal, "backup-sentinel.txt"),
        "backup link target must survive");
    var backupLinkGame = CreateFakeGame(
        Path.Combine(root, "backup-reparse-game"),
        "v0.109.1");
    var backupLinkTd = Path.Combine(backupLinkGame, "mods", "TDBank");
    Directory.CreateDirectory(backupLinkTd);
    File.WriteAllText(
        Path.Combine(backupLinkTd, "TDBank.json"),
        """{"id":"TDBank","version":"v0.1"}""");
    var backupLink = Path.Combine(
        backupLinkGame,
        ".cnj-tower-debt-uninstall-backups");
    CreateDirectoryLink(backupLink, backupExternal);
    rejected = false;
    try
    {
        _ = TransactionUninstaller.Uninstall(backupLinkGame);
    }
    catch (InstallerOperationException exception)
    {
        rejected = exception.Code == InstallerErrorCode.ReparsePoint;
    }
    Assert(rejected, "Uninstall accepted a recovery-backup directory symlink.");
    Assert(
        Directory.Exists(backupLinkTd),
        "Backup-directory reparse rejection changed TD Bank.");
    Assert(
        File.ReadAllText(Path.Combine(backupExternal, "backup-sentinel.txt"))
            == "backup link target must survive",
        "Backup-directory reparse rejection changed its external target.");
    Directory.Delete(backupLink);

    var linkedModsExternal = Path.Combine(root, "mods-reparse-external");
    var linkedModsTd = Path.Combine(linkedModsExternal, "TDBank");
    Directory.CreateDirectory(linkedModsTd);
    File.WriteAllText(
        Path.Combine(linkedModsTd, "TDBank.json"),
        """{"id":"TDBank","version":"v0.1"}""");
    File.WriteAllText(
        Path.Combine(linkedModsTd, "mods-link-sentinel.txt"),
        "linked mods must survive");
    var modsLinkGame = CreateFakeGame(
        Path.Combine(root, "mods-reparse-game"),
        "v0.109.1");
    var modsLink = Path.Combine(modsLinkGame, "mods");
    CreateDirectoryLink(modsLink, linkedModsExternal);
    rejected = false;
    try
    {
        _ = TransactionUninstaller.Uninstall(modsLinkGame);
    }
    catch (InstallerOperationException exception)
    {
        rejected = exception.Code == InstallerErrorCode.ReparsePoint;
    }
    Assert(rejected, "Uninstall accepted a linked Mods root.");
    Assert(
        File.ReadAllText(Path.Combine(linkedModsTd, "mods-link-sentinel.txt"))
            == "linked mods must survive",
        "Mods-root reparse rejection changed its external target.");
    Directory.Delete(modsLink);
}

static void CreateDirectoryLink(string linkPath, string targetPath)
{
    try
    {
        Directory.CreateSymbolicLink(linkPath, targetPath);
        return;
    }
    catch (Exception exception)
        when (exception is UnauthorizedAccessException
            or IOException
            or PlatformNotSupportedException)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            ArgumentList =
            {
                "/d",
                "/c",
                "mklink",
                "/J",
                linkPath,
                targetPath,
            },
        });
        if (process is null)
        {
            throw new IOException(
                "Could not start the junction fallback.",
                exception);
        }

        process.WaitForExit();
        if (process.ExitCode != 0 || !Directory.Exists(linkPath))
        {
            var detail = process.StandardError.ReadToEnd();
            if (string.IsNullOrWhiteSpace(detail))
            {
                detail = process.StandardOutput.ReadToEnd();
            }
            throw new IOException(
                $"Could not create a directory link: {detail}",
                exception);
        }
    }
}

static void AssertUiDetectsInstalledGame(string game)
{
    Exception? failure = null;
    var thread = new Thread(
        () =>
        {
            try
            {
                using var form = new InstallerForm(UiLanguage.En);
                var pathBox = Descendants<TextBox>(form).Single(textBox => textBox.ReadOnly);
                var consent = Descendants<CheckBox>(form).Single();
                pathBox.Text = game;

                var uninstall = Descendants<Button>(form).Single(
                    button => button.Text.Contains(
                        "Close Account",
                        StringComparison.Ordinal));
                Assert(uninstall.Enabled, "Detected uninstall button was not enabled.");
                Assert(
                    !consent.Checked,
                    "Uninstall detection unexpectedly accepted installation consent.");

                form.SetLanguage(UiLanguage.ZhCn);
                Assert(
                    uninstall.Text.Contains("注销账户", StringComparison.Ordinal),
                    "Chinese switch did not localize the uninstall button.");
                Assert(
                    uninstall.Enabled,
                    "Language switching disabled the detected uninstall button.");
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (failure is not null)
    {
        throw new InvalidOperationException(
            "WinForms uninstall detection test failed.",
            failure);
    }
}

static string[] SnapshotTreeExact(string root)
{
    if (!Directory.Exists(root))
    {
        return ["<missing>"];
    }

    var fullRoot = Path.GetFullPath(root);
    var entries = new List<string>
    {
        DescribeDirectory(fullRoot, "."),
    };
    foreach (var directory in Directory.EnumerateDirectories(
                 fullRoot,
                 "*",
                 SearchOption.AllDirectories))
    {
        entries.Add(DescribeDirectory(
            directory,
            Path.GetRelativePath(fullRoot, directory)));
    }
    foreach (var file in Directory.EnumerateFiles(
                 fullRoot,
                 "*",
                 SearchOption.AllDirectories))
    {
        var info = new FileInfo(file);
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
        entries.Add(
            $"F|{Path.GetRelativePath(fullRoot, file)}|{info.Length}|" +
            $"{info.LastWriteTimeUtc.Ticks}|{(int)info.Attributes}|{hash}");
    }

    return entries
        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

static string[] SnapshotTreeWithoutFollowingReparsePoints(string root)
{
    if (!Directory.Exists(root))
    {
        return ["<missing>"];
    }

    var fullRoot = Path.GetFullPath(root);
    var rootInfo = new DirectoryInfo(fullRoot);
    rootInfo.Refresh();
    var entries = new List<string>
    {
        $"D|.|{rootInfo.LastWriteTimeUtc.Ticks}|{(int)rootInfo.Attributes}",
    };
    if ((rootInfo.Attributes & FileAttributes.ReparsePoint) != 0)
    {
        return entries.ToArray();
    }

    var pending = new Stack<string>();
    pending.Push(fullRoot);
    while (pending.Count > 0)
    {
        var directory = pending.Pop();
        foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
        {
            entry.Refresh();
            var relative = Path.GetRelativePath(fullRoot, entry.FullName);
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                entries.Add(
                    $"L|{relative}|{entry.LastWriteTimeUtc.Ticks}|" +
                    $"{(int)entry.Attributes}");
            }
            else if ((entry.Attributes & FileAttributes.Directory) != 0)
            {
                entries.Add(
                    $"D|{relative}|{entry.LastWriteTimeUtc.Ticks}|" +
                    $"{(int)entry.Attributes}");
                pending.Push(entry.FullName);
            }
            else
            {
                var file = (FileInfo)entry;
                var hash = Convert.ToHexString(
                    SHA256.HashData(File.ReadAllBytes(file.FullName)));
                entries.Add(
                    $"F|{relative}|{file.Length}|{file.LastWriteTimeUtc.Ticks}|" +
                    $"{(int)file.Attributes}|{hash}");
            }
        }
    }

    return entries
        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

static string DescribeDirectory(string path, string relative)
{
    var info = new DirectoryInfo(path);
    return $"D|{relative}|{info.LastWriteTimeUtc.Ticks}|{(int)info.Attributes}";
}

static void AssertTreeExactlyUnchanged(
    string label,
    IReadOnlyList<string> expected,
    IReadOnlyList<string> actual)
{
    Assert(
        expected.SequenceEqual(actual, StringComparer.Ordinal),
        $"Uninstall changed the {label}.{Environment.NewLine}" +
        $"Expected:{Environment.NewLine}{string.Join(Environment.NewLine, expected)}" +
        $"{Environment.NewLine}Actual:{Environment.NewLine}" +
        string.Join(Environment.NewLine, actual));
}

static bool ContainsHanCharacter(string value)
{
    return value.Any(character => character is >= '\u4e00' and <= '\u9fff');
}

static void AssertUiLanguageSwitchPreservesState()
{
    Exception? failure = null;
    var thread = new Thread(
        () =>
        {
            try
            {
                using var form = new InstallerForm(UiLanguage.En);
                form.PerformLayout();
                var pathBox = Descendants<TextBox>(form).Single(textBox => textBox.ReadOnly);
                var consent = Descendants<CheckBox>(form).Single();
                var englishHeader = InstallerStrings.Get(UiLanguage.En, UiText.HeaderTitle);
                var headerTitleLabel = Descendants<Label>(form).Single(
                    label => string.Equals(label.Text, englishHeader, StringComparison.Ordinal));
                Assert(
                    !headerTitleLabel.AutoEllipsis,
                    "The English Install / Uninstall header may still be abbreviated.");
                Assert(
                    TextRenderer.MeasureText(englishHeader, headerTitleLabel.Font).Width
                    <= headerTitleLabel.ClientSize.Width,
                    "The English Install / Uninstall header does not fit at minimum window size.");
                Assert(
                    !Descendants<Button>(form).Any(
                        button => button.Text.Contains("Open Mods", StringComparison.Ordinal)
                            || button.Text.Contains("Launch via Steam", StringComparison.Ordinal)),
                    "Removed Mods-folder or Steam-launch utility button is still visible.");

                var releaseCapture = typeof(InstallerForm).GetMethod(
                    "ReleaseCapture",
                    BindingFlags.NonPublic | BindingFlags.Static);
                var sendMessage = typeof(InstallerForm).GetMethod(
                    "SendMessage",
                    BindingFlags.NonPublic | BindingFlags.Static);
                Assert(
                    releaseCapture?.GetCustomAttribute<DllImportAttribute>()?.Value
                        == "user32.dll"
                    && sendMessage?.GetCustomAttribute<DllImportAttribute>()?.Value
                        == "user32.dll",
                    "Installer header drag is not using the native Windows caption-drag API.");
                const string preservedPath = @"C:\A path that must survive localization";
                pathBox.Text = preservedPath;
                consent.Checked = true;

                form.SetLanguage(UiLanguage.ZhCn);
                Assert(
                    form.Text.Contains("Setup v0.1.4", StringComparison.Ordinal)
                    && form.Text.Contains("卸载器", StringComparison.Ordinal),
                    "Chinese switch did not update the setup/uninstall window title.");
                Assert(pathBox.Text == preservedPath, "Chinese switch changed the selected path.");
                Assert(consent.Checked, "Chinese switch changed consent state.");
                Assert(
                    Descendants<Label>(form).Any(label => label.Text.Contains("把今天的金币")),
                    "Chinese switch did not update static labels.");

                form.SetLanguage(UiLanguage.En);
                Assert(
                    form.Text.Contains("Setup v0.1.4", StringComparison.Ordinal)
                    && !ContainsHanCharacter(form.Text),
                    "English switch did not update the setup/uninstall window title.");
                Assert(pathBox.Text == preservedPath, "English switch changed the selected path.");
                Assert(consent.Checked, "English switch changed consent state.");
                Assert(
                    Descendants<Label>(form).Any(label => label.Text.Contains("Turn today")),
                    "English switch did not update static labels.");
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (failure is not null)
    {
        throw new InvalidOperationException("WinForms bilingual switch test failed.", failure);
    }
}

static IEnumerable<T> Descendants<T>(Control parent)
    where T : Control
{
    foreach (Control child in parent.Controls)
    {
        if (child is T match)
        {
            yield return match;
        }

        foreach (var descendant in Descendants<T>(child))
        {
            yield return descendant;
        }
    }
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

internal sealed class RecordingProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value)
    {
        report(value);
    }
}
