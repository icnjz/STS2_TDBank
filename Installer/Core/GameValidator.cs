using System.Text.Json;

namespace CNJ.TowerDebt.Setup.Core;

internal static class GameValidator
{
    public const string MinimumVersion = "v0.107.1";
    public const string LatestVerifiedVersion = "v0.110.0";
    public const string SupportedVersionSummary = "v0.107.1+ (LTS)";

    private static readonly Version MinimumSemanticVersion = new(0, 107, 1);

    private static readonly HashSet<string> VerifiedVersions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "v0.107.1",
            "v0.109.1",
            LatestVerifiedVersion,
        };

    public static bool IsSupportedProgressSchema(int schemaVersion)
        => schemaVersion is >= 21 and <= 24;

    public static GameValidation Validate(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return new(false, false, null, null, null, ValidationStatus.NoDirectory);
        }

        string root;
        try
        {
            root = Path.GetFullPath(directory.Trim().Trim('"'));
        }
        catch
        {
            return new(false, false, null, null, null, ValidationStatus.InvalidPath);
        }

        var executable = Path.Combine(root, "SlayTheSpire2.exe");
        var releaseInfo = Path.Combine(root, "release_info.json");
        var dataDirectory = Path.Combine(root, "data_sts2_windows_x86_64");
        if (!File.Exists(executable) || !File.Exists(releaseInfo) || !Directory.Exists(dataDirectory))
        {
            return new(
                false,
                false,
                null,
                null,
                null,
                ValidationStatus.MissingGameFiles);
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(releaseInfo));
            var rootElement = document.RootElement;
            var version = ReadString(rootElement, "version");
            var branch = ReadString(rootElement, "branch");
            var commit = ReadString(rootElement, "commit");
            var parsed = TryParseGameVersion(version, out var semanticVersion);
            var supported = parsed && semanticVersion >= MinimumSemanticVersion;
            var status = supported
                ? VerifiedVersions.Contains(version!)
                    ? ValidationStatus.Supported
                    : ValidationStatus.ForwardCompatible
                : ValidationStatus.UnsupportedVersion;
            return new(
                true,
                supported,
                version,
                branch,
                commit,
                status);
        }
        catch (Exception exception)
        {
            return new(
                false,
                false,
                null,
                null,
                null,
                ValidationStatus.ReleaseInfoUnreadable,
                exception.Message);
        }
    }

    private static string? ReadString(JsonElement root, string property)
    {
        return root.TryGetProperty(property, out var value) ? value.GetString() : null;
    }

    private static bool TryParseGameVersion(
        string? value,
        out Version semanticVersion)
    {
        semanticVersion = new Version();
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();
        if (normalized.StartsWith('v'))
        {
            normalized = normalized[1..];
        }

        var suffix = normalized.IndexOfAny(['-', '+']);
        if (suffix >= 0)
        {
            normalized = normalized[..suffix];
        }

        if (!Version.TryParse(normalized, out var parsed))
        {
            return false;
        }

        semanticVersion = parsed;
        return true;
    }
}
