using System.Text.Json;

namespace CNJ.TowerDebt.Setup.Core;

internal static class TDLibOwnership
{
    internal const int SchemaVersion = 1;

    internal static bool StateClaimsManaged(string tdBankDirectory)
    {
        try
        {
            var statePath = Path.Combine(tdBankDirectory, "install-state.json");
            using var document = JsonDocument.Parse(File.ReadAllText(statePath));
            var root = document.RootElement;
            if (!string.Equals(
                    root.GetProperty("installer").GetString(),
                    "CNJ Tower Debt Setup",
                    StringComparison.Ordinal))
            {
                return false;
            }

            if (root.TryGetProperty("tdLibOwnership", out var ownership))
            {
                return ReadCurrentOwnershipClaim(root, ownership);
            }




            return false;
        }
        catch
        {
            return false;
        }
    }

    internal static bool CanRemoveManagedPayload(
        string tdBankDirectory,
        string tdLibDirectory)
    {
        return StateClaimsManaged(tdBankDirectory)
            && IsExactEmbeddedPayload(tdLibDirectory);
    }

    internal static bool IsExactEmbeddedPayload(string tdLibDirectory)
    {
        try
        {
            if (!Directory.Exists(tdLibDirectory)
                || IsReparsePoint(tdLibDirectory)
                || !HasTDLibManifestIdentity(tdLibDirectory))
            {
                return false;
            }

            var expectedFiles = ExpectedPayloadFiles();
            var expectedDirectories = ExpectedPayloadDirectories(expectedFiles.Keys);
            var actualFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var actualDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pending = new Stack<string>();
            pending.Push(tdLibDirectory);
            while (pending.Count > 0)
            {
                var directory = pending.Pop();
                if (IsReparsePoint(directory))
                {
                    return false;
                }

                foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
                {
                    entry.Refresh();
                    if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        return false;
                    }

                    var relative = NormalizeRelativePath(
                        Path.GetRelativePath(tdLibDirectory, entry.FullName));
                    if ((entry.Attributes & FileAttributes.Directory) != 0)
                    {
                        if (!expectedDirectories.Contains(relative)
                            || !actualDirectories.Add(relative))
                        {
                            return false;
                        }
                        pending.Push(entry.FullName);
                    }
                    else if (!expectedFiles.ContainsKey(relative)
                             || !actualFiles.Add(relative))
                    {
                        return false;
                    }
                }
            }

            if (!actualFiles.SetEquals(expectedFiles.Keys)
                || !actualDirectories.SetEquals(expectedDirectories))
            {
                return false;
            }

            return expectedFiles.All(pair =>
                EmbeddedPayload.Matches(
                    pair.Value,
                    Path.Combine(
                        tdLibDirectory,
                        pair.Key.Replace('/', Path.DirectorySeparatorChar))));
        }
        catch
        {
            return false;
        }
    }

    internal static IReadOnlyList<TDLibPayloadProof> CreatePayloadProof()
    {
        return EmbeddedPayload.Files
            .Where(file => file.IsTDLib)
            .Select(file => new TDLibPayloadProof(
                NormalizeRelativePath(
                    Path.GetRelativePath("TDLib", file.RelativePath)),
                EmbeddedPayload.Hash(EmbeddedPayload.Read(file))))
            .OrderBy(
                entry => entry.RelativePath,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool ReadCurrentOwnershipClaim(
        JsonElement stateRoot,
        JsonElement ownership)
    {
        if (ownership.ValueKind != JsonValueKind.Object
            || ownership.GetProperty("schemaVersion").GetInt32() != SchemaVersion
            || !ownership.GetProperty("managedBySetup").GetBoolean()
            || !string.Equals(
                ownership.GetProperty("payloadVersion").GetString(),
                EmbeddedPayload.RequiredTDLibVersion.ToString(),
                StringComparison.Ordinal))
        {
            return false;
        }

        var rootAction = stateRoot.GetProperty("tdLibAction").GetString();
        var ownershipAction =
            ownership.GetProperty("actionAtThisInstall").GetString();
        if (!string.Equals(rootAction, ownershipAction, StringComparison.Ordinal)
            || ownershipAction is not (
                nameof(TDLibInstallAction.Install)
                or nameof(TDLibInstallAction.UpgradeOrRepair)
                or nameof(TDLibInstallAction.PreserveExact)))
        {
            return false;
        }

        var expected = ExpectedPayloadFiles()
            .ToDictionary(
                pair => pair.Key,
                pair => EmbeddedPayload.Hash(EmbeddedPayload.Read(pair.Value)),
                StringComparer.OrdinalIgnoreCase);
        var recorded = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in ownership.GetProperty("payloadFiles").EnumerateArray())
        {
            var relativePath = NormalizeRelativePath(
                item.GetProperty("relativePath").GetString() ?? string.Empty);
            var hash = item.GetProperty("sha256").GetString() ?? string.Empty;
            if (Path.IsPathRooted(relativePath)
                || relativePath.Length == 0
                || relativePath.Split('/').Any(part => part is "" or "." or "..")
                || !recorded.TryAdd(relativePath, hash))
            {
                return false;
            }
        }

        return recorded.Count == expected.Count
            && expected.All(pair =>
                recorded.TryGetValue(pair.Key, out var hash)
                && string.Equals(hash, pair.Value, StringComparison.OrdinalIgnoreCase));
    }

    private static Dictionary<string, PayloadFile> ExpectedPayloadFiles()
    {
        return EmbeddedPayload.Files
            .Where(file => file.IsTDLib)
            .ToDictionary(
                file => NormalizeRelativePath(
                    Path.GetRelativePath("TDLib", file.RelativePath)),
                file => file,
                StringComparer.OrdinalIgnoreCase);
    }

    private static HashSet<string> ExpectedPayloadDirectories(
        IEnumerable<string> expectedFiles)
    {
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var expectedFile in expectedFiles)
        {
            var parent = Path.GetDirectoryName(
                expectedFile.Replace('/', Path.DirectorySeparatorChar));
            while (!string.IsNullOrWhiteSpace(parent) && parent != ".")
            {
                directories.Add(NormalizeRelativePath(parent));
                parent = Path.GetDirectoryName(parent);
            }
        }
        return directories;
    }

    private static bool HasTDLibManifestIdentity(string tdLibDirectory)
    {
        using var manifest = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(tdLibDirectory, "TDLib.json")));
        return string.Equals(
            manifest.RootElement.GetProperty("id").GetString(),
            "TDLib",
            StringComparison.Ordinal);
    }

    private static bool IsReparsePoint(string path)
    {
        return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    }

    private static string NormalizeRelativePath(string path)
    {
        return path.Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
    }

    internal sealed record TDLibPayloadProof(
        string RelativePath,
        string Sha256);
}
