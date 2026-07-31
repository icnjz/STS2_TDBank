using CNJ.TowerDebt.Setup.Core;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CNJ.TowerDebt.Setup.Tests;

internal static class SaveProtectionTests
{
    private const string AccountA = "76561198000000001";
    private const string AccountB = "76561198000000002";

    public static void Run(string root)
    {
        Directory.CreateDirectory(root);

        MigratesMissingProfileAndCreatesCloudHandoffMarker(
            Path.Combine(root, "missing-target"));
        ReplacesOnlyKnownBlankProfiles(
            Path.Combine(root, "blank-versus-established"));
        RecognizesRealDualBranchBlanksAndFailsClosedOnUnknownSchema(
            Path.Combine(root, "real-blank-fixture"));
        HandlesMultipleAccountsAndUnsafeNames(
            Path.Combine(root, "multiple-accounts"));
        FailsClosedForUnknownSchemaAndUnsupportedProfile(
            Path.Combine(root, "fail-closed"));
        IsIdempotentAndPreservesExistingRootProfile(
            Path.Combine(root, "idempotent"));
        RejectsUnsafeTransactionIdsBeforeWriting(
            Path.Combine(root, "unsafe-transaction"));
        RollsBackAStagingFailure(
            Path.Combine(root, "copy-failure"));
        DoesNotTouchCloudMetadataOutsideTheSaveRoot(
            Path.Combine(root, "cloud-boundary"));
        RejectsReparsePointsWhenSupported(
            Path.Combine(root, "reparse"));

        Console.WriteLine("TD Bank save-protection matrix passed.");
    }

    private static void MigratesMissingProfileAndCreatesCloudHandoffMarker(string scenario)
    {
        var saveRoot = NewSaveRoot(scenario);
        var accountRoot = AccountRoot(saveRoot, AccountA);
        var vanilla = WriteProfile(accountRoot, "profile1", meaningful: true, includeRunEvidence: true);
        WriteText(Path.Combine(accountRoot, "profile.save"), """{"selected_profile":1}""");
        SetTreeTimestamp(accountRoot, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var vanillaBefore = SnapshotTree(vanilla);
        var accountProfileBefore = SnapshotFile(Path.Combine(accountRoot, "profile.save"));

        var result = SaveProtection.ProtectAndInitialize(saveRoot, "sp01");

        Assert(result.AccountsScanned == 1, "SP-01 did not scan the numeric Steam account.");
        Assert(result.ProfilesMigrated == 1, "SP-01 did not report one migrated profile.");
        Assert(
            result.Profiles.Single().Disposition == SaveProfileDisposition.Migrated,
            "SP-01 returned the wrong disposition.");

        var modded = Path.Combine(accountRoot, "modded", "profile1");
        AssertTreeContentEqual(vanilla, modded, "SP-01 migrated profile");
        AssertTreeExact(vanillaBefore, vanilla, "SP-01 changed the vanilla profile");
        AssertFileExact(
            accountProfileBefore,
            Path.Combine(accountRoot, "profile.save"),
            "SP-01 changed vanilla profile.save");
        Assert(
            File.ReadAllText(Path.Combine(accountRoot, "modded", "profile.save"))
                == File.ReadAllText(Path.Combine(accountRoot, "profile.save")),
            "SP-01 did not copy the account profile.save into the modded namespace.");

        AssertMarker(
            accountRoot,
            expectedProfileNames: ["profile1"],
            requireAccountProfile: true);
        AssertNoTemporaryArtifacts(saveRoot, "sp01");
    }

    private static void ReplacesOnlyKnownBlankProfiles(string scenario)
    {
        var saveRoot = NewSaveRoot(scenario);
        var accountRoot = AccountRoot(saveRoot, AccountA);
        WriteText(Path.Combine(accountRoot, "profile.save"), """{"selected_profile":1}""");

        var vanillaBlankTarget = WriteProfile(
            accountRoot,
            "profile1",
            meaningful: true,
            includeRunEvidence: true);
        var blankTarget = WriteProfile(
            Path.Combine(accountRoot, "modded"),
            "profile1",
            meaningful: false,
            includeRunEvidence: false);
        var capturedBlank = CapturedSchema22PristineProgress();
        var capturedBlankBytes = Encoding.UTF8.GetByteCount(capturedBlank);
        Assert(
            capturedBlankBytes == 1138,
            $"SP-03 captured public-beta blank fixture is {capturedBlankBytes:N0}, not 1,138 bytes.");
        WriteText(Path.Combine(blankTarget, "saves", "progress.save"), capturedBlank);
        WriteText(Path.Combine(blankTarget, "saves", "progress.save.backup"), capturedBlank);
        var blankBefore = SnapshotTree(blankTarget);

        WriteProfile(accountRoot, "profile2", meaningful: true, includeRunEvidence: false);
        var established = WriteProfile(
            Path.Combine(accountRoot, "modded"),
            "profile2",
            meaningful: true,
            includeRunEvidence: false);
        WriteText(Path.Combine(established, "do-not-touch.txt"), "friend's real modded progress");
        var establishedBefore = SnapshotTree(established);

        WriteProfile(accountRoot, "profile3", meaningful: true, includeRunEvidence: false);
        var evidenceTarget = WriteProfile(
            Path.Combine(accountRoot, "modded"),
            "profile3",
            meaningful: false,
            includeRunEvidence: true);
        var evidenceBefore = SnapshotTree(evidenceTarget);

        var result = SaveProtection.ProtectAndInitialize(saveRoot, "sp03");
        var byProfile = result.Profiles.ToDictionary(profile => profile.ProfileName);

        Assert(
            byProfile["profile1"].Disposition == SaveProfileDisposition.Migrated,
            "SP-03 did not replace a known game-generated blank profile.");
        Assert(
            byProfile["profile2"].Disposition == SaveProfileDisposition.PreservedEstablished,
            "SP-04 did not preserve an established modded profile.");
        Assert(
            byProfile["profile3"].Disposition == SaveProfileDisposition.PreservedEstablished,
            "SP-05 treated current-run/history evidence as blank.");

        AssertTreeContentEqual(
            vanillaBlankTarget,
            blankTarget,
            "SP-03 replacement content");
        AssertTreeExact(
            establishedBefore,
            established,
            "SP-04 established profile");
        AssertTreeExact(
            evidenceBefore,
            evidenceTarget,
            "SP-05 evidence-bearing profile");

        Assert(
            !string.IsNullOrWhiteSpace(result.BackupDirectory)
                && Directory.Exists(result.BackupDirectory),
            "SP-03 did not retain a backup directory for the replaced blank target.");
        Assert(
            Directory.EnumerateFiles(result.BackupDirectory, "progress.save", SearchOption.AllDirectories)
                .Any(path => File.ReadAllBytes(path)
                    .SequenceEqual(blankBefore["saves/progress.save"].Bytes)),
            "SP-03 backup does not contain the replaced blank progress.save.");

        AssertMarker(
            accountRoot,
            expectedProfileNames: ["profile1"],
            requireAccountProfile: true);
    }

    private static void FailsClosedForUnknownSchemaAndUnsupportedProfile(string scenario)
    {
        var saveRoot = NewSaveRoot(scenario);
        var accountRoot = AccountRoot(saveRoot, AccountA);
        WriteText(Path.Combine(accountRoot, "profile.save"), """{"selected_profile":2}""");

        WriteProfile(accountRoot, "profile1", meaningful: true, includeRunEvidence: false);
        var schema25Target = WriteProfile(
            Path.Combine(accountRoot, "modded"),
            "profile1",
            meaningful: false,
            includeRunEvidence: false);
        var schema25 = CapturedSchema22PristineProgress().Replace(
            "\"schema_version\": 22",
            "\"schema_version\": 25",
            StringComparison.Ordinal);
        WriteText(Path.Combine(schema25Target, "saves", "progress.save"), schema25);
        WriteText(Path.Combine(schema25Target, "saves", "progress.save.backup"), schema25);
        var schema25Before = SnapshotTree(schema25Target);

        WriteProfile(accountRoot, "profile2", meaningful: true, includeRunEvidence: false);
        WriteProfile(accountRoot, "profile4", meaningful: true, includeRunEvidence: false);

        var result = SaveProtection.ProtectAndInitialize(saveRoot, "sp-schema");
        var byName = result.Profiles.ToDictionary(profile => profile.ProfileName);

        Assert(
            byName["profile1"].Disposition == SaveProfileDisposition.PreservedEstablished,
            "SP schema-25 target was treated as a replaceable blank profile.");
        AssertTreeExact(
            schema25Before,
            schema25Target,
            "SP schema-25 fail-closed target");
        Assert(
            byName["profile4"].Disposition == SaveProfileDisposition.SkippedUnsafe,
            "SP profile4 was not reported as unsupported.");
        Assert(
            !Directory.Exists(Path.Combine(accountRoot, "modded", "profile4")),
            "SP profile4 was copied into the modded namespace.");
        Assert(
            byName["profile2"].Disposition == SaveProfileDisposition.Migrated,
            "SP valid profile2 did not migrate alongside skipped profiles.");

        AssertMarker(
            accountRoot,
            expectedProfileNames: ["profile2"],
            requireAccountProfile: true);
        using var marker = JsonDocument.Parse(File.ReadAllText(MarkerPath(accountRoot)));
        Assert(
            marker.RootElement.GetProperty("profiles").EnumerateArray()
                .Select(element => element.GetInt32())
                .SequenceEqual([2]),
            "SP profile4 leaked into the cloud-handoff marker profile list.");
        Assert(
            marker.RootElement.GetProperty("files").EnumerateArray()
                .All(entry =>
                    !entry.GetProperty("target_relative_path").GetString()!
                        .Contains("profile4/", StringComparison.OrdinalIgnoreCase)),
            "SP profile4 leaked into the cloud-handoff marker file list.");
    }

    private static void HandlesMultipleAccountsAndUnsafeNames(string scenario)
    {
        var saveRoot = NewSaveRoot(scenario);
        var accountA = AccountRoot(saveRoot, AccountA);
        var accountB = AccountRoot(saveRoot, AccountB);
        WriteText(Path.Combine(accountA, "profile.save"), """{"selected_profile":1}""");
        WriteText(Path.Combine(accountB, "profile.save"), """{"selected_profile":3}""");

        WriteProfile(accountA, "profile1", meaningful: true, includeRunEvidence: false);
        WriteProfile(accountA, "profile2", meaningful: true, includeRunEvidence: false);
        var accountAProfile2Modded = WriteProfile(
            Path.Combine(accountA, "modded"),
            "profile2",
            meaningful: true,
            includeRunEvidence: false);
        WriteText(
            Path.Combine(accountAProfile2Modded, "saves", "modded-only.save"),
            "established");

        var noVanilla = WriteProfile(
            Path.Combine(accountB, "modded"),
            "profile3",
            meaningful: true,
            includeRunEvidence: false);
        var noVanillaBefore = SnapshotTree(noVanilla);

        var unsafeAccount = AccountRoot(saveRoot, "not-a-steam-id");
        var unsafeProfile = WriteProfile(
            unsafeAccount,
            "profile1",
            meaningful: true,
            includeRunEvidence: false);
        var unsafeBefore = SnapshotTree(unsafeProfile);

        foreach (var invalidName in new[] { "profile0", "profile4", "profile1x", "profile-1" })
        {
            WriteProfile(accountA, invalidName, meaningful: true, includeRunEvidence: false);
        }

        var result = SaveProtection.ProtectAndInitialize(saveRoot, "sp08");
        var keyed = result.Profiles.ToDictionary(
            profile => $"{profile.SteamAccountId}/{profile.ProfileName}");

        Assert(result.AccountsScanned == 2, "SP-08 counted an unsafe Steam account name.");
        Assert(
            keyed[$"{AccountA}/profile1"].Disposition == SaveProfileDisposition.Migrated,
            "SP-08 failed to migrate account A/profile1.");
        Assert(
            keyed[$"{AccountA}/profile2"].Disposition
                == SaveProfileDisposition.PreservedEstablished,
            "SP-08 failed to preserve account A/profile2.");
        Assert(
            keyed[$"{AccountB}/profile3"].Disposition == SaveProfileDisposition.NoUsableVanilla,
            "SP-07 did not report the missing vanilla profile.");
        Assert(
            keyed[$"{AccountA}/profile0"].Disposition == SaveProfileDisposition.SkippedUnsafe
                && keyed[$"{AccountA}/profile4"].Disposition == SaveProfileDisposition.SkippedUnsafe
                && result.Profiles.All(profile =>
                    profile.ProfileName is not ("profile1x" or "profile-1")),
            "SP-09 did not skip invalid profile names safely.");
        AssertTreeExact(
            noVanillaBefore,
            noVanilla,
            "SP-07 target without vanilla");
        AssertTreeExact(
            unsafeBefore,
            unsafeProfile,
            "SP-09 unsafe-account sentinel");
    }

    private static void IsIdempotentAndPreservesExistingRootProfile(string scenario)
    {
        var saveRoot = NewSaveRoot(scenario);
        var accountRoot = AccountRoot(saveRoot, AccountA);
        var vanilla = WriteProfile(
            accountRoot,
            "profile1",
            meaningful: true,
            includeRunEvidence: false);
        WriteText(Path.Combine(accountRoot, "profile.save"), "vanilla-root-profile");
        var moddedRootProfile = Path.Combine(accountRoot, "modded", "profile.save");
        WriteText(moddedRootProfile, "vanilla-root-profile");

        var first = SaveProtection.ProtectAndInitialize(saveRoot, "sp10-first");
        Assert(first.ProfilesMigrated == 1, "SP-10 first pass did not migrate the profile.");
        var modded = Path.Combine(accountRoot, "modded", "profile1");
        var equivalentBefore = SnapshotTree(modded);
        var equivalent = SaveProtection.ProtectAndInitialize(saveRoot, "sp10-equivalent");
        Assert(
            equivalent.Profiles.Single().Disposition == SaveProfileDisposition.AlreadyEquivalent,
            "SP-06 did not recognize an already equivalent local mirror.");
        AssertTreeExact(
            equivalentBefore,
            modded,
            "SP-06 equivalent profile");
        AssertMarker(
            accountRoot,
            expectedProfileNames: ["profile1"],
            requireAccountProfile: true);

        WriteText(moddedRootProfile, "existing-modded-root-profile");
        var moddedRootBefore = SnapshotFile(moddedRootProfile);

        WriteText(Path.Combine(modded, "saves", "played-after-install.save"), "new modded data");
        var moddedBeforeSecond = SnapshotTree(modded);
        WriteText(Path.Combine(vanilla, "saves", "progress.save"), MeaningfulProgress(42));

        var markerPath = MarkerPath(accountRoot);
        var markerBefore = SnapshotFile(markerPath);
        var second = SaveProtection.ProtectAndInitialize(saveRoot, "sp10-second");

        Assert(
            second.Profiles.Single().Disposition == SaveProfileDisposition.PreservedEstablished,
            "SP-12 reinstall did not preserve a profile played after migration.");
        AssertTreeExact(
            moddedBeforeSecond,
            modded,
            "SP-12 reinstall changed established modded data");
        AssertFileExact(
            moddedRootBefore,
            moddedRootProfile,
            "SP-11 overwrote the existing modded profile.save");
        AssertFileExact(
            markerBefore,
            markerPath,
            "SP-19 reinstall rewrote the pending cloud-handoff marker");
    }

    private static void RecognizesRealDualBranchBlanksAndFailsClosedOnUnknownSchema(
        string scenario)
    {
        var schema21Root = NewSaveRoot(Path.Combine(scenario, "schema21"));
        var schema21Account = AccountRoot(schema21Root, AccountA);
        WriteText(Path.Combine(schema21Account, "profile.save"), """{"last_profile_id":1,"schema_version":2}""");
        WriteProfile(schema21Account, "profile1", meaningful: true, includeRunEvidence: true);
        WriteBlankFixture(
            Path.Combine(schema21Account, "modded", "profile1"),
            PublicLatestSchema21BlankFixture());

        var schema21Result = SaveProtection.ProtectAndInitialize(schema21Root, "sp-latest-blank");
        Assert(
            schema21Result.Profiles.Single().Disposition == SaveProfileDisposition.Migrated,
            "The Steam Latest schema-v21 blank structure was not recognized as blank.");

        var schema22Root = NewSaveRoot(Path.Combine(scenario, "schema22"));
        var schema22Account = AccountRoot(schema22Root, AccountA);
        WriteText(Path.Combine(schema22Account, "profile.save"), """{"last_profile_id":1,"schema_version":2}""");
        WriteProfile(schema22Account, "profile1", meaningful: true, includeRunEvidence: true);
        WriteBlankFixture(
            Path.Combine(schema22Account, "modded", "profile1"),
            PublicBetaSchema22BlankFixture());

        var schema22Result = SaveProtection.ProtectAndInitialize(schema22Root, "sp-real-blank");
        Assert(
            schema22Result.Profiles.Single().Disposition == SaveProfileDisposition.Migrated,
            "The real 1,138-byte public-beta blank structure was not recognized as blank.");

        var schema24Root = NewSaveRoot(Path.Combine(scenario, "schema24"));
        var schema24Account = AccountRoot(schema24Root, AccountB);
        WriteText(Path.Combine(schema24Account, "profile.save"), """{"last_profile_id":1,"schema_version":2}""");
        WriteProfile(schema24Account, "profile1", meaningful: true, includeRunEvidence: false);
        WriteBlankFixture(
            Path.Combine(schema24Account, "modded", "profile1"),
            PublicBetaSchema22BlankFixture().Replace(
                "\"schema_version\": 22",
                "\"schema_version\": 24",
                StringComparison.Ordinal));

        var schema24Result = SaveProtection.ProtectAndInitialize(schema24Root, "sp-schema24");
        Assert(
            schema24Result.Profiles.Single().Disposition
                == SaveProfileDisposition.Migrated,
            "The v0.110.0 schema-v24 blank structure was not recognized as blank.");

        var schema25Root = NewSaveRoot(Path.Combine(scenario, "schema25"));
        var schema25Account = AccountRoot(schema25Root, AccountB);
        WriteText(Path.Combine(schema25Account, "profile.save"), """{"last_profile_id":1,"schema_version":2}""");
        WriteProfile(schema25Account, "profile1", meaningful: true, includeRunEvidence: false);
        var unknownSchemaTarget = WriteBlankFixture(
            Path.Combine(schema25Account, "modded", "profile1"),
            PublicBetaSchema22BlankFixture().Replace(
                "\"schema_version\": 22",
                "\"schema_version\": 25",
                StringComparison.Ordinal));
        var unknownBefore = SnapshotTree(unknownSchemaTarget);

        var schema25Result = SaveProtection.ProtectAndInitialize(schema25Root, "sp-unknown-schema");
        Assert(
            schema25Result.Profiles.Single().Disposition
                == SaveProfileDisposition.PreservedEstablished,
            "An unknown future progress schema was not preserved fail-closed.");
        AssertTreeExact(
            unknownBefore,
            unknownSchemaTarget,
            "Unknown-schema target");
        Assert(
            !File.Exists(MarkerPath(schema25Account)),
            "Unknown-schema target incorrectly authorized a cloud handoff marker.");
    }

    private static void RejectsUnsafeTransactionIdsBeforeWriting(string scenario)
    {
        var saveRoot = NewSaveRoot(scenario);
        var accountRoot = AccountRoot(saveRoot, AccountA);
        WriteProfile(accountRoot, "profile1", meaningful: true, includeRunEvidence: false);
        var before = SnapshotTree(saveRoot);

        var rejected = false;
        try
        {
            SaveProtection.ProtectAndInitialize(saveRoot, @"..\escape");
        }
        catch (Exception exception)
            when (exception is ArgumentException or InstallerOperationException)
        {
            rejected = true;
        }

        Assert(rejected, "SP-15 accepted a traversal-shaped transaction ID.");
        AssertTreeExact(before, saveRoot, "SP-15 unsafe transaction attempt");
        Assert(
            !Directory.Exists(Path.Combine(scenario, "escape")),
            "SP-15 created a directory outside the injected save root.");
    }

    private static void RollsBackAStagingFailure(string scenario)
    {
        var saveRoot = NewSaveRoot(scenario);
        var accountRoot = AccountRoot(saveRoot, AccountA);
        var vanilla = WriteProfile(
            accountRoot,
            "profile1",
            meaningful: true,
            includeRunEvidence: true);
        var lockedPath = Path.Combine(vanilla, "saves", "history", "locked.run");
        WriteText(lockedPath, "cannot copy while locked");
        var blankTarget = WriteProfile(
            Path.Combine(accountRoot, "modded"),
            "profile1",
            meaningful: false,
            includeRunEvidence: false);
        var vanillaBefore = SnapshotTree(vanilla);
        var targetBefore = SnapshotTree(blankTarget);

        Exception? failure = null;
        SaveProtectionResult? result = null;
        using (var locked = new FileStream(
            lockedPath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None))
        {
            try
            {
                result = SaveProtection.ProtectAndInitialize(saveRoot, "sp13");
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        }

        Assert(
            failure is not null
                || result!.Profiles.Any(profile =>
                    profile.Disposition == SaveProfileDisposition.SkippedUnsafe),
            "SP-13 silently reported success after a staging copy failure.");
        AssertTreeExact(vanillaBefore, vanilla, "SP-13 vanilla source");
        AssertTreeExact(targetBefore, blankTarget, "SP-13 pre-existing blank target");
        AssertNoTemporaryArtifacts(saveRoot, "sp13");
    }

    private static void DoesNotTouchCloudMetadataOutsideTheSaveRoot(string scenario)
    {
        var saveRoot = NewSaveRoot(Path.Combine(scenario, "appdata"));
        var accountRoot = AccountRoot(saveRoot, AccountA);
        WriteProfile(accountRoot, "profile1", meaningful: true, includeRunEvidence: false);

        var cloudMetadata = Path.Combine(
            scenario,
            "Steam",
            "userdata",
            AccountA,
            "2868840",
            "remotecache.vdf");
        WriteText(cloudMetadata, "remote metadata belongs to Steam");
        File.SetLastWriteTimeUtc(
            cloudMetadata,
            new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var cloudBefore = SnapshotFile(cloudMetadata);

        SaveProtection.ProtectAndInitialize(saveRoot, "sp16");

        AssertFileExact(
            cloudBefore,
            cloudMetadata,
            "SP-16 modified Steam Cloud metadata");
    }

    private static void RejectsReparsePointsWhenSupported(string scenario)
    {
        var saveRoot = NewSaveRoot(scenario);
        var accountRoot = AccountRoot(saveRoot, AccountA);
        var external = Path.Combine(scenario, "external-profile");
        var linkedProfile = Path.Combine(accountRoot, "profile1");
        WriteProfile(external, "source", meaningful: true, includeRunEvidence: false);
        var externalProfile = Path.Combine(external, "source");
        var externalBefore = SnapshotTree(externalProfile);
        Directory.CreateDirectory(accountRoot);

        Exception? symbolicLinkFailure = null;
        try
        {
            Directory.CreateSymbolicLink(linkedProfile, externalProfile);
        }
        catch (Exception exception)
            when (exception is UnauthorizedAccessException
                or IOException
                or PlatformNotSupportedException)
        {
            symbolicLinkFailure = exception;
        }

        if (!Directory.Exists(linkedProfile)
            && !TryCreateDirectoryJunction(linkedProfile, externalProfile))
        {
            Console.WriteLine(
                "SP-14 reparse-point test skipped: symbolic links and junctions unavailable "
                + $"({symbolicLinkFailure?.GetType().Name ?? "unknown error"}).");
            return;
        }

        try
        {
            var result = SaveProtection.ProtectAndInitialize(saveRoot, "sp14");
            Assert(
                result.ProfilesSkippedUnsafe > 0 || result.Profiles.Count == 0,
                "SP-14 followed a reparse-point profile as a normal source.");
            AssertTreeExact(externalBefore, externalProfile, "SP-14 external sentinel");
            Assert(
                !Directory.Exists(Path.Combine(accountRoot, "modded", "profile1")),
                "SP-14 initialized a target by following an unsafe source link.");
        }
        finally
        {
            if (Directory.Exists(linkedProfile)
                && (File.GetAttributes(linkedProfile) & FileAttributes.ReparsePoint) != 0)
            {
                Directory.Delete(linkedProfile, recursive: false);
            }
        }
    }

    private static bool TryCreateDirectoryJunction(string linkPath, string targetPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using var process = Process.Start(
                new ProcessStartInfo
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
                return false;
            }

            process.WaitForExit();
            return process.ExitCode == 0
                && Directory.Exists(linkPath)
                && (File.GetAttributes(linkPath) & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            return false;
        }
    }

    private static string NewSaveRoot(string scenario)
    {
        var root = Path.Combine(scenario, "SlayTheSpire2");
        Directory.CreateDirectory(Path.Combine(root, "steam"));
        return root;
    }

    private static string AccountRoot(string saveRoot, string accountId) =>
        Path.Combine(saveRoot, "steam", accountId);

    private static string WriteProfile(
        string accountOrModdedRoot,
        string profileName,
        bool meaningful,
        bool includeRunEvidence)
    {
        var profile = Path.Combine(accountOrModdedRoot, profileName);
        var saves = Path.Combine(profile, "saves");
        WriteText(
            Path.Combine(saves, "progress.save"),
            meaningful ? MeaningfulProgress(1) : BlankProgress());
        WriteText(
            Path.Combine(saves, "progress.save.backup"),
            meaningful ? MeaningfulProgress(1) : BlankProgress());
        WriteText(Path.Combine(saves, "prefs.save"), """{"language":"eng"}""");
        WriteText(Path.Combine(saves, "prefs.save.backup"), """{"language":"eng"}""");

        if (includeRunEvidence)
        {
            WriteText(Path.Combine(saves, "current_run_mp.save"), "active-run");
            WriteText(Path.Combine(saves, "history", "100.run"), "history");
            WriteText(Path.Combine(profile, "replays", "latest.mcr"), "replay");
        }

        return profile;
    }

    private static string BlankProgress() => ProgressJson(0);

    private static string WriteBlankFixture(string profile, string progress)
    {
        var saves = Path.Combine(profile, "saves");
        WriteText(Path.Combine(saves, "progress.save"), progress);
        WriteText(Path.Combine(saves, "progress.save.backup"), progress);
        WriteText(Path.Combine(saves, "prefs.save"), """{"language":"eng"}""");
        WriteText(Path.Combine(saves, "prefs.save.backup"), """{"language":"eng"}""");
        return profile;
    }

    private static string PublicBetaSchema22BlankFixture() =>
        """
        {
          "ancient_stats": [],
          "architect_damage": 0,
          "card_stats": [],
          "character_stats": [
            {
              "badges": [],
              "best_win_streak": 0,
              "current_streak": 0,
              "fastest_win_time": -1,
              "id": "CHARACTER.IRONCLAD",
              "max_ascension": 0,
              "playtime": 0,
              "preferred_ascension": 0,
              "total_losses": 0,
              "total_wins": 0
            }
          ],
          "current_score": 0,
          "discovered_acts": [],
          "discovered_cards": [
            "CARD.STRIKE_IRONCLAD",
            "CARD.DEFEND_IRONCLAD",
            "CARD.BASH"
          ],
          "discovered_events": [],
          "discovered_potions": [],
          "discovered_relics": [
            "RELIC.BURNING_BLOOD"
          ],
          "enable_ftues": false,
          "encounter_stats": [],
          "enemy_stats": [],
          "epochs": [],
          "floors_climbed": 0,
          "ftue_completed": [
            "accept_tutorials_ftue"
          ],
          "max_multiplayer_ascension": 0,
          "pending_character_unlock": "NONE.NONE",
          "preferred_multiplayer_ascension": 0,
          "schema_version": 22,
          "test_subject_kills": 0,
          "total_playtime": 0,
          "total_unlocks": 0,
          "unique_id": "5RQA3EL",
          "unlocked_achievements": [],
          "wongo_points": 0
        }
        """;

    private static string PublicLatestSchema21BlankFixture()
    {
        var root = JsonNode.Parse(PublicBetaSchema22BlankFixture())!.AsObject();
        root["schema_version"] = 21;
        root["character_stats"] = new JsonArray();
        return root.ToJsonString();
    }

    private static string MeaningfulProgress(int floorsClimbed) => ProgressJson(floorsClimbed);

    private static string CapturedSchema22PristineProgress()
    {
        const string fixture = """
            {
              "ancient_stats": [],
              "architect_damage": 0,
              "card_stats": [],
              "character_stats": [
                {
                  "badges": [],
                  "best_win_streak": 0,
                  "current_streak": 0,
                  "fastest_win_time": -1,
                  "id": "CHARACTER.IRONCLAD",
                  "max_ascension": 0,
                  "playtime": 0,
                  "preferred_ascension": 0,
                  "total_losses": 0,
                  "total_wins": 0
                }
              ],
              "current_score": 0,
              "discovered_acts": [],
              "discovered_cards": [
                "CARD.STRIKE_IRONCLAD",
                "CARD.DEFEND_IRONCLAD",
                "CARD.BASH"
              ],
              "discovered_events": [],
              "discovered_potions": [],
              "discovered_relics": [
                "RELIC.BURNING_BLOOD"
              ],
              "enable_ftues": false,
              "encounter_stats": [],
              "enemy_stats": [],
              "epochs": [],
              "floors_climbed": 0,
              "ftue_completed": [
                "accept_tutorials_ftue"
              ],
              "max_multiplayer_ascension": 0,
              "pending_character_unlock": "NONE.NONE",
              "preferred_multiplayer_ascension": 0,
              "schema_version": 22,
              "test_subject_kills": 0,
              "total_playtime": 0,
              "total_unlocks": 0,
              "unique_id": "5RQA3EL",
              "unlocked_achievements": [],
              "wongo_points": 0
            }
            """;
        return fixture.Replace("\n", "\r\n", StringComparison.Ordinal);
    }

    private static string ProgressJson(int floorsClimbed)
    {
        return JsonSerializer.Serialize(
            new
            {
                ancient_stats = Array.Empty<object>(),
                architect_damage = 0,
                card_stats = Array.Empty<object>(),
                character_stats = new[]
                {
                    new
                    {
                        badges = Array.Empty<object>(),
                        best_win_streak = 0,
                        current_streak = 0,
                        fastest_win_time = -1,
                        id = "CHARACTER.IRONCLAD",
                        max_ascension = 0,
                        playtime = floorsClimbed,
                        preferred_ascension = 0,
                        total_losses = 0,
                        total_wins = 0,
                    },
                },
                current_score = 0,
                discovered_acts = Array.Empty<object>(),
                discovered_cards = new[]
                {
                    "CARD.STRIKE_IRONCLAD",
                    "CARD.DEFEND_IRONCLAD",
                    "CARD.BASH",
                },
                discovered_events = Array.Empty<object>(),
                discovered_potions = Array.Empty<object>(),
                discovered_relics = new[] { "RELIC.BURNING_BLOOD" },
                enable_ftues = false,
                encounter_stats = Array.Empty<object>(),
                enemy_stats = Array.Empty<object>(),
                epochs = Array.Empty<object>(),
                floors_climbed = floorsClimbed,
                ftue_completed = new[] { "accept_tutorials_ftue" },
                max_multiplayer_ascension = 0,
                pending_character_unlock = "NONE.NONE",
                preferred_multiplayer_ascension = 0,
                schema_version = 22,
                test_subject_kills = 0,
                total_playtime = floorsClimbed,
                total_unlocks = 0,
                unique_id = "TEST123",
                unlocked_achievements = Array.Empty<object>(),
                wongo_points = 0,
            },
            new JsonSerializerOptions { WriteIndented = true });
    }

    private static void AssertMarker(
        string accountRoot,
        IReadOnlyCollection<string> expectedProfileNames,
        bool requireAccountProfile)
    {
        var markerPath = MarkerPath(accountRoot);
        Assert(File.Exists(markerPath), "SP-17 pending cloud-handoff marker is missing.");

        using var marker = JsonDocument.Parse(File.ReadAllText(markerPath));
        Assert(
            marker.RootElement.TryGetProperty("files", out var files)
                && files.ValueKind == JsonValueKind.Array,
            "SP-17 marker has no files array.");

        var entries = files.EnumerateArray().ToArray();
        Assert(entries.Length > 0, "SP-17 marker contains no mirrored files.");

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var criticalPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            var relativePath = ReadMarkerString(
                entry,
                "target_relative_path",
                "targetRelativePath");
            var sha = ReadMarkerString(entry, "target_sha256", "targetSha256");
            var sourceSha = ReadMarkerString(entry, "source_sha256", "sourceSha256");
            Assert(
                relativePath is not null
                    && !Path.IsPathRooted(relativePath)
                    && !relativePath.Split(
                            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                            StringSplitOptions.RemoveEmptyEntries)
                        .Contains("..", StringComparer.Ordinal),
                "SP-17 marker contains an unsafe or missing relative path.");
            Assert(
                sha is not null
                    && sha.Length == 64
                    && sha.All(character => Uri.IsHexDigit(character)),
                "SP-17 marker contains an invalid SHA-256.");
            Assert(
                string.Equals(sourceSha, sha, StringComparison.OrdinalIgnoreCase),
                "SP-17 source and target hashes differ.");

            var normalized = relativePath!.Replace('\\', '/');
            paths.Add(normalized);
            var critical = entry.TryGetProperty("critical", out var criticalElement)
                && criticalElement.ValueKind is JsonValueKind.True or JsonValueKind.False
                && criticalElement.GetBoolean();
            if (critical)
            {
                criticalPaths.Add(normalized);
            }

            var localPath = Path.GetFullPath(
                Path.Combine(accountRoot, relativePath));
            Assert(File.Exists(localPath), $"SP-17 marker lists a missing local file: {relativePath}");
            Assert(
                HashFile(localPath).Equals(sha, StringComparison.OrdinalIgnoreCase),
                $"SP-17 marker hash does not match local file: {relativePath}");
        }

        foreach (var profileName in expectedProfileNames)
        {
            var progress = $"modded/{profileName}/saves/progress.save";
            Assert(paths.Contains(progress), $"SP-17 marker omitted {progress}.");
            Assert(criticalPaths.Contains(progress), $"SP-17 did not mark {progress} critical.");
        }

        if (requireAccountProfile)
        {
            Assert(paths.Contains("modded/profile.save"), "SP-17 marker omitted profile.save.");
            Assert(
                criticalPaths.Contains("modded/profile.save"),
                "SP-17 did not mark profile.save critical.");
        }

        Assert(
            !Directory.EnumerateFiles(
                    Path.GetDirectoryName(markerPath)!,
                    "*.tmp",
                    SearchOption.TopDirectoryOnly)
                .Any(),
            "SP-17 left a temporary marker file behind.");
    }

    private static string? ReadMarkerString(
        JsonElement entry,
        string preferredName,
        string fallbackName)
    {
        if (entry.TryGetProperty(preferredName, out var preferred)
            && preferred.ValueKind == JsonValueKind.String)
        {
            return preferred.GetString();
        }
        if (entry.TryGetProperty(fallbackName, out var fallback)
            && fallback.ValueKind == JsonValueKind.String)
        {
            return fallback.GetString();
        }
        return null;
    }

    private static string MarkerPath(string accountRoot) =>
        Path.Combine(accountRoot, "tdbank_migration_v2_1.pending.json");

    private static void SetTreeTimestamp(string root, DateTime timestampUtc)
    {
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            File.SetLastWriteTimeUtc(file, timestampUtc);
        }
    }

    private static Dictionary<string, FileSnapshot> SnapshotTree(string root)
    {
        if (!Directory.Exists(root))
        {
            return new Dictionary<string, FileSnapshot>(StringComparer.OrdinalIgnoreCase);
        }

        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetRelativePath(root, path).Replace('\\', '/'),
                SnapshotFile,
                StringComparer.OrdinalIgnoreCase);
    }

    private static FileSnapshot SnapshotFile(string path)
    {
        var info = new FileInfo(path);
        return new FileSnapshot(
            File.ReadAllBytes(path),
            info.LastWriteTimeUtc,
            info.Attributes);
    }

    private static void AssertTreeContentEqual(string expectedRoot, string actualRoot, string label)
    {
        var expected = SnapshotTree(expectedRoot);
        var actual = SnapshotTree(actualRoot);
        Assert(
            expected.Keys.Order(StringComparer.OrdinalIgnoreCase)
                .SequenceEqual(actual.Keys.Order(StringComparer.OrdinalIgnoreCase)),
            $"{label} has a different file inventory.");
        foreach (var relativePath in expected.Keys)
        {
            Assert(
                expected[relativePath].Bytes.SequenceEqual(actual[relativePath].Bytes),
                $"{label} content differs: {relativePath}");
        }
    }

    private static void AssertTreeExact(
        IReadOnlyDictionary<string, FileSnapshot> expected,
        string actualRoot,
        string label)
    {
        var actual = SnapshotTree(actualRoot);
        Assert(
            expected.Keys.Order(StringComparer.OrdinalIgnoreCase)
                .SequenceEqual(actual.Keys.Order(StringComparer.OrdinalIgnoreCase)),
            $"{label} file inventory changed.");
        foreach (var relativePath in expected.Keys)
        {
            AssertFileSnapshotEqual(expected[relativePath], actual[relativePath], $"{label}/{relativePath}");
        }
    }

    private static void AssertFileExact(FileSnapshot expected, string actualPath, string label)
    {
        Assert(File.Exists(actualPath), $"{label} was deleted.");
        AssertFileSnapshotEqual(expected, SnapshotFile(actualPath), label);
    }

    private static void AssertFileSnapshotEqual(
        FileSnapshot expected,
        FileSnapshot actual,
        string label)
    {
        Assert(expected.Bytes.SequenceEqual(actual.Bytes), $"{label} bytes changed.");
        Assert(expected.LastWriteTimeUtc == actual.LastWriteTimeUtc, $"{label} timestamp changed.");
        Assert(expected.Attributes == actual.Attributes, $"{label} attributes changed.");
    }

    private static void AssertNoTemporaryArtifacts(string saveRoot, string transactionId)
    {
        Assert(
            !Directory.EnumerateFileSystemEntries(saveRoot, "*", SearchOption.AllDirectories)
                .Any(path =>
                    Path.GetFileName(path).Contains(transactionId, StringComparison.OrdinalIgnoreCase)
                    && (Path.GetFileName(path).Contains("stage", StringComparison.OrdinalIgnoreCase)
                        || Path.GetFileName(path).Contains("tmp", StringComparison.OrdinalIgnoreCase))),
            $"Temporary staging data remains for {transactionId}.");
    }

    private static string HashFile(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static void WriteText(string path, string value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, value);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed record FileSnapshot(
        byte[] Bytes,
        DateTime LastWriteTimeUtc,
        FileAttributes Attributes);
}
