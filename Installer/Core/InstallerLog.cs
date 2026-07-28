using System.Text;

namespace CNJ.TowerDebt.Setup.Core;

internal static class InstallerLog
{
    private static readonly object Gate = new();
    private static string? _path;

    public static string Path
    {
        get
        {
            lock (Gate)
            {
                try
                {
                    _path ??= CreatePath();
                    return _path;
                }
                catch (Exception exception)
                {
                    return $"Log unavailable ({exception.GetType().Name}: {exception.Message})";
                }
            }
        }
    }

    public static void Write(string message)
    {
        lock (Gate)
        {
            _path ??= CreatePath();
            var line = $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}";
            File.AppendAllText(_path, line, new UTF8Encoding(false));
        }
    }

    public static void TryWrite(string message)
    {
        try
        {
            Write(message);
        }
        catch
        {

        }
    }

    private static string CreatePath()
    {
        var directory = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CNJ",
            "TowerDebt",
            "Logs");
        Directory.CreateDirectory(directory);
        return System.IO.Path.Combine(directory, $"setup-{DateTime.Now:yyyyMMdd-HHmmss}.log");
    }
}
