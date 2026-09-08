using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace SForce.App.Services;

public sealed class WindhawkIntegrationService
{
    private const string ModId = "sforce-font-injector";
    private const string ModVersion = "2.0.0";
    private const string RegistryPath = @"SOFTWARE\Windhawk\Engine\Mods\" + ModId;
    private const string IncludeList =
        "explorer.exe|ShellExperienceHost.exe|StartMenuExperienceHost.exe|SearchHost.exe|" +
        "SystemSettings.exe|ApplicationFrameHost.exe|Notion.exe|Claude.exe|ChatGPT.exe|" +
        "msedge.exe|chrome.exe|brave.exe|Arc.exe|firefox.exe|Code.exe|Cursor.exe";
    private const string SafetyExclusions =
        "windhawk.exe|SFORCE.exe|lsass.exe|csrss.exe|winlogon.exe|services.exe|smss.exe|" +
        "fontdrvhost.exe|dwm.exe|audiodg.exe|MsMpEng.exe|SecurityHealthService.exe|" +
        "EasyAntiCheat*.exe|BEService.exe";

    private static readonly string WindhawkDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Windhawk");
    private static readonly string WindhawkExe = Path.Combine(WindhawkDirectory, "windhawk.exe");
    private static readonly string SourceDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Windhawk", "ModsSource");
    private static readonly string ModDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Windhawk", "Engine", "Mods", "64");

    public bool IsAvailable => File.Exists(WindhawkExe) && Directory.Exists(ModDirectory);

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.LocalMachine.OpenSubKey(RegistryPath);
            return key?.GetValue("Disabled") is int disabled && disabled == 0 &&
                   key.GetValue("LibraryFileName") is string library &&
                   File.Exists(Path.Combine(ModDirectory, library));
        }
    }

    public ApplyResult Enable(ForceProfile profile)
    {
        if (!IsAvailable)
        {
            return new ApplyResult(false, "Windhawk نصب یا قابل شناسایی نیست؛ Deep Force فعال نشد.");
        }

        try
        {
            Directory.CreateDirectory(SourceDirectory);
            Directory.CreateDirectory(ModDirectory);

            var sourcePath = Path.Combine(SourceDirectory, $"{ModId}.wh.cpp");
            var libraryName = $"{ModId}_{ModVersion}_sforce.dll";
            var libraryPath = Path.Combine(ModDirectory, libraryName);
            ExtractResource("SForce.App.Native.sforce-font-injector.wh.cpp", sourcePath);
            ExtractResource("SForce.App.Native.sforce-font-injector.dll", libraryPath);

            using var key = Registry.LocalMachine.CreateSubKey(RegistryPath, writable: true)
                ?? throw new InvalidOperationException("تنظیمات Windhawk قابل نوشتن نیست.");
            key.SetValue("LibraryFileName", libraryName, RegistryValueKind.String);
            key.SetValue("Disabled", 0, RegistryValueKind.DWord);
            key.SetValue("Include", profile.AggressiveAliases ? "*" : IncludeList, RegistryValueKind.String);
            key.SetValue("Exclude", SafetyExclusions, RegistryValueKind.String);
            key.SetValue("Architecture", "x86-64", RegistryValueKind.String);
            key.SetValue("Version", ModVersion, RegistryValueKind.String);
            key.SetValue("SettingsChangeTime", checked((int)DateTimeOffset.UtcNow.ToUnixTimeSeconds()), RegistryValueKind.DWord);

            using var settings = key.CreateSubKey("Settings", writable: true)
                ?? throw new InvalidOperationException("تنظیمات Deep Force قابل نوشتن نیست.");
            settings.SetValue("latinFamily", profile.LatinFamily, RegistryValueKind.String);
            settings.SetValue("arabicFamily", profile.ArabicFamily, RegistryValueKind.String);
            settings.SetValue("minimumWeight", Math.Clamp(profile.MinimumWeight, 100, 900), RegistryValueKind.DWord);
            settings.SetValue("preserveMonospace", 1, RegistryValueKind.DWord);

            RestartWindhawk();
            return new ApplyResult(true, "Deep Force فعال شد؛ DirectWrite و GDI اپ‌های هدف از SF استفاده می‌کنند.");
        }
        catch (Exception ex)
        {
            return new ApplyResult(false, $"Deep Force فعال نشد: {ex.Message}");
        }
    }

    public ApplyResult Disable()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(RegistryPath, writable: true);
            if (key is null)
            {
                return new ApplyResult(true, "Deep Force از قبل خاموش بود.");
            }

            key.SetValue("Disabled", 1, RegistryValueKind.DWord);
            key.SetValue("SettingsChangeTime", checked((int)DateTimeOffset.UtcNow.ToUnixTimeSeconds()), RegistryValueKind.DWord);
            RestartWindhawk();
            return new ApplyResult(true, "Deep Force خاموش شد.");
        }
        catch (Exception ex)
        {
            return new ApplyResult(false, $"خاموش‌کردن Deep Force ناموفق بود: {ex.Message}");
        }
    }

    private static void ExtractResource(string name, string destination)
    {
        using var source = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"منبع داخلی پیدا نشد: {name}");
        using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.Read);
        source.CopyTo(output);
    }

    private static void RestartWindhawk()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = WindhawkExe,
            Arguments = "-restart -tray-only",
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }
}
