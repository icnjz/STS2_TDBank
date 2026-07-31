using CNJ.TowerDebt.Setup.Core;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CNJ.TowerDebt.Setup.Tests;

internal static class UninstallSaveHandoffTests
{
    private const string AccountId = "76561198012345678";
    private const string UniqueId = "TD-HANDOFF-TEST";

    public static void Run(string root)
    {
        Directory.CreateDirectory(root);
        PreparesExactSnapshotAndRequiresVerifiedReceipt(
            Path.Combine(root, "prepare-and-inspect"));
        FailsClosedOnUnsafeSourceData(
            Path.Combine(root, "unsafe-source"));
        AcceptsOnlyKnownRemovableCurrentRunState(
            Path.Combine(root, "known-current-run-state"));
        RejectsUnsafeReceiptTargets(
            Path.Combine(root, "unsafe-receipt"));
        ReplacesOnlyProtocolArtifactsAfterBackingThemUp(
            Path.Combine(root, "stale-protocol"));
        PreservesOverflowHistoryInBackupAndAllowsVerifiedCoreHandoff(
            Path.Combine(root, "overflow-history"));

        Assert(
            UninstallSaveHandoff.IsAllowedVanillaCloudTarget(
                "profile3/saves/history/abc.run"),
            "Handoff allowlist rejected a valid history save.");
        Assert(
            !UninstallSaveHandoff.IsAllowedVanillaCloudTarget(
                "profile4/saves/progress.save")
            && !UninstallSaveHandoff.IsAllowedVanillaCloudTarget(
                "profile1/saves/replays/abc.run")
            && !UninstallSaveHandoff.IsAllowedVanillaCloudTarget(
                "../profile.save"),
            "Handoff allowlist accepted a path outside the game's cloud-save set.");

        Console.WriteLine("TD Bank uninstall save-handoff matrix passed.");
    }

    private static void PreparesExactSnapshotAndRequiresVerifiedReceipt(
        string scenario)
    {
        var saveRoot = NewSaveRoot(scenario);
        var account = AccountRoot(saveRoot);
        WriteSelector(Path.Combine(account, "profile.save"), 1);
        WriteProfile(
            Path.Combine(account, "profile1"),
            UniqueId,
            progressMarker: "vanilla");
        WriteSelector(Path.Combine(account, "modded", "profile.save"), 1);
        WriteProfile(
            Path.Combine(account, "modded", "profile1"),
            UniqueId,
            progressMarker: "modded");

        var vanillaBefore = SnapshotTree(Path.Combine(account, "profile1"));
        var moddedBefore = SnapshotTree(Path.Combine(account, "modded", "profile1"));
        var preparation = UninstallSaveHandoff.Prepare(saveRoot, "handoff01");

        Assert(preparation.Accounts.Count == 1, "Expected one pending handoff account.");
        Assert(preparation.RequiresGameHandoff, "Usable modded progress did not require a handoff.");
        Assert(
            UninstallSaveHandoff.Inspect(preparation).State
                == UninstallSaveHandoffState.PendingGameHandoff,
            "Missing receipt did not leave the handoff pending.");

        AssertTreeExact(
            vanillaBefore,
            Path.Combine(account, "profile1"),
            "Prepare changed vanilla save bytes or timestamps.");
        AssertTreeExact(
            moddedBefore,
            Path.Combine(account, "modded", "profile1"),
            "Prepare changed modded save bytes or timestamps.");
        AssertTreeExact(
            vanillaBefore,
            Path.Combine(
                preparation.BackupDirectory,
                "snapshot",
                "steam",
                AccountId,
                "vanilla",
                "profile1"),
            "Vanilla snapshot is not exact.");
        AssertTreeExact(
            moddedBefore,
            Path.Combine(
                preparation.BackupDirectory,
                "snapshot",
                "steam",
                AccountId,
                "modded",
                "profile1"),
            "Modded snapshot is not exact.");

        var pendingAccount = preparation.Accounts.Single();
        var progressRelative = "profile1/saves/progress.save";
        var progressPath = Path.Combine(
            account,
            "profile1",
            "saves",
            "progress.save");
        WriteReceipt(
            pendingAccount,
            preparation.TransactionId,
            success: false,
            cloudStatus: "failed",
            progressRelative,
            HashFile(progressPath));
        Assert(
            UninstallSaveHandoff.Inspect(preparation).State
                == UninstallSaveHandoffState.Failed,
            "Failure receipt authorized mod removal.");

        WriteReceipt(
            pendingAccount,
            preparation.TransactionId,
            success: true,
            cloudStatus: "verified",
            progressRelative,
            HashFile(progressPath));
        var verified = UninstallSaveHandoff.Inspect(preparation);
        Assert(
            verified.State == UninstallSaveHandoffState.ReadyToRemoveMods
                && verified.MayRemoveMods,
            "A valid receipt and matching local file were not accepted.");

        File.AppendAllText(progressPath, " ");
        Assert(
            !UninstallSaveHandoff.Inspect(preparation).MayRemoveMods,
            "Receipt was trusted after its local target changed.");
    }

    private static void FailsClosedOnUnsafeSourceData(string scenario)
    {
        var mismatchRoot = NewSaveRoot(Path.Combine(scenario, "identity"));
        var mismatchAccount = AccountRoot(mismatchRoot);
        WriteProfile(
            Path.Combine(mismatchAccount, "profile1"),
            "VANILLA-ID",
            progressMarker: "vanilla");
        WriteProfile(
            Path.Combine(mismatchAccount, "modded", "profile1"),
            "MODDED-ID",
            progressMarker: "modded");
        AssertThrows<InvalidDataException>(
            () => UninstallSaveHandoff.Prepare(mismatchRoot, "mismatch01"),
            "Identity mismatch did not fail closed.");
        AssertNoProtocolMarker(mismatchAccount);

        var schema21Root = NewSaveRoot(Path.Combine(scenario, "schema21"));
        var schema21Account = AccountRoot(schema21Root);
        WriteProfile(
            Path.Combine(schema21Account, "modded", "profile1"),
            UniqueId,
            progressMarker: "modded",
            schemaVersion: 21);
        var schema21Preparation =
            UninstallSaveHandoff.Prepare(schema21Root, "schema21");
        Assert(
            schema21Preparation.RequiresGameHandoff,
            "Steam Latest schema-v21 modded progress was rejected.");

        var schema24Root = NewSaveRoot(Path.Combine(scenario, "schema24"));
        var schema24Account = AccountRoot(schema24Root);
        WriteProfile(
            Path.Combine(schema24Account, "modded", "profile1"),
            UniqueId,
            progressMarker: "modded",
            schemaVersion: 24);
        var schema24Preparation =
            UninstallSaveHandoff.Prepare(schema24Root, "schema24");
        Assert(
            schema24Preparation.RequiresGameHandoff,
            "v0.110.0 schema-v24 modded progress was rejected.");

        var schema25Root = NewSaveRoot(Path.Combine(scenario, "schema25"));
        var schema25Account = AccountRoot(schema25Root);
        WriteProfile(
            Path.Combine(schema25Account, "modded", "profile1"),
            UniqueId,
            progressMarker: "modded",
            schemaVersion: 25);
        AssertThrows<InvalidDataException>(
            () => UninstallSaveHandoff.Prepare(schema25Root, "schema25"),
            "Unknown future modded schema did not fail closed.");
        AssertNoProtocolMarker(schema25Account);

        var collisionRoot = NewSaveRoot(Path.Combine(scenario, "history"));
        var collisionAccount = AccountRoot(collisionRoot);
        var vanilla = WriteProfile(
            Path.Combine(collisionAccount, "profile1"),
            UniqueId,
            progressMarker: "vanilla");
        var modded = WriteProfile(
            Path.Combine(collisionAccount, "modded", "profile1"),
            UniqueId,
            progressMarker: "modded");
        WriteText(
            Path.Combine(vanilla, "saves", "history", "same.run"),
            """{"schema_version":22,"origin":"vanilla"}""");
        WriteText(
            Path.Combine(modded, "saves", "history", "same.run"),
            """{"schema_version":22,"origin":"modded"}""");
        AssertThrows<InvalidDataException>(
            () => UninstallSaveHandoff.Prepare(collisionRoot, "history01"),
            "Conflicting same-name history did not fail closed.");
        AssertNoProtocolMarker(collisionAccount);
    }

    private static void AcceptsOnlyKnownRemovableCurrentRunState(string scenario)
    {
        var acceptedRoot = NewSaveRoot(Path.Combine(scenario, "accepted"));
        var acceptedAccount = AccountRoot(acceptedRoot);
        var accepted = WriteProfile(
            Path.Combine(acceptedAccount, "modded", "profile1"),
            UniqueId,
            progressMarker: "modded");
        WriteText(
            Path.Combine(accepted, "saves", "current_run.save"),
            """
            {
              "schema_version": 22,
              "save_dict_TDBank.TDBankCode.Banking.AccountState": {"gold": 123},
              "nested": {
                "save_dict_List[BaseLib.Abstracts.CardModifier+ModifierSave]": []
              }
            }
            """);
        var acceptedPreparation =
            UninstallSaveHandoff.Prepare(acceptedRoot, "knownstate01");
        Assert(
            acceptedPreparation.RequiresGameHandoff,
            "Known removable TD Bank state was rejected.");

        var rejectedRoot = NewSaveRoot(Path.Combine(scenario, "rejected"));
        var rejectedAccount = AccountRoot(rejectedRoot);
        var rejected = WriteProfile(
            Path.Combine(rejectedAccount, "modded", "profile1"),
            UniqueId,
            progressMarker: "modded");
        WriteText(
            Path.Combine(rejected, "saves", "current_run.save"),
            """
            {
              "schema_version": 22,
              "save_dict_SomeOtherMod.State": {"important": true}
            }
            """);
        AssertThrows<InvalidDataException>(
            () => UninstallSaveHandoff.Prepare(rejectedRoot, "foreignstate01"),
            "Another mod's current-run state did not fail closed.");
        AssertNoProtocolMarker(rejectedAccount);
    }

    private static void RejectsUnsafeReceiptTargets(string scenario)
    {
        var saveRoot = NewSaveRoot(scenario);
        var account = AccountRoot(saveRoot);
        WriteProfile(
            Path.Combine(account, "modded", "profile1"),
            UniqueId,
            progressMarker: "modded");
        var preparation = UninstallSaveHandoff.Prepare(saveRoot, "receipt01");
        var pendingAccount = preparation.Accounts.Single();
        WriteReceipt(
            pendingAccount,
            preparation.TransactionId,
            success: true,
            cloudStatus: "verified",
            "../profile.save",
            new string('a', 64));
        Assert(
            UninstallSaveHandoff.Inspect(preparation).State
                == UninstallSaveHandoffState.Failed,
            "Traversal path in a forged receipt was accepted.");
    }

    private static void ReplacesOnlyProtocolArtifactsAfterBackingThemUp(
        string scenario)
    {
        var saveRoot = NewSaveRoot(scenario);
        var account = AccountRoot(saveRoot);
        WriteProfile(
            Path.Combine(account, "modded", "profile1"),
            UniqueId,
            progressMarker: "modded");
        var markerPath = Path.Combine(
            account,
            UninstallSaveHandoff.PendingMarkerName);
        var receiptPath = Path.Combine(
            account,
            UninstallSaveHandoff.ReceiptName);
        WriteText(markerPath, """{"old":"pending"}""");
        WriteText(receiptPath, """{"old":"receipt"}""");

        var preparation = UninstallSaveHandoff.Prepare(saveRoot, "stale01");
        Assert(
            !File.Exists(receiptPath),
            "A stale receipt remained able to satisfy a new handoff.");
        Assert(
            File.ReadAllText(markerPath).Contains(
                "\"transaction_id\": \"stale01\"",
                StringComparison.Ordinal),
            "New pending marker did not replace the stale protocol marker.");
        Assert(
            File.ReadAllText(
                    Path.Combine(
                        preparation.BackupDirectory,
                        "previous-protocol",
                        AccountId,
                        UninstallSaveHandoff.PendingMarkerName))
                == """{"old":"pending"}""",
            "Stale pending marker was not backed up.");
        Assert(
            File.ReadAllText(
                    Path.Combine(
                        preparation.BackupDirectory,
                        "previous-protocol",
                        AccountId,
                        UninstallSaveHandoff.ReceiptName))
                == """{"old":"receipt"}""",
            "Stale receipt was not backed up.");
    }

    private static void PreservesOverflowHistoryInBackupAndAllowsVerifiedCoreHandoff(
        string scenario)
    {
        var saveRoot = NewSaveRoot(scenario);
        var account = AccountRoot(saveRoot);
        WriteSelector(Path.Combine(account, "profile.save"), 1);
        WriteProfile(
            Path.Combine(account, "profile1"),
            UniqueId,
            progressMarker: "vanilla");
        WriteSelector(Path.Combine(account, "modded", "profile.save"), 1);
        var modded = WriteProfile(
            Path.Combine(account, "modded", "profile1"),
            UniqueId,
            progressMarker: "modded");
        var history = Path.Combine(modded, "saves", "history");
        for (var index = 1; index < 125; index++)
        {
            WriteText(
                Path.Combine(history, $"overflow-{index:D3}.run"),
                $$"""{"schema_version":22,"run":{{index}}}""");
        }

        var moddedBefore = SnapshotTree(modded);
        var preparation = UninstallSaveHandoff.Prepare(
            saveRoot,
            "overflowhistory01");
        AssertTreeExact(
            moddedBefore,
            Path.Combine(
                preparation.BackupDirectory,
                "snapshot",
                "steam",
                AccountId,
                "modded",
                "profile1"),
            "Overflow run history was not fully preserved in the handoff backup.");

        var progressRelative = "profile1/saves/progress.save";
        var progressPath = Path.Combine(
            account,
            "profile1",
            "saves",
            "progress.save");
        File.Copy(
            Path.Combine(modded, "saves", "progress.save"),
            progressPath,
            overwrite: true);
        var pendingAccount = preparation.Accounts.Single();
        WriteReceipt(
            pendingAccount,
            preparation.TransactionId,
            success: true,
            cloudStatus: "verified",
            progressRelative,
            HashFile(progressPath));
        Assert(
            UninstallSaveHandoff.Inspect(preparation).MayRemoveMods,
            "A verified core handoff was rejected because backup-only history exceeds cloud quota.");
        Assert(
            Directory.EnumerateFiles(
                    Path.Combine(
                        preparation.BackupDirectory,
                        "snapshot",
                        "steam",
                        AccountId,
                        "modded",
                        "profile1",
                        "saves",
                        "history"),
                    "*.run",
                    SearchOption.TopDirectoryOnly)
                .Count() == 125,
            "The complete overflow history is not recoverable from the verified backup.");
    }

    private static string NewSaveRoot(string scenario)
    {
        Directory.CreateDirectory(scenario);
        return scenario;
    }

    private static string AccountRoot(string saveRoot)
    {
        var root = Path.Combine(saveRoot, "steam", AccountId);
        Directory.CreateDirectory(root);
        return root;
    }

    private static string WriteProfile(
        string profileRoot,
        string uniqueId,
        string progressMarker,
        int schemaVersion = 22)
    {
        var saves = Path.Combine(profileRoot, "saves");
        Directory.CreateDirectory(Path.Combine(saves, "history"));
        WriteText(
            Path.Combine(saves, "progress.save"),
            JsonSerializer.Serialize(new
            {
                schema_version = schemaVersion,
                unique_id = uniqueId,
                test_marker = progressMarker,
            }));
        WriteText(
            Path.Combine(saves, "prefs.save"),
            """{"schema_version":22,"volume":0.5}""");
        WriteText(
            Path.Combine(saves, "history", $"{progressMarker}.run"),
            $$"""{"schema_version":22,"origin":"{{progressMarker}}"}""");
        return profileRoot;
    }

    private static void WriteSelector(string path, int profileId)
    {
        WriteText(path, $$"""{"selected_profile":{{profileId}}}""");
    }

    private static void WriteReceipt(
        UninstallSaveHandoffAccount account,
        string transactionId,
        bool success,
        string cloudStatus,
        string targetRelative,
        string hash)
    {
        WriteText(
            account.ReceiptPath,
            JsonSerializer.Serialize(
                new
                {
                    schema_version = UninstallSaveHandoff.SchemaVersion,
                    protocol = UninstallSaveHandoff.Protocol,
                    mod_id = "TDBank",
                    transaction_id = transactionId,
                    account_id = account.AccountId,
                    marker_sha256 = account.MarkerSha256,
                    completed_utc = "2030-01-01T00:00:00.0000000Z",
                    success,
                    cloud_status = cloudStatus,
                    files = new[]
                    {
                        new
                        {
                            target_relative_path = targetRelative,
                            sha256 = hash,
                        },
                    },
                }));
    }

    private static Dictionary<string, FileSnapshot> SnapshotTree(string root)
    {
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetRelativePath(root, path),
                path => new FileSnapshot(
                    File.ReadAllBytes(path),
                    File.GetLastWriteTimeUtc(path)),
                StringComparer.OrdinalIgnoreCase);
    }

    private static void AssertTreeExact(
        IReadOnlyDictionary<string, FileSnapshot> expected,
        string root,
        string message)
    {
        var actual = SnapshotTree(root);
        Assert(
            actual.Keys.Order(StringComparer.OrdinalIgnoreCase)
                .SequenceEqual(
                    expected.Keys.Order(StringComparer.OrdinalIgnoreCase),
                    StringComparer.OrdinalIgnoreCase),
            message + " File inventory differs.");
        foreach (var pair in expected)
        {
            Assert(
                actual.TryGetValue(pair.Key, out var found)
                    && found.Bytes.SequenceEqual(pair.Value.Bytes)
                    && found.LastWriteTimeUtc == pair.Value.LastWriteTimeUtc,
                message + $" File differs: {pair.Key}");
        }
    }

    private static void AssertNoProtocolMarker(string accountRoot)
    {
        Assert(
            !File.Exists(
                Path.Combine(
                    accountRoot,
                    UninstallSaveHandoff.PendingMarkerName)),
            "A failed prepare left a pending marker.");
    }

    private static void WriteText(string path, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents, new UTF8Encoding(false));
        File.SetLastWriteTimeUtc(
            path,
            new DateTime(2024, 1, 2, 3, 4, 6, DateTimeKind.Utc));
    }

    private static string HashFile(string path)
    {
        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
            .ToLowerInvariant();
    }

    private static void AssertThrows<TException>(
        Action action,
        string message)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException(message);
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
        DateTime LastWriteTimeUtc);
}
