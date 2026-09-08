using Microsoft.Win32;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Windows.Markup;
using System.Windows.Media;

namespace SForce.App.Services;

public sealed class FontCatalogService
{
    private const string FontsRegistryPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts";
    private static readonly string WindowsFonts = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
    private static readonly string UserFonts = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Microsoft", "Windows", "Fonts");

    public Task<IReadOnlyList<FontFaceInfo>> DiscoverSfFontsAsync() => Task.Run(DiscoverSfFonts);

    public async Task<IReadOnlyList<FontFaceInfo>> InstallAsync(IEnumerable<string> sourceFiles)
    {
        var installed = new List<FontFaceInfo>();

        foreach (var source in sourceFiles.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var face = Inspect(source) ?? throw new InvalidDataException($"این فایل فونت قابل خواندن نیست:\n{source}");
            if (!IsExpectedSfFont(face.Family))
            {
                throw new InvalidDataException($"«{face.Family}» از خانوادهٔ SF Pro یا SF Arabic نیست.");
            }

            var destination = await CopyIntoWindowsFontsAsync(source);
            var installedFace = face with { FilePath = destination };
            RegisterFont(installedFace);
            NativeMethods.AddFontResourceEx(destination, 0, nint.Zero);
            installed.Add(installedFace);
        }

        NativeMethods.BroadcastFontChange();
        return installed;
    }

    public static bool IsArabicFamily(string family) =>
        family.Contains("Arabic", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<FontFaceInfo> DiscoverSfFonts()
    {
        var faces = new List<FontFaceInfo>();
        DiscoverFromRegistry(Registry.LocalMachine, faces);
        DiscoverFromRegistry(Registry.CurrentUser, faces);
        DiscoverFromDirectory(WindowsFonts, faces);
        DiscoverFromDirectory(UserFonts, faces);

        foreach (var family in Fonts.SystemFontFamilies)
        {
            var name = GetFamilyName(family);
            if (IsExpectedSfFont(name) && faces.All(x => !x.Family.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                faces.Add(new FontFaceInfo(string.Empty, name, "Installed", IsArabicFamily(name), name.Contains("Variable", StringComparison.OrdinalIgnoreCase)));
            }
        }

        return faces
            .DistinctBy(x => $"{x.Family}\0{x.Face}", StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.IsArabic)
            .ThenBy(x => RankFamily(x.Family))
            .ThenBy(x => x.Family, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Face, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void DiscoverFromDirectory(string directory, List<FontFaceInfo> faces)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var pattern in new[] { "*.ttf", "*.otf", "*.ttc" })
        {
            foreach (var path in Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly))
            {
                if (!IsExpectedSfFont(Path.GetFileName(path)))
                {
                    continue;
                }

                var face = Inspect(path);
                if (face is not null && IsExpectedSfFont(face.Family))
                {
                    faces.Add(face);
                }
            }
        }
    }

    private static void DiscoverFromRegistry(RegistryKey hive, List<FontFaceInfo> faces)
    {
        using var key = hive.OpenSubKey(FontsRegistryPath);
        if (key is null)
        {
            return;
        }

        foreach (var valueName in key.GetValueNames())
        {
            var rawPath = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
            if (!IsExpectedSfFont(valueName) && (rawPath is null || !IsExpectedSfFont(rawPath)))
            {
                continue;
            }

            var path = ResolveFontPath(rawPath);
            var face = path is not null ? Inspect(path) : null;
            if (face is not null)
            {
                faces.Add(face);
                continue;
            }

            var family = CleanRegistryFamily(valueName);
            if (IsExpectedSfFont(family))
            {
                faces.Add(new FontFaceInfo(path ?? string.Empty, family, "Installed", IsArabicFamily(family), family.Contains("Variable", StringComparison.OrdinalIgnoreCase)));
            }
        }
    }

    private static FontFaceInfo? Inspect(string path)
    {
        try
        {
            var glyph = new GlyphTypeface(new Uri(Path.GetFullPath(path), UriKind.Absolute));
            var family = PickLocalizedName(glyph.Win32FamilyNames);
            var face = PickLocalizedName(glyph.Win32FaceNames);
            if (string.IsNullOrWhiteSpace(family))
            {
                return null;
            }

            return new FontFaceInfo(
                Path.GetFullPath(path),
                family,
                string.IsNullOrWhiteSpace(face) ? "Regular" : face,
                IsArabicFamily(family),
                Path.GetFileName(path).Contains("Variable", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is ArgumentException or FileFormatException or NotSupportedException or IOException)
        {
            return null;
        }
    }

    private static string PickLocalizedName(IDictionary<CultureInfo, string> names)
    {
        if (names.TryGetValue(CultureInfo.GetCultureInfo("en-US"), out var english))
        {
            return english;
        }

        return names.Values.FirstOrDefault() ?? string.Empty;
    }

    private static string GetFamilyName(FontFamily family)
    {
        if (family.FamilyNames.TryGetValue(XmlLanguage.GetLanguage("en-us"), out var english))
        {
            return english;
        }

        return family.FamilyNames.Values.FirstOrDefault() ?? family.Source;
    }

    private static async Task<string> CopyIntoWindowsFontsAsync(string source)
    {
        var sourceInfo = new FileInfo(source);
        if (!sourceInfo.Exists)
        {
            throw new FileNotFoundException("فایل فونت پیدا نشد.", source);
        }

        var fileName = Path.GetFileName(source);
        var destination = Path.Combine(WindowsFonts, fileName);
        if (File.Exists(destination))
        {
            if (await FilesMatchAsync(source, destination))
            {
                return destination;
            }

            var hash = await ShortHashAsync(source);
            fileName = $"SFORCE-{hash}-{fileName}";
            destination = Path.Combine(WindowsFonts, fileName);
        }

        File.Copy(source, destination, overwrite: false);
        return destination;
    }

    private static void RegisterFont(FontFaceInfo face)
    {
        using var key = Registry.LocalMachine.CreateSubKey(FontsRegistryPath, writable: true)
            ?? throw new InvalidOperationException("رجیستری فونت‌های ویندوز قابل نوشتن نیست.");
        var label = $"{face.Family} {face.Face} (TrueType)";
        key.SetValue(label, Path.GetFileName(face.FilePath), RegistryValueKind.String);
    }

    private static async Task<bool> FilesMatchAsync(string left, string right)
    {
        var a = new FileInfo(left);
        var b = new FileInfo(right);
        if (a.Length != b.Length)
        {
            return false;
        }

        return string.Equals(await ShortHashAsync(left), await ShortHashAsync(right), StringComparison.Ordinal);
    }

    private static async Task<string> ShortHashAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hash.AsSpan(0, 6));
    }

    private static string? ResolveFontPath(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return null;
        }

        var expanded = Environment.ExpandEnvironmentVariables(rawPath);
        var path = Path.IsPathRooted(expanded) ? expanded : Path.Combine(WindowsFonts, expanded);
        return File.Exists(path) ? path : null;
    }

    private static bool IsExpectedSfFont(string value) =>
        value.Contains("SF Pro", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("SF Arabic", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("SFArabic", StringComparison.OrdinalIgnoreCase);

    private static string CleanRegistryFamily(string valueName)
    {
        var marker = valueName.IndexOf(" (", StringComparison.Ordinal);
        return marker > 0 ? valueName[..marker] : valueName;
    }

    private static int RankFamily(string family)
    {
        if (family.Equals("SF Pro Text", StringComparison.OrdinalIgnoreCase)) return 0;
        if (family.Equals("SF Pro Display", StringComparison.OrdinalIgnoreCase)) return 1;
        if (family.Equals("SF Arabic", StringComparison.OrdinalIgnoreCase)) return 0;
        return 10;
    }
}
