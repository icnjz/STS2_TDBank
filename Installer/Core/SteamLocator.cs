using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace CNJ.TowerDebt.Setup.Core;

internal static partial class SteamLocator
{
    private const string AppId = "2868840";

    public static IReadOnlyList<string> FindGameDirectories()
    {
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var direct in ReadDirectInstallLocations())
        {
            AddIfGameDirectory(results, direct);
        }

        foreach (var steamRoot in FindSteamRoots())
        {
            foreach (var library in ReadSteamLibraries(steamRoot))
            {
                var steamApps = Path.Combine(library, "steamapps");
                var manifest = Path.Combine(steamApps, $"appmanifest_{AppId}.acf");
                if (File.Exists(manifest))
                {
                    var installDir = ReadAcfValue(manifest, "installdir");
                    if (!string.IsNullOrWhiteSpace(installDir))
                    {
                        AddIfGameDirectory(results, Path.Combine(steamApps, "common", installDir));
                    }
                }

                AddIfGameDirectory(results, Path.Combine(steamApps, "common", "Slay the Spire 2"));
            }
        }

        return results.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IEnumerable<string> ReadDirectInstallLocations()
    {
        const string subkey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 2868840";
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            string? path = null;
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = baseKey.OpenSubKey(subkey);
                path = key?.GetValue("InstallLocation") as string;
            }
            catch
            {

            }

            if (!string.IsNullOrWhiteSpace(path))
            {
                yield return path;
            }
        }
    }

    private static IEnumerable<string> FindSteamRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddRegistryValue(roots, Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath");
        AddRegistryValue(roots, Registry.CurrentUser, @"Software\Valve\Steam", "InstallPath");

        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = baseKey.OpenSubKey(@"SOFTWARE\Valve\Steam");
                AddPath(roots, key?.GetValue("InstallPath") as string);
            }
            catch
            {

            }
        }

        AddPath(roots, Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Steam"));

        return roots;
    }

    private static IEnumerable<string> ReadSteamLibraries(string steamRoot)
    {
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddPath(libraries, steamRoot);

        var vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf))
        {
            return libraries;
        }

        try
        {
            var text = File.ReadAllText(vdf);
            foreach (Match match in VdfPathRegex().Matches(text))
            {
                var path = match.Groups["path"].Value
                    .Replace(@"\\", @"\")
                    .Replace(@"\/", @"/");
                AddPath(libraries, path);
            }
        }
        catch
        {

        }

        return libraries;
    }

    private static string? ReadAcfValue(string path, string key)
    {
        try
        {
            var text = File.ReadAllText(path);
            var match = Regex.Match(
                text,
                $"\"{Regex.Escape(key)}\"\\s+\"(?<value>[^\"]+)\"",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return match.Success ? match.Groups["value"].Value : null;
        }
        catch
        {
            return null;
        }
    }

    private static void AddRegistryValue(HashSet<string> paths, RegistryKey hive, string subkey, string name)
    {
        try
        {
            using var key = hive.OpenSubKey(subkey);
            AddPath(paths, key?.GetValue(name) as string);
        }
        catch
        {

        }
    }

    private static void AddPath(HashSet<string> paths, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var fullPath = Path.GetFullPath(path.Trim().Trim('"'));
            if (Directory.Exists(fullPath))
            {
                paths.Add(fullPath);
            }
        }
        catch
        {

        }
    }

    private static void AddIfGameDirectory(HashSet<string> results, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            if (File.Exists(Path.Combine(fullPath, "SlayTheSpire2.exe")))
            {
                results.Add(fullPath);
            }
        }
        catch
        {

        }
    }

    [GeneratedRegex("\"path\"\\s+\"(?<path>(?:\\\\\\\\|\\\\/|[^\"])*)\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VdfPathRegex();
}
