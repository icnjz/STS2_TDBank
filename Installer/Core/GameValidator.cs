using System.Text.Json;

namespace CNJ.TowerDebt.Setup.Core;

internal static class GameValidator
{
    public const string SupportedVersion = "v0.109.1";

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
            var supported = string.Equals(version, SupportedVersion, StringComparison.OrdinalIgnoreCase);
            return new(
                true,
                supported,
                version,
                branch,
                commit,
                supported ? ValidationStatus.Supported : ValidationStatus.UnsupportedVersion);
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
}
