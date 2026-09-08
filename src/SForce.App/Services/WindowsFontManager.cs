using Microsoft.Win32;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace SForce.App.Services;

public sealed class WindowsFontManager(BackupStore backupStore)
{
    private const string SubstitutesPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\FontSubstitutes";
    private const string SystemLinkPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\FontLink\SystemLink";

    public SystemStatus GetStatus()
    {
        var state = backupStore.LoadState();
        using var key = Registry.LocalMachine.OpenSubKey(SubstitutesPath);
        var activeLatin = key?.GetValue("Segoe UI") as string;
        var applied = state is not null && activeLatin is not null &&
                      activeLatin.Equals(state.Profile.LatinFamily, StringComparison.OrdinalIgnoreCase);

        return new SystemStatus(
            applied,
            IsAdministrator(),
            activeLatin,
            state?.Profile.ArabicFamily,
            state?.AppliedAt);
    }

    public ApplyResult Apply(ForceProfile profile)
    {
        if (!IsAdministrator())
        {
            return new ApplyResult(false, "SFORCE باید با دسترسی Administrator اجرا شود.");
        }

        var aliases = FontProfiles.BuildAliases(profile);
        var allAliases = FontProfiles.BuildAliases(profile with { AggressiveAliases = true });
        var previousState = backupStore.LoadState();
        var active = backupStore.LoadActiveBackup();
        var backup = active ?? CaptureBaseline(profile, allAliases);

        if (active is null)
        {
            backupStore.SaveBaseline(backup);
        }
        else if (!string.IsNullOrWhiteSpace(profile.ArabicFontFile) &&
                 backup.RegistryValues.All(x =>
                     !x.Hive.Equals("HKLM", StringComparison.Ordinal) ||
                     !x.KeyPath.Equals(SystemLinkPath, StringComparison.OrdinalIgnoreCase) ||
                     !x.ValueName.Equals(profile.LatinFamily, StringComparison.OrdinalIgnoreCase)))
        {
            backup.RegistryValues.Add(CaptureRegistryValue("HKLM", Registry.LocalMachine, SystemLinkPath, profile.LatinFamily));
            backupStore.SaveBaseline(backup);
        }

        try
        {
            RestoreInactiveAliases(backup, aliases, allAliases);
            RestorePreviousFontLink(backup, previousState, profile);
            WriteAliases(Registry.LocalMachine, aliases);
            WriteAliases(Registry.CurrentUser, aliases);
            WriteArabicFontLink(profile);
            ApplyNonClientFonts(profile.LatinFamily, profile.MinimumWeight);
            NativeMethods.BroadcastFontChange();
            backupStore.SetActive(backup.Id, profile);
            return new ApplyResult(true, "فونت‌ها اعمال شدند. اپ‌ها را دوباره باز کن؛ برای نتیجهٔ کامل یک‌بار Sign out کن.", backup.Id);
        }
        catch (Exception ex)
        {
            return new ApplyResult(false, $"اعمال کامل نشد: {ex.Message}\nبکاپ امن نگه داشته شده و Restore در دسترس است.", backup.Id);
        }
    }

    public ApplyResult Restore()
    {
        var backup = backupStore.LoadActiveBackup();
        if (backup is null)
        {
            return new ApplyResult(false, "بکاپ فعالی برای بازگردانی پیدا نشد.");
        }

        var errors = new List<string>();
        foreach (var snapshot in backup.RegistryValues.AsEnumerable().Reverse())
        {
            try
            {
                RestoreRegistryValue(snapshot);
            }
            catch (Exception ex)
            {
                errors.Add($"{snapshot.ValueName}: {ex.Message}");
            }
        }

        try
        {
            RestoreMetrics(backup.NonClientMetrics, backup.IconTitleFont);
        }
        catch (Exception ex)
        {
            errors.Add($"Window metrics: {ex.Message}");
        }

        NativeMethods.BroadcastFontChange();
        if (errors.Count > 0)
        {
            return new ApplyResult(false, "بخشی از Restore ناموفق بود:\n" + string.Join("\n", errors), backup.Id);
        }

        backupStore.ClearActive();
        return new ApplyResult(true, "تنظیمات اصلی ویندوز برگردانده شد. برای تکمیل، Sign out یا Restart کن.", backup.Id);
    }

    private static BackupManifest CaptureBaseline(ForceProfile profile, IReadOnlyList<FontAlias> aliases)
    {
        var snapshots = new List<RegistryValueSnapshot>();
        foreach (var hive in new[] { (Name: "HKLM", Key: Registry.LocalMachine), (Name: "HKCU", Key: Registry.CurrentUser) })
        {
            snapshots.AddRange(aliases.Select(alias => CaptureRegistryValue(hive.Name, hive.Key, SubstitutesPath, alias.Source)));
        }

        if (!string.IsNullOrWhiteSpace(profile.ArabicFontFile))
        {
            snapshots.Add(CaptureRegistryValue("HKLM", Registry.LocalMachine, SystemLinkPath, profile.LatinFamily));
        }

        var (metrics, iconFont) = CaptureMetrics();
        return new BackupManifest
        {
            Id = $"baseline-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..42],
            CreatedAt = DateTimeOffset.Now,
            Profile = profile,
            RegistryValues = snapshots,
            NonClientMetrics = metrics,
            IconTitleFont = iconFont
        };
    }

    private static void WriteAliases(RegistryKey hive, IReadOnlyList<FontAlias> aliases)
    {
        using var key = hive.CreateSubKey(SubstitutesPath, writable: true)
            ?? throw new InvalidOperationException("FontSubstitutes قابل نوشتن نیست.");

        foreach (var alias in aliases)
        {
            key.SetValue(alias.Source, alias.Target, RegistryValueKind.String);
        }
    }

    private static void RestoreInactiveAliases(
        BackupManifest backup,
        IReadOnlyList<FontAlias> activeAliases,
        IReadOnlyList<FontAlias> allAliases)
    {
        var activeNames = activeAliases.Select(x => x.Source).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var inactiveNames = allAliases
            .Select(x => x.Source)
            .Where(source => !activeNames.Contains(source))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var snapshot in backup.RegistryValues.Where(x =>
                     x.KeyPath.Equals(SubstitutesPath, StringComparison.OrdinalIgnoreCase) &&
                     inactiveNames.Contains(x.ValueName)))
        {
            RestoreRegistryValue(snapshot);
        }
    }

    private static void RestorePreviousFontLink(BackupManifest backup, AppState? previousState, ForceProfile profile)
    {
        if (previousState is null || previousState.Profile.LatinFamily.Equals(profile.LatinFamily, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var snapshot = backup.RegistryValues.FirstOrDefault(x =>
            x.Hive.Equals("HKLM", StringComparison.Ordinal) &&
            x.KeyPath.Equals(SystemLinkPath, StringComparison.OrdinalIgnoreCase) &&
            x.ValueName.Equals(previousState.Profile.LatinFamily, StringComparison.OrdinalIgnoreCase));
        if (snapshot is not null)
        {
            RestoreRegistryValue(snapshot);
        }
    }

    private static void WriteArabicFontLink(ForceProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.ArabicFontFile))
        {
            return;
        }

        using var key = Registry.LocalMachine.CreateSubKey(SystemLinkPath, writable: true)
            ?? throw new InvalidOperationException("SystemLink قابل نوشتن نیست.");
        var current = key.GetValue(profile.LatinFamily) as string[] ?? [];
        var link = $"{Path.GetFileName(profile.ArabicFontFile)},{profile.ArabicFamily}";
        var updated = current
            .Append(link)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        key.SetValue(profile.LatinFamily, updated, RegistryValueKind.MultiString);
    }

    private static RegistryValueSnapshot CaptureRegistryValue(string hiveName, RegistryKey hive, string path, string valueName)
    {
        using var key = hive.OpenSubKey(path);
        var existed = key?.GetValueNames().Contains(valueName, StringComparer.OrdinalIgnoreCase) == true;
        if (!existed || key is null)
        {
            return new RegistryValueSnapshot(hiveName, path, valueName, false, (int)RegistryValueKind.None, []);
        }

        var kind = key.GetValueKind(valueName);
        var value = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        return new RegistryValueSnapshot(hiveName, path, valueName, true, (int)kind, EncodeRegistryValue(value, kind));
    }

    private static string[] EncodeRegistryValue(object? value, RegistryValueKind kind) => kind switch
    {
        RegistryValueKind.MultiString => (string[]?)value ?? [],
        RegistryValueKind.Binary or RegistryValueKind.None => [Convert.ToBase64String((byte[]?)value ?? [])],
        RegistryValueKind.DWord => [Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)],
        RegistryValueKind.QWord => [Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)],
        _ => [Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty]
    };

    private static void RestoreRegistryValue(RegistryValueSnapshot snapshot)
    {
        var hive = snapshot.Hive switch
        {
            "HKLM" => Registry.LocalMachine,
            "HKCU" => Registry.CurrentUser,
            _ => throw new InvalidDataException($"Hive ناشناخته: {snapshot.Hive}")
        };

        using var key = hive.CreateSubKey(snapshot.KeyPath, writable: true)
            ?? throw new InvalidOperationException("کلید رجیستری قابل نوشتن نیست.");
        if (!snapshot.Existed)
        {
            key.DeleteValue(snapshot.ValueName, throwOnMissingValue: false);
            return;
        }

        var kind = (RegistryValueKind)snapshot.Kind;
        object value = kind switch
        {
            RegistryValueKind.MultiString => snapshot.Data,
            RegistryValueKind.Binary or RegistryValueKind.None => Convert.FromBase64String(snapshot.Data.FirstOrDefault() ?? string.Empty),
            RegistryValueKind.DWord => int.Parse(snapshot.Data[0], CultureInfo.InvariantCulture),
            RegistryValueKind.QWord => long.Parse(snapshot.Data[0], CultureInfo.InvariantCulture),
            _ => snapshot.Data.FirstOrDefault() ?? string.Empty
        };
        key.SetValue(snapshot.ValueName, value, kind);
    }

    private static (string? Metrics, string? IconFont) CaptureMetrics()
    {
        var metrics = ReadNativeStructure<NonClientMetrics>(0x0029, initializeSize: true);
        var iconFont = ReadNativeStructure<LogFont>(0x001F, initializeSize: false);
        return (metrics, iconFont);
    }

    private static void ApplyNonClientFonts(string family, int minimumWeight)
    {
        var metrics = GetNativeStructure<NonClientMetrics>(0x0029, initializeSize: true);
        SetFace(ref metrics.CaptionFont, family, minimumWeight);
        SetFace(ref metrics.SmallCaptionFont, family, minimumWeight);
        SetFace(ref metrics.MenuFont, family, minimumWeight);
        SetFace(ref metrics.StatusFont, family, minimumWeight);
        SetFace(ref metrics.MessageFont, family, minimumWeight);
        SetNativeStructure(0x002A, metrics);

        var iconFont = GetNativeStructure<LogFont>(0x001F, initializeSize: false);
        SetFace(ref iconFont, family, minimumWeight);
        SetNativeStructure(0x0022, iconFont);
    }

    private static void RestoreMetrics(string? metrics, string? iconFont)
    {
        if (!string.IsNullOrWhiteSpace(metrics))
        {
            SetRawNativeStructure(0x002A, Convert.FromBase64String(metrics));
        }

        if (!string.IsNullOrWhiteSpace(iconFont))
        {
            SetRawNativeStructure(0x0022, Convert.FromBase64String(iconFont));
        }
    }

    private static string? ReadNativeStructure<T>(uint action, bool initializeSize) where T : struct
    {
        try
        {
            var value = GetNativeStructure<T>(action, initializeSize);
            return Convert.ToBase64String(StructureToBytes(value));
        }
        catch
        {
            return null;
        }
    }

    private static T GetNativeStructure<T>(uint action, bool initializeSize) where T : struct
    {
        var size = Marshal.SizeOf<T>();
        var pointer = Marshal.AllocHGlobal(size);
        try
        {
            Span<byte> zeroes = stackalloc byte[Math.Min(size, 512)];
            for (var offset = 0; offset < size; offset += zeroes.Length)
            {
                var count = Math.Min(zeroes.Length, size - offset);
                Marshal.Copy(zeroes[..count].ToArray(), 0, pointer + offset, count);
            }

            if (initializeSize)
            {
                Marshal.WriteInt32(pointer, size);
            }

            if (!NativeMethods.SystemParametersInfo(action, (uint)size, pointer, 0))
            {
                throw new InvalidOperationException($"SystemParametersInfo GET failed ({Marshal.GetLastWin32Error()}).");
            }

            return Marshal.PtrToStructure<T>(pointer);
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    private static void SetNativeStructure<T>(uint action, T value) where T : struct =>
        SetRawNativeStructure(action, StructureToBytes(value));

    private static void SetRawNativeStructure(uint action, byte[] bytes)
    {
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, pointer, bytes.Length);
            const uint updateAndBroadcast = 0x0001 | 0x0002;
            if (!NativeMethods.SystemParametersInfo(action, (uint)bytes.Length, pointer, updateAndBroadcast))
            {
                throw new InvalidOperationException($"SystemParametersInfo SET failed ({Marshal.GetLastWin32Error()}).");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    private static byte[] StructureToBytes<T>(T value) where T : struct
    {
        var size = Marshal.SizeOf<T>();
        var bytes = new byte[size];
        var pointer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(value, pointer, false);
            Marshal.Copy(pointer, bytes, 0, size);
            return bytes;
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    private static void SetFace(ref LogFont font, string family, int minimumWeight)
    {
        font.FaceName = family.Length > 31 ? family[..31] : family;
        font.Weight = Math.Max(font.Weight, Math.Clamp(minimumWeight, 100, 900));
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private struct LogFont
    {
        public int Height;
        public int Width;
        public int Escapement;
        public int Orientation;
        public int Weight;
        public byte Italic;
        public byte Underline;
        public byte StrikeOut;
        public byte CharSet;
        public byte OutPrecision;
        public byte ClipPrecision;
        public byte Quality;
        public byte PitchAndFamily;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string FaceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private struct NonClientMetrics
    {
        public uint Size;
        public int BorderWidth;
        public int ScrollWidth;
        public int ScrollHeight;
        public int CaptionWidth;
        public int CaptionHeight;
        public LogFont CaptionFont;
        public int SmallCaptionWidth;
        public int SmallCaptionHeight;
        public LogFont SmallCaptionFont;
        public int MenuWidth;
        public int MenuHeight;
        public LogFont MenuFont;
        public LogFont StatusFont;
        public LogFont MessageFont;
        public int PaddedBorderWidth;
    }
}
