<div align="center">

# 🍎 SFORCE

### System-Wide Apple San Francisco (SF Pro & SF Arabic) Typography Enforcer for Windows

[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-0078D6?style=flat-square&logo=windows)](https://microsoft.com)
[![.NET](https://img.shields.io/badge/.NET-10.0%20%7C%20C%23-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![WPF](https://img.shields.io/badge/UI-WPF%20XAML-blue?style=flat-square)](https://github.com/dotnet/wpf)
[![Extension](https://img.shields.io/badge/Browser-Manifest%20V3-4285F4?style=flat-square&logo=googlechrome)](https://developer.chrome.com/docs/extensions/)
[![License](https://img.shields.io/badge/License-MIT-blue?style=flat-square)](LICENSE)
[![Author](https://img.shields.io/badge/Author-Pouya%20Sadeghpour-181717?style=flat-square&logo=github)](https://github.com/pouya-wp)

<p align="center">
  <b>SFORCE</b> delivers seamless, unified Apple typography to Windows. It meticulously maps <b>SF Pro</b> for Latin and <b>SF Arabic</b> for Persian/Arabic/Urdu/Pashto across Win32 interfaces, Windows metrics, system fonts, and modern web browsers with snapshot backups and instant rollback.
</p>

</div>

---

## ✨ Features

- 🖥️ **Windows Metrics Override** — Re-styles classic and modern Windows UI via `NONCLIENTMETRICS` and `SystemParametersInfo`.
- 🔗 **Dual Font Linking** — Links SF Pro to SF Arabic to prevent fallback glyph corruption in Persian, Arabic, and Urdu.
- 🧬 **System Font Aliasing** — Remaps legacy `Segoe UI`, `Tahoma`, `Arial`, `Helvetica`, `Roboto`, and `Inter` via registry `FontSubstitutes`.
- 🌐 **Web Force Pack (Manifest V3)** — Standalone extension generator for Chrome, Edge, Brave, and Arc injecting typography across web frames and Shadow DOM while preserving code monospaces.
- 🛡️ **Zero-Risk Snapshot Restore** — Backs up registry state to `C:\ProgramData\SFORCE\Backups` before any modification. Instant rollback via UI or CLI (`SFORCE.exe --restore`).

---

## 🏗️ Architecture

```
SFORCE Solution Structure
├── SForce.Core (Pure domain logic, alias planner, and CSS extension generator)
├── SForce.App (WPF user interface, registry modifier, metrics updater, backup engine)
└── SForce.Tests (Unit & integration test suites decoupled from OS registry)
```

| Project | Target | Description |
|:---|:---|:---|
| `src/SForce.Core` | .NET 10 | Font metrics calculation, alias rules, Manifest V3 generator |
| `src/SForce.App` | .NET 10 (Windows) | WPF XAML UI, Win32 API bridge, registry manager |
| `tests/SForce.Tests` | .NET 10 | Unit tests validating font substitutions and rollback trees |

---

## 🚀 Quick Start

### Prerequisites
- Windows 10/11 (x64)
- .NET 10 SDK

### Building & Publishing

```powershell
# Clone the repository
git clone https://github.com/pouya-wp/sforce.git
cd sforce

# Build release solution
dotnet build SForce.slnx -c Release

# Run automated tests
dotnet test tests/SForce.Tests/SForce.Tests.csproj -c Release

# Publish self-contained executable (No runtime installation required)
dotnet publish src/SForce.App/SForce.App.csproj -c Release -o artifacts/publish/win-x64
```

---

## 💻 CLI Usage

```powershell
# Launch GUI
SFORCE.exe

# Emergency restore of system defaults
SFORCE.exe --restore
```

---

## 📄 License

Distributed under the MIT License.

---

<div align="center">
  <sub>Crafted with precision & passion by <b><a href="https://github.com/pouya-wp">Pouya Sadeghpour</a></b></sub>
</div>
