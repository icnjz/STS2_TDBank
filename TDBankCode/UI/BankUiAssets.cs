using System;
using System.Collections.Generic;
using System.IO;
using Godot;

namespace TDBank.TDBankCode.UI;

internal static class BankUiAssets
{
    private static readonly Dictionary<string, Texture2D?> TextureCache =
        new(StringComparer.OrdinalIgnoreCase);

    public static Texture2D? Logo => LoadTexture("bank_logo.png");

    public static Texture2D? Background => LoadTexture("bank_background.png");

    public static Texture2D? Card(BankCreditTier tier)
    {
        var localizedFileName = LocalizedCardFileName(
            tier,
            BankUiText.IsChinese);
        if (localizedFileName is null)
        {
            return null;
        }

        return LoadTexture(localizedFileName);
    }

    internal static string? LocalizedCardFileName(
        BankCreditTier tier,
        bool isChinese)
    {
        var languageSuffix = isChinese ? "zh" : "en";
        return tier switch
        {
            BankCreditTier.Starter => $"visa_broke_{languageSuffix}.png",
            BankCreditTier.MiddleClass => $"visa_middle_{languageSuffix}.png",
            BankCreditTier.NouveauRiche => $"visa_rich_{languageSuffix}.png",
            _ => null,
        };
    }

    private static Texture2D? LoadTexture(string fileName)
    {
        if (TextureCache.TryGetValue(fileName, out var cached))
        {
            return cached;
        }

        var path = Path.Combine(AssetsDirectory(), fileName);
        Texture2D? texture = null;

        try
        {
            if (!File.Exists(path))
            {
                MainFile.Logger.Warn($"Optional TD Bank artwork is missing; using code fallback: {path}");
                TextureCache[fileName] = null;
                return null;
            }

            var bytes = File.ReadAllBytes(path);
            using var image = new Image();
            var error = DecodeImage(image, bytes, path);
            if (error != Error.Ok || image.IsEmpty())
            {
                MainFile.Logger.Warn(
                    $"Could not decode optional TD Bank artwork '{path}' ({error}); using code fallback.");
            }
            else
            {
                texture = ImageTexture.CreateFromImage(image);
                MainFile.Logger.Info(
                    $"Loaded TD Bank artwork: {fileName} ({image.GetWidth()}x{image.GetHeight()}).");
            }
        }
        catch (Exception exception)
        {
            MainFile.Logger.Warn(
                $"Could not load optional TD Bank artwork '{path}'; using code fallback. {exception.Message}");
        }

        TextureCache[fileName] = texture;
        return texture;
    }

    private static Error DecodeImage(Image image, byte[] bytes, string path)
    {
        if (LooksLikePng(bytes))
        {
            return image.LoadPngFromBuffer(bytes);
        }

        if (LooksLikeJpeg(bytes))
        {
            return image.LoadJpgFromBuffer(bytes);
        }


        return image.Load(path);
    }

    private static bool LooksLikePng(IReadOnlyList<byte> bytes)
    {
        return bytes.Count >= 8
            && bytes[0] == 0x89
            && bytes[1] == 0x50
            && bytes[2] == 0x4E
            && bytes[3] == 0x47
            && bytes[4] == 0x0D
            && bytes[5] == 0x0A
            && bytes[6] == 0x1A
            && bytes[7] == 0x0A;
    }

    private static bool LooksLikeJpeg(IReadOnlyList<byte> bytes)
    {
        return bytes.Count >= 3
            && bytes[0] == 0xFF
            && bytes[1] == 0xD8
            && bytes[2] == 0xFF;
    }

    private static string AssetsDirectory()
    {
        var assemblyPath = typeof(MainFile).Assembly.Location;
        var modDirectory = Path.GetDirectoryName(assemblyPath);
        return Path.Combine(modDirectory ?? AppContext.BaseDirectory, "Assets");
    }
}
