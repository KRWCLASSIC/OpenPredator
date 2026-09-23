# OpenPredator

<div align="center">

High-performance, lightweight, zero-bloat hardware control suite for Acer gaming laptops.

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![NativeAOT](https://img.shields.io/badge/Runtime-NativeAOT-success.svg)]()
[![Platform](https://img.shields.io/badge/Platform-Windows-lightgrey.svg)]()

</div>

---

> [!WARNING]
> **Work in Progress Notice:**
> - **Graphical User Interface (GUI)**: The standalone GUI client is currently under active development. The background service daemon (`openpredator-service`) and CLI utility (`openpredator`) should be fully functional.
> - **Linux Support**: Basic Linux abstractions exist in `Core`, but active development and physical testing for Linux are on the back burner for now.

---

## Overview

OpenPredator is an open-source replacement for Acer's proprietary NitroSense / PredatorSense software stack (`PSSvc.exe` and `NitroSense.exe` / `PredatorSense.exe`).

The project interacts directly with the Embedded Controller (EC) via ACPI WMI (`root\wmi`) and kernel SMBIOS tables. It aims for full feature parity with OEM controls while removing background bloat, telemetry, and Windows Registry dependencies in favor of a single `%AppData%\OpenPredator\config.json` configuration file.

OpenPredator's daemon is also wire-compatible with the OEM named pipe interface (`\\.\pipe\predatorsense_service_namedpipe`), allowing it to serve as a drop-in replacement service or run entirely standalone in direct hardware access mode.

---

## Device Compatibility

> [!NOTE]
> Testing is currently based on physically verified hardware. If you test OpenPredator on another Nitro or Predator model, please share your results in GitHub Issues or submit a Pull Request to help expand this table.

| Laptop Model | Telemetry | Fan Control & CoolBoost | Power Modes | Keyboard Lighting | LCD Overdrive |
| :--- | :---: | :---: | :---: | :---: | :---: |
| Acer Nitro 5 AN515-57-577G | ✅ | ✅ | ✅ (Quiet, Balanced, Perf) | ✅ (Monochrome Red Brightness & Timeout) | ❌ (Unsupported on this display) |

---

## OS Compatibility

| Operating System | Status | Notes |
| :--- | :---: | :--- |
| Windows 11 | ✅ | Tested on Windows 11 (25H2) |
| Windows 10 | ❓ | Expected working (uses identical ACPI WMI drivers) |
| Linux | 🚧 | In active development (ACPI backend and Unix sockets implemented; physical testing in progress) |

---

## Features

OpenPredator targets parity with all standard NitroSense / PredatorSense hardware controls:

- **Thermal & Fans**: Automatic dynamic curves with CoolBoost™ offset/boost, manual custom speeds (0–100% duty cycle per fan), and 100% Max mode.
- **Power Modes**: Quiet, Balanced/Default, Performance, and Turbo (Turbo mode is automatically filtered and scoped to Predator models).
- **Keyboard Backlight**: Brightness adjustment and 30-second idle timeout for monochrome laptops; 4-Zone RGB effects, colors, speed, and brightness for RGB-equipped SKUs.
- **Hardware Probing**: Automatic feature detection (such as 4-Zone RGB vs. Monochrome) via OEM SMBIOS Type 171 tables.
- **Reboot Persistence**: Saves and restores user settings on startup via `%AppData%\OpenPredator\config.json` without touching the Windows Registry.

> [!TIP]
> If any feature does not work as expected on your specific device, please open an issue with your laptop model and telemetry output.

---

## Benchmarks & Resource Footprint

*Measured on physical hardware (**Acer Nitro 5 AN515-57-577G**, Windows 11 24H2):*

### 1. Memory & System Resource Usage

| Component / Layer | OEM NitroSense Stack | OpenPredator | Notes |
| :--- | :--- | :--- | :--- |
| **Background Service Daemon** | `PSSvc.exe`<br>26.29 MB RAM *(13.23 MB Private)* | `openpredator-service.exe`<br>39.61 MB RAM *(24.49 MB Private)* | OpenPredator daemon uses ~13 MB more memory because it embeds its own real-time ACPI hardware probing engine and NVML direct GPU telemetry daemon self-contained, with zero external driver dependencies. |
| **User Interface / App** | `NitroSense.exe` (UWP)<br>140.85 MB RAM *(123.05 MB Private)* | *(GUI client in development)* | OEM UWP application consumes over 140 MB of RAM while running. |
| **CLI Tool** | ❌ None | `openpredator.exe`<br>~15 MB RAM | Lightweight standalone executable that runs and exits immediately. |
| **Total Active Stack Running Load** | **167.14 MB RAM** *(136.28 MB Commit)*<br>31 Threads / 1,006 Handles | **~54.61 MB RAM** *(39.61 MB Daemon + ~15 MB CLI during run)*<br>16 Threads / ~300 Handles | **-67.3% total active memory footprint** during command execution (**-76.3%** at idle with daemon only). |

### 2. Startup & Execution Latency

| Measurement | OEM NitroSense Stack | OpenPredator |
| :--- | :--- | :--- |
| **Cold UI Launch to Render** | ~3,900 ms (3.90 s) | *(GUI client in development)* |
| **CLI Command Lifecycle (`status`)** | ❌ N/A | **286 ms** median *(Total process start, ACPI/NVML probe, render, exit)* |

---

## Project Structure

- `OpenPredator.Core`: Hardware abstraction layer, ACPI WMI caller, kernel SMBIOS reader, and JSON configuration manager.
- `OpenPredator.Client`: IPC client for communicating with the service daemon over named pipes or Unix sockets.
- `OpenPredator.Service`: Standalone background service daemon providing wire-compatible `PSSvc` pipe support.
- `OpenPredator.Cli`: Command-line interface with dynamic hardware capability probing.
- `OpenPredator.TestingSuite`: Interactive terminal interface and diagnostic hardware tool.
- `builder`: Cross-platform C# build runner and Inno Setup installer packaging pipeline.

---

## CLI Reference

```cmd
# Telemetry
openpredator status
openpredator watch

# Fan controls
openpredator fan auto
openpredator fan max
openpredator fan custom 70 85
openpredator coolboost on|off

# Power profiles
openpredator mode quiet|default|perf|turbo

# Backlight controls
openpredator brightness 100
openpredator kb-timeout on|off

# RGB controls (RGB SKUs)
openpredator rgb static FF0000 00FF00 0000FF FFFFFF
openpredator rgb effect wave 5 5

# Configuration
openpredator config show
openpredator config apply
```

---

## Building from Source

### Prerequisites
- [.NET 10.0 SDK](https://dotnet.microsoft.com/)
- Visual Studio 2022 Desktop development with C++ (required for NativeAOT compilation on Windows)
- [Inno Setup 6](https://jrsoftware.org/isdl.php) (optional, for compiling Windows installer package)

### Build Pipeline (`builder`)

OpenPredator features a cross-platform, zero-dependency C# build runner located in `builder/`. You can invoke it via the root wrappers (`build.bat` on Windows, `./build.sh` on Linux) or `dotnet run --project builder`:

```cmd
# 1. Release build for current OS (dist/<rid>/) - excludes test suite
build.bat
# or: build.bat all

# 2. Release build + package Windows Setup Installer (dist/installer/)
build.bat installer

# 3. Release build + package Portable standalone ZIP (dist/portable/)
build.bat portable

# 4. Full Release Packaging (Installer + Portable ZIP + SHA256SUMS manifest)
build.bat pack

# 5. Development debug build with interactive Testing Suite included
build.bat dev

# 6. Clean all build outputs, .temp caches, and bin/obj directories
build.bat clean

# 7. Version management
build.bat version           # Inspect current version
build.bat version 1.0.1     # Bump version across build props & installer

# 8. Explicit OS target prefixes
build.bat windows:pack
build.bat linux:all
```

---

## License

OpenPredator is licensed under the [MIT License](LICENSE).
Acer, Nitro, Predator, PredatorSense, NitroSense, and CoolBoost are trademarks of Acer Inc. OpenPredator is an independent open-source project and is not affiliated with or endorsed by Acer Inc.
