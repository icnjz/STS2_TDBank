using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace CNJ.TowerDebt.Tools.ArtworkAssetConverter;

internal static class Program
{
    private static readonly byte[] JpegMagic = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly AssetRule[] Rules =
    [
        new("bank_background.png", ConversionMode.OpaquePng, 0, 0, 0, 0),
        new("bank_logo.png", ConversionMode.TransparentBorderMatte, 16, 55, 3, 0),
        new("bisa_broke_en.png", ConversionMode.TransparentRoundedCorners, 18, 55, 4, 42),
        new("bisa_middle_en.png", ConversionMode.TransparentRoundedCorners, 12, 50, 4, 42),
    ];

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var workspace = ResolveWorkspace(args);
            var referenceAssets = args.Length == 2
                ? Path.GetFullPath(args[1])
                : null;
            var runtimeAssets = Path.Combine(workspace, "TDBank", "Assets");
            var installerAssets = Path.Combine(
                workspace,
                "Installer",
                "Payload",
                "TDBank",
                "Assets");

            EnsureDirectory(runtimeAssets);
            EnsureDirectory(installerAssets);

            foreach (var rule in Rules)
            {
                ConvertAndSynchronize(rule, runtimeAssets, installerAssets);
            }

            ValidateAllAssets(runtimeAssets, installerAssets, referenceAssets);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static string ResolveWorkspace(string[] args)
    {
        if (args.Length > 2)
        {
            throw new ArgumentException(
                "Usage: ArtworkAssetConverter [workspace-root] [reference-assets-directory]");
        }

        return Path.GetFullPath(args.Length == 1
            ? args[0]
            : Directory.GetCurrentDirectory());
    }

    private static void ConvertAndSynchronize(
        AssetRule rule,
        string runtimeAssets,
        string installerAssets)
    {
        var runtimePath = Path.Combine(runtimeAssets, rule.FileName);
        var installerPath = Path.Combine(installerAssets, rule.FileName);
        EnsureFile(runtimePath);
        EnsureFile(installerPath);

        if (!HashesEqual(runtimePath, installerPath))
        {
            throw new InvalidOperationException(
                $"Refusing to choose between different runtime and installer assets: {rule.FileName}");
        }

        var originalHeader = ReadHeader(runtimePath, PngMagic.Length);
        if (originalHeader.AsSpan().StartsWith(JpegMagic))
        {
            PixelBuffer pixels;
            using (var decoded = new Bitmap(runtimePath))
            {
                pixels = PixelBuffer.Decode(decoded);
            }
            var originalWidth = pixels.Width;
            var originalHeight = pixels.Height;

            MatteResult matte = MatteResult.None;
            if (rule.Mode == ConversionMode.TransparentBorderMatte)
            {
                matte = RemoveConnectedBorderMatte(
                    pixels,
                    rule.CoreTolerance,
                    rule.SoftTolerance,
                    rule.FeatherRadius);
            }
            else if (rule.Mode == ConversionMode.TransparentRoundedCorners)
            {
                matte = RemoveRoundedCornerMatte(
                    pixels,
                    rule.CoreTolerance,
                    rule.SoftTolerance,
                    rule.FeatherRadius,
                    rule.CornerRadius);
            }
            else
            {
                pixels.ForceOpaque();
            }

            SavePngAtomically(
                runtimePath,
                pixels,
                includeAlpha: rule.Mode is ConversionMode.TransparentBorderMatte
                    or ConversionMode.TransparentRoundedCorners);

            using var verification = new Bitmap(runtimePath);
            if (verification.Width != originalWidth || verification.Height != originalHeight)
            {
                throw new InvalidDataException(
                    $"Dimensions changed during conversion: {rule.FileName}");
            }

            Console.WriteLine(
                $"CONVERTED {rule.FileName}: {originalWidth}x{originalHeight}; " +
                $"transparent={matte.TransparentPixels}; partial={matte.PartialPixels}");
        }
        else if (originalHeader.AsSpan().SequenceEqual(PngMagic))
        {
            Console.WriteLine($"UNCHANGED {rule.FileName}: already true PNG");
        }
        else
        {
            throw new InvalidDataException(
                $"Unsupported source encoding for {rule.FileName}: " +
                Convert.ToHexString(originalHeader));
        }

        if (!HashesEqual(runtimePath, installerPath))
        {
            File.Copy(runtimePath, installerPath, overwrite: true);
        }
        if (!HashesEqual(runtimePath, installerPath))
        {
            throw new IOException(
                $"Runtime and installer assets differ after synchronization: {rule.FileName}");
        }
    }

    private static MatteResult RemoveConnectedBorderMatte(
        PixelBuffer pixels,
        int coreTolerance,
        int softTolerance,
        int featherRadius)
    {
        if (coreTolerance <= 0
            || softTolerance <= coreTolerance
            || featherRadius < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(coreTolerance),
                "Transparent-matte tolerances are invalid.");
        }

        var width = pixels.Width;
        var height = pixels.Height;
        var pixelCount = checked(width * height);
        var cornerSize = Math.Clamp(Math.Min(width, height) / 48, 6, 18);
        var corners = SampleCornerColors(pixels, cornerSize);
        var core = new bool[pixelCount];
        var queue = new Queue<int>();

        SeedCorner(0, 0);
        SeedCorner(width - cornerSize, 0);
        SeedCorner(0, height - cornerSize);
        SeedCorner(width - cornerSize, height - cornerSize);

        while (queue.Count > 0)
        {
            var index = queue.Dequeue();
            var x = index % width;
            var y = index / width;
            TryAdd(x - 1, y);
            TryAdd(x + 1, y);
            TryAdd(x, y - 1);
            TryAdd(x, y + 1);
        }

        var transparentPixels = 0;
        for (var index = 0; index < pixelCount; index++)
        {
            if (!core[index])
            {
                continue;
            }

            pixels.SetAlpha(index, 0);
            transparentPixels++;
        }

        var layer = new byte[pixelCount];
        var featherQueue = new Queue<int>();
        for (var index = 0; index < pixelCount; index++)
        {
            if (!core[index])
            {
                continue;
            }

            var x = index % width;
            var y = index / width;
            SeedFeather(x - 1, y);
            SeedFeather(x + 1, y);
            SeedFeather(x, y - 1);
            SeedFeather(x, y + 1);
        }

        while (featherQueue.Count > 0)
        {
            var index = featherQueue.Dequeue();
            var currentLayer = layer[index];
            if (currentLayer >= featherRadius)
            {
                continue;
            }

            var x = index % width;
            var y = index / width;
            GrowFeather(x - 1, y, currentLayer + 1);
            GrowFeather(x + 1, y, currentLayer + 1);
            GrowFeather(x, y - 1, currentLayer + 1);
            GrowFeather(x, y + 1, currentLayer + 1);
        }

        var partialPixels = 0;
        for (var index = 0; index < pixelCount; index++)
        {
            var currentLayer = layer[index];
            if (currentLayer == 0 || core[index])
            {
                continue;
            }

            var x = index % width;
            var y = index / width;
            var score = DistanceFromExpectedMatte(pixels, x, y, corners);
            if (score > softTolerance)
            {
                continue;
            }

            var colorAlpha = (int)Math.Round(
                255d * (score - coreTolerance) / (softTolerance - coreTolerance));
            var layerAlpha = (int)Math.Round(
                255d * currentLayer / (featherRadius + 1d));
            var alpha = (byte)Math.Clamp(
                Math.Max(colorAlpha, layerAlpha),
                1,
                254);
            if (alpha < pixels.GetAlpha(index))
            {
                pixels.SetAlpha(index, alpha);
            }
            if (pixels.GetAlpha(index) is > 0 and < 255)
            {
                partialPixels++;
            }
        }

        return new MatteResult(transparentPixels, partialPixels);

        void SeedCorner(int startX, int startY)
        {
            for (var y = startY; y < startY + cornerSize; y++)
            {
                for (var x = startX; x < startX + cornerSize; x++)
                {
                    TryAdd(x, y);
                }
            }
        }

        void TryAdd(int x, int y)
        {
            if ((uint)x >= (uint)width || (uint)y >= (uint)height)
            {
                return;
            }
            var index = y * width + x;
            if (core[index])
            {
                return;
            }

            if (pixels.GetAlpha(index) == 0
                || DistanceFromExpectedMatte(pixels, x, y, corners) <= coreTolerance)
            {
                core[index] = true;
                queue.Enqueue(index);
            }
        }

        void SeedFeather(int x, int y)
        {
            GrowFeather(x, y, 1);
        }

        void GrowFeather(int x, int y, int requestedLayer)
        {
            if ((uint)x >= (uint)width
                || (uint)y >= (uint)height
                || requestedLayer > featherRadius)
            {
                return;
            }

            var index = y * width + x;
            if (core[index]
                || (layer[index] != 0 && layer[index] <= requestedLayer))
            {
                return;
            }

            if (DistanceFromExpectedMatte(pixels, x, y, corners) > softTolerance)
            {
                return;
            }

            layer[index] = (byte)requestedLayer;
            featherQueue.Enqueue(index);
        }
    }

    private static MatteResult RemoveRoundedCornerMatte(
        PixelBuffer pixels,
        int coreTolerance,
        int softTolerance,
        int featherRadius,
        int cornerRadius)
    {
        if (cornerRadius <= featherRadius
            || coreTolerance <= 0
            || softTolerance <= coreTolerance
            || featherRadius < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cornerRadius),
                "Rounded-corner matte settings are invalid.");
        }

        var corners = SampleCornerColors(
            pixels,
            Math.Clamp(cornerRadius / 2, 6, 18));
        var transparent = 0;
        var partial = 0;
        for (var y = 0; y < pixels.Height; y++)
        {
            for (var x = 0; x < pixels.Width; x++)
            {
                var cornerX = x < cornerRadius
                    ? cornerRadius - 0.5
                    : x >= pixels.Width - cornerRadius
                        ? pixels.Width - cornerRadius - 0.5
                        : double.NaN;
                var cornerY = y < cornerRadius
                    ? cornerRadius - 0.5
                    : y >= pixels.Height - cornerRadius
                        ? pixels.Height - cornerRadius - 0.5
                        : double.NaN;
                if (double.IsNaN(cornerX) || double.IsNaN(cornerY))
                {
                    continue;
                }

                var deltaX = x - cornerX;
                var deltaY = y - cornerY;
                var signedDistance =
                    Math.Sqrt(deltaX * deltaX + deltaY * deltaY) - cornerRadius;
                if (signedDistance <= -featherRadius)
                {
                    continue;
                }

                var score = DistanceFromExpectedMatte(pixels, x, y, corners);
                if (score > softTolerance)
                {
                    continue;
                }

                var geometryAlpha = (int)Math.Round(
                    255d * (featherRadius - signedDistance) / (2d * featherRadius));
                var colorAlpha = (int)Math.Round(
                    255d * (score - coreTolerance) / (softTolerance - coreTolerance));
                var alpha = (byte)Math.Clamp(
                    Math.Max(geometryAlpha, colorAlpha),
                    0,
                    255);
                var index = y * pixels.Width + x;
                if (alpha < pixels.GetAlpha(index))
                {
                    pixels.SetAlpha(index, alpha);
                }

                if (pixels.GetAlpha(index) == 0)
                {
                    transparent++;
                }
                else if (pixels.GetAlpha(index) < 255)
                {
                    partial++;
                }
            }
        }

        return new MatteResult(transparent, partial);
    }

    private static CornerColors SampleCornerColors(
        PixelBuffer pixels,
        int cornerSize)
    {
        return new(
            MedianCorner(0, 0),
            MedianCorner(pixels.Width - cornerSize, 0),
            MedianCorner(0, pixels.Height - cornerSize),
            MedianCorner(
                pixels.Width - cornerSize,
                pixels.Height - cornerSize));

        Rgb MedianCorner(int startX, int startY)
        {
            var reds = new List<byte>(cornerSize * cornerSize);
            var greens = new List<byte>(cornerSize * cornerSize);
            var blues = new List<byte>(cornerSize * cornerSize);
            for (var y = startY; y < startY + cornerSize; y++)
            {
                for (var x = startX; x < startX + cornerSize; x++)
                {
                    var color = pixels.GetRgb(x, y);
                    reds.Add((byte)color.R);
                    greens.Add((byte)color.G);
                    blues.Add((byte)color.B);
                }
            }

            reds.Sort();
            greens.Sort();
            blues.Sort();
            var middle = reds.Count / 2;
            return new(reds[middle], greens[middle], blues[middle]);
        }
    }

    private static double DistanceFromExpectedMatte(
        PixelBuffer pixels,
        int x,
        int y,
        CornerColors corners)
    {
        var horizontal = pixels.Width == 1
            ? 0d
            : (double)x / (pixels.Width - 1);
        var vertical = pixels.Height == 1
            ? 0d
            : (double)y / (pixels.Height - 1);
        var top = Rgb.Lerp(corners.TopLeft, corners.TopRight, horizontal);
        var bottom = Rgb.Lerp(corners.BottomLeft, corners.BottomRight, horizontal);
        var expected = Rgb.Lerp(top, bottom, vertical);
        var actual = pixels.GetRgb(x, y);
        var red = actual.R - expected.R;
        var green = actual.G - expected.G;
        var blue = actual.B - expected.B;
        return Math.Sqrt((red * red + green * green + blue * blue) / 3d);
    }

    private static void SavePngAtomically(
        string destination,
        PixelBuffer pixels,
        bool includeAlpha)
    {
        var directory = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException("Destination has no parent directory.");
        var temporary = Path.Combine(
            directory,
            $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using var bitmap = pixels.CreateBitmap(includeAlpha);
            bitmap.Save(temporary, ImageFormat.Png);
            var header = ReadHeader(temporary, PngMagic.Length);
            if (!header.AsSpan().SequenceEqual(PngMagic))
            {
                throw new InvalidDataException(
                    $"PNG encoder returned the wrong magic for {destination}");
            }
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static void ValidateAllAssets(
        string runtimeAssets,
        string installerAssets,
        string? referenceAssets)
    {
        Console.WriteLine("VALIDATION");
        foreach (var fileName in Directory.EnumerateFiles(runtimeAssets, "*.png")
                     .Select(Path.GetFileName)
                     .Where(fileName => fileName is not null)
                     .OrderBy(fileName => fileName, StringComparer.OrdinalIgnoreCase))
        {
            var runtimePath = Path.Combine(runtimeAssets, fileName!);
            var installerPath = Path.Combine(installerAssets, fileName!);
            EnsureFile(installerPath);
            var header = ReadHeader(runtimePath, PngMagic.Length);
            if (!header.AsSpan().SequenceEqual(PngMagic))
            {
                throw new InvalidDataException(
                    $"Runtime asset is not a true PNG: {fileName}");
            }
            if (!HashesEqual(runtimePath, installerPath))
            {
                throw new InvalidDataException(
                    $"Runtime/Payload mismatch: {fileName}");
            }

            using var bitmap = new Bitmap(runtimePath);
            var pixels = PixelBuffer.Decode(bitmap);
            var statistics = pixels.Statistics();
            var referenceStatus = string.Empty;
            if (!string.IsNullOrWhiteSpace(referenceAssets))
            {
                var referencePath = Path.Combine(referenceAssets, fileName!);
                EnsureFile(referencePath);
                using var referenceBitmap = new Bitmap(referencePath);
                var referencePixels = PixelBuffer.Decode(referenceBitmap);
                pixels.EnsureOpaqueRgbMatches(
                    referencePixels,
                    $"Decoded RGB content changed: {fileName}");
                referenceStatus = "; opaqueRgbReference=exact";
            }
            Console.WriteLine(
                $"{fileName}: {pixels.Width}x{pixels.Height}; " +
                $"alpha0={statistics.Transparent}; alphaPartial={statistics.Partial}; " +
                $"alpha255={statistics.Opaque}; rgbMean=" +
                $"{statistics.MeanRed:F1}/{statistics.MeanGreen:F1}/{statistics.MeanBlue:F1}; " +
                $"sha256={Hash(runtimePath)}{referenceStatus}");
            if (statistics.Transparent > 0 || statistics.Partial > 0)
            {
                WriteDiagnosticPreviews(fileName!, pixels);
            }
        }
    }

    private static void WriteDiagnosticPreviews(
        string fileName,
        PixelBuffer pixels)
    {
        var previewDirectory = Path.Combine(
            Path.GetTempPath(),
            "cnj-td-artwork-previews");
        Directory.CreateDirectory(previewDirectory);
        var stem = Path.GetFileNameWithoutExtension(fileName);

        using (var checkerboard = pixels.CreateCheckerboardPreview())
        {
            checkerboard.Save(
                Path.Combine(previewDirectory, $"{stem}-checker.png"),
                ImageFormat.Png);
        }
        using (var mask = pixels.CreateAlphaMaskBitmap())
        {
            mask.Save(
                Path.Combine(previewDirectory, $"{stem}-alpha.png"),
                ImageFormat.Png);
        }
        Console.WriteLine($"  previews={previewDirectory}");
    }

    private static byte[] ReadHeader(string path, int count)
    {
        using var stream = File.OpenRead(path);
        var buffer = new byte[count];
        var read = stream.Read(buffer, 0, count);
        if (read != count)
        {
            throw new InvalidDataException($"File is too short: {path}");
        }
        return buffer;
    }

    private static bool HashesEqual(string left, string right)
    {
        return string.Equals(Hash(left), Hash(right), StringComparison.Ordinal);
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void EnsureDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException(path);
        }
    }

    private static void EnsureFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Required artwork is missing.", path);
        }
    }

    private enum ConversionMode
    {
        OpaquePng,
        TransparentBorderMatte,
        TransparentRoundedCorners,
    }

    private sealed record AssetRule(
        string FileName,
        ConversionMode Mode,
        int CoreTolerance,
        int SoftTolerance,
        int FeatherRadius,
        int CornerRadius);

    private readonly record struct MatteResult(
        int TransparentPixels,
        int PartialPixels)
    {
        public static MatteResult None => new(0, 0);
    }

    private readonly record struct Rgb(double R, double G, double B)
    {
        public Rgb(byte red, byte green, byte blue)
            : this((double)red, green, blue)
        {
        }

        public static Rgb Lerp(Rgb left, Rgb right, double amount)
        {
            return new(
                left.R + (right.R - left.R) * amount,
                left.G + (right.G - left.G) * amount,
                left.B + (right.B - left.B) * amount);
        }
    }

    private readonly record struct CornerColors(
        Rgb TopLeft,
        Rgb TopRight,
        Rgb BottomLeft,
        Rgb BottomRight);

    private readonly record struct PixelStatistics(
        int Transparent,
        int Partial,
        int Opaque,
        double MeanRed,
        double MeanGreen,
        double MeanBlue);

    private sealed class PixelBuffer
    {
        private readonly byte[] _bgra;

        private PixelBuffer(int width, int height, byte[] bgra)
        {
            Width = width;
            Height = height;
            _bgra = bgra;
        }

        public int Width { get; }

        public int Height { get; }

        public static PixelBuffer Decode(Bitmap source)
        {
            using var normalized = new Bitmap(
                source.Width,
                source.Height,
                PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(normalized))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.CompositingQuality = CompositingQuality.HighSpeed;
                graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                graphics.PixelOffsetMode = PixelOffsetMode.None;
                graphics.SmoothingMode = SmoothingMode.None;
                graphics.DrawImageUnscaled(source, 0, 0);
            }

            var data = normalized.LockBits(
                new Rectangle(0, 0, normalized.Width, normalized.Height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);
            try
            {
                var output = new byte[checked(normalized.Width * normalized.Height * 4)];
                CopyFromBitmap(data, normalized.Width, normalized.Height, output, 4);
                return new PixelBuffer(normalized.Width, normalized.Height, output);
            }
            finally
            {
                normalized.UnlockBits(data);
            }
        }

        public Rgb GetRgb(int x, int y)
        {
            var offset = checked((y * Width + x) * 4);
            return new(_bgra[offset + 2], _bgra[offset + 1], _bgra[offset]);
        }

        public byte GetAlpha(int index)
        {
            return _bgra[checked(index * 4 + 3)];
        }

        public void SetAlpha(int index, byte alpha)
        {
            _bgra[checked(index * 4 + 3)] = alpha;
        }

        public void ForceOpaque()
        {
            for (var index = 0; index < Width * Height; index++)
            {
                SetAlpha(index, 255);
            }
        }

        public Bitmap CreateBitmap(bool includeAlpha)
        {
            var pixelFormat = includeAlpha
                ? PixelFormat.Format32bppArgb
                : PixelFormat.Format24bppRgb;
            var bitmap = new Bitmap(Width, Height, pixelFormat);
            var data = bitmap.LockBits(
                new Rectangle(0, 0, Width, Height),
                ImageLockMode.WriteOnly,
                pixelFormat);
            try
            {
                var bytesPerPixel = includeAlpha ? 4 : 3;
                var row = new byte[checked(Width * bytesPerPixel)];
                for (var y = 0; y < Height; y++)
                {
                    for (var x = 0; x < Width; x++)
                    {
                        var source = (y * Width + x) * 4;
                        var destination = x * bytesPerPixel;
                        row[destination] = _bgra[source];
                        row[destination + 1] = _bgra[source + 1];
                        row[destination + 2] = _bgra[source + 2];
                        if (includeAlpha)
                        {
                            row[destination + 3] = _bgra[source + 3];
                        }
                    }
                    Marshal.Copy(
                        row,
                        0,
                        IntPtr.Add(data.Scan0, y * data.Stride),
                        row.Length);
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            return bitmap;
        }

        public Bitmap CreateCheckerboardPreview()
        {
            var preview = new Bitmap(Width, Height, PixelFormat.Format24bppRgb);
            using var graphics = Graphics.FromImage(preview);
            const int cell = 24;
            using var light = new SolidBrush(Color.FromArgb(224, 224, 224));
            using var dark = new SolidBrush(Color.FromArgb(176, 176, 176));
            graphics.Clear(Color.White);
            for (var y = 0; y < Height; y += cell)
            {
                for (var x = 0; x < Width; x += cell)
                {
                    graphics.FillRectangle(
                        ((x / cell + y / cell) & 1) == 0 ? light : dark,
                        x,
                        y,
                        Math.Min(cell, Width - x),
                        Math.Min(cell, Height - y));
                }
            }

            using var foreground = CreateBitmap(includeAlpha: true);
            graphics.CompositingMode = CompositingMode.SourceOver;
            graphics.DrawImageUnscaled(foreground, 0, 0);
            return preview;
        }

        public Bitmap CreateAlphaMaskBitmap()
        {
            var mask = new byte[_bgra.Length];
            for (var index = 0; index < Width * Height; index++)
            {
                var source = index * 4;
                var alpha = _bgra[source + 3];
                mask[source] = alpha;
                mask[source + 1] = alpha;
                mask[source + 2] = alpha;
                mask[source + 3] = 255;
            }
            return new PixelBuffer(Width, Height, mask).CreateBitmap(includeAlpha: false);
        }

        public PixelStatistics Statistics()
        {
            var transparent = 0;
            var partial = 0;
            var opaque = 0;
            long red = 0;
            long green = 0;
            long blue = 0;
            var count = Width * Height;
            for (var index = 0; index < count; index++)
            {
                var offset = index * 4;
                var alpha = _bgra[offset + 3];
                if (alpha == 0)
                {
                    transparent++;
                }
                else if (alpha == 255)
                {
                    opaque++;
                }
                else
                {
                    partial++;
                }
                red += _bgra[offset + 2];
                green += _bgra[offset + 1];
                blue += _bgra[offset];
            }

            return new(
                transparent,
                partial,
                opaque,
                (double)red / count,
                (double)green / count,
                (double)blue / count);
        }

        public void EnsureOpaqueRgbMatches(PixelBuffer reference, string message)
        {
            if (Width != reference.Width || Height != reference.Height)
            {
                throw new InvalidDataException(
                    $"{message}; dimensions differ: " +
                    $"{Width}x{Height} vs {reference.Width}x{reference.Height}");
            }

            for (var index = 0; index < Width * Height; index++)
            {
                var offset = index * 4;
                if (_bgra[offset + 3] != 255)
                {
                    continue;
                }
                if (_bgra[offset] == reference._bgra[offset]
                    && _bgra[offset + 1] == reference._bgra[offset + 1]
                    && _bgra[offset + 2] == reference._bgra[offset + 2])
                {
                    continue;
                }

                throw new InvalidDataException(
                    $"{message}; first mismatch at " +
                    $"{index % Width},{index / Width}");
            }
        }

        private static void CopyFromBitmap(
            BitmapData source,
            int width,
            int height,
            byte[] destination,
            int bytesPerPixel)
        {
            var rowBytes = checked(width * bytesPerPixel);
            for (var y = 0; y < height; y++)
            {
                Marshal.Copy(
                    IntPtr.Add(source.Scan0, y * source.Stride),
                    destination,
                    y * rowBytes,
                    rowBytes);
            }
        }
    }
}
