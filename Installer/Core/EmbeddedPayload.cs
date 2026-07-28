using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace CNJ.TowerDebt.Setup.Core;

internal static class EmbeddedPayload
{
    public static readonly Version RequiredTDLibVersion = new(0, 1, 0);

    public static readonly IReadOnlyList<PayloadFile> Files =
    [
        new(
            "CNJ.TowerDebt.Setup.Payload.TDBank.TDBank.dll",
            Path.Combine("TDBank", "TDBank.dll"),
            false),
        new(
            "CNJ.TowerDebt.Setup.Payload.TDBank.TDBank.json",
            Path.Combine("TDBank", "TDBank.json"),
            false),
        new(
            "CNJ.TowerDebt.Setup.Payload.TDBank.Assets.bank_logo.png",
            Path.Combine("TDBank", "Assets", "bank_logo.png"),
            false),
        new(
            "CNJ.TowerDebt.Setup.Payload.TDBank.Assets.bank_background.png",
            Path.Combine("TDBank", "Assets", "bank_background.png"),
            false),
        new(
            "CNJ.TowerDebt.Setup.Payload.TDBank.Assets.visa_broke_zh.png",
            Path.Combine("TDBank", "Assets", "visa_broke_zh.png"),
            false),
        new(
            "CNJ.TowerDebt.Setup.Payload.TDBank.Assets.visa_middle_zh.png",
            Path.Combine("TDBank", "Assets", "visa_middle_zh.png"),
            false),
        new(
            "CNJ.TowerDebt.Setup.Payload.TDBank.Assets.visa_rich_zh.png",
            Path.Combine("TDBank", "Assets", "visa_rich_zh.png"),
            false),
        new(
            "CNJ.TowerDebt.Setup.Payload.TDBank.Assets.visa_broke_en.png",
            Path.Combine("TDBank", "Assets", "visa_broke_en.png"),
            false),
        new(
            "CNJ.TowerDebt.Setup.Payload.TDBank.Assets.visa_middle_en.png",
            Path.Combine("TDBank", "Assets", "visa_middle_en.png"),
            false),
        new(
            "CNJ.TowerDebt.Setup.Payload.TDBank.Assets.visa_rich_en.png",
            Path.Combine("TDBank", "Assets", "visa_rich_en.png"),
            false),
        new(
            "CNJ.TowerDebt.Setup.Payload.TDLib.TDLib.dll",
            Path.Combine("TDLib", "TDLib.dll"),
            true),
        new(
            "CNJ.TowerDebt.Setup.Payload.TDLib.TDLib.json",
            Path.Combine("TDLib", "TDLib.json"),
            true),
        new(
            "CNJ.TowerDebt.Setup.Payload.TDLib.THIRD_PARTY_LICENSES.BaseLib-LICENSE.txt",
            Path.Combine("TDLib", "THIRD_PARTY_LICENSES", "BaseLib-LICENSE.txt"),
            true),
    ];

    public static byte[] Read(PayloadFile file)
    {
        var assembly = typeof(EmbeddedPayload).Assembly;
        using var stream = assembly.GetManifestResourceStream(file.ResourceName)
            ?? throw new InstallerOperationException(
                InstallerErrorCode.MissingEmbeddedResource,
                targetPath: file.ResourceName);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    public static string ReadDependencyLicense()
    {
        var license = Files.Single(file =>
            file.RelativePath.EndsWith("BaseLib-LICENSE.txt", StringComparison.OrdinalIgnoreCase));
        return Encoding.UTF8.GetString(Read(license));
    }

    public static string Hash(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    public static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public static bool Matches(PayloadFile file, string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        var embeddedHash = Hash(Read(file));
        var installedHash = HashFile(path);
        return string.Equals(embeddedHash, installedHash, StringComparison.OrdinalIgnoreCase);
    }

    public static Image ReadLogoImage()
    {
        var logo = Files.Single(file =>
            file.RelativePath.EndsWith("bank_logo.png", StringComparison.OrdinalIgnoreCase));
        using var stream = new MemoryStream(Read(logo));
        using var source = Image.FromStream(stream);
        return new Bitmap(source);
    }
}
