using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace SForce.App.Services;

public sealed class BrowserForceService
{
    public string ExtensionDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "SFORCE",
        "BrowserExtension");

    public string Build(ForceProfile profile)
    {
        Directory.CreateDirectory(ExtensionDirectory);
        var manifest = new
        {
            manifest_version = 3,
            name = "SFORCE — SF Pro Everywhere",
            version = "1.0.0",
            description = "Forces SF Pro and SF Arabic across websites while preserving icon and code fonts.",
            content_scripts = new[]
            {
                new
                {
                    matches = new[] { "<all_urls>" },
                    css = new[] { "force.css" },
                    js = new[] { "shadow-force.js" },
                    run_at = "document_start",
                    all_frames = true
                }
            }
        };

        var css = ForceCssBuilder.Build(profile);
        File.WriteAllText(
            Path.Combine(ExtensionDirectory, "manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(ExtensionDirectory, "force.css"), css);
        File.WriteAllText(Path.Combine(ExtensionDirectory, "shadow-force.js"), BuildShadowScript(css));
        File.WriteAllText(Path.Combine(ExtensionDirectory, "راهنما.txt"), BuildReadme());
        return ExtensionDirectory;
    }

    public bool Exists() => File.Exists(Path.Combine(ExtensionDirectory, "manifest.json"));

    public void OpenDirectory() => Process.Start(new ProcessStartInfo
    {
        FileName = "explorer.exe",
        Arguments = $"\"{ExtensionDirectory}\"",
        UseShellExecute = true
    });

    public string? OpenExtensionsPage()
    {
        var candidates = new[]
        {
            (Name: "Edge", Path: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"), Url: "edge://extensions"),
            (Name: "Chrome", Path: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"), Url: "chrome://extensions"),
            (Name: "Chrome", Path: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe"), Url: "chrome://extensions"),
            (Name: "Brave", Path: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "BraveSoftware", "Brave-Browser", "Application", "brave.exe"), Url: "brave://extensions")
        };

        var browser = candidates.FirstOrDefault(item => File.Exists(item.Path));
        if (string.IsNullOrWhiteSpace(browser.Path))
        {
            return null;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = browser.Path,
            Arguments = browser.Url,
            UseShellExecute = true
        });
        return browser.Name;
    }

    private static string BuildShadowScript(string css)
    {
        var encodedCss = JsonSerializer.Serialize(css);
        return $$"""
            (() => {
              "use strict";
              const css = {{encodedCss}};
              const marker = "data-sforce-shadow-style";

              function inject(root) {
                if (!root || root.querySelector?.(`style[${marker}]`)) return;
                const style = document.createElement("style");
                style.setAttribute(marker, "");
                style.textContent = css;
                root.appendChild(style);
              }

              function scan(node) {
                if (!node) return;
                if (node.shadowRoot) inject(node.shadowRoot);
                node.querySelectorAll?.("*").forEach(element => {
                  if (element.shadowRoot) inject(element.shadowRoot);
                });
              }

              scan(document);
              new MutationObserver(records => {
                for (const record of records) {
                  record.addedNodes.forEach(scan);
                }
              }).observe(document, { childList: true, subtree: true });
            })();
            """;
    }

    private static string BuildReadme() => """
        SFORCE Browser Force Pack
        =========================

        1) Chrome / Edge / Brave / Arc را باز کنید و وارد صفحه Extensions شوید.
        2) Developer mode را روشن کنید.
        3) روی Load unpacked بزنید و همین پوشه را انتخاب کنید.
        4) صفحه‌های باز را Refresh کنید.

        اگر فونت را در SFORCE عوض کردید، پس از «به‌روزرسانی Force Pack» در صفحه Extensions
        دکمه Reload افزونه را بزنید. برای Incognito باید Allow in Incognito را دستی فعال کنید.

        این افزونه اطلاعاتی جمع‌آوری یا ارسال نمی‌کند و هیچ دسترسی شبکه‌ای ندارد.
        """;
}
