# MemFree - Cross-Platform Memory Cleaner
[![License](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](https://opensource.org/licenses/Apache-2.0)
[![Build & Release](https://img.shields.io/github/actions/workflow/status/Harry20120330/MemFree/build.yml?branch=main&label=build)](https://github.com/Harry20120330/MemFree/actions/workflows/build.yml)

English | [简体中文](README.zh.md)

## Overview
MemFree is a lightweight, cross-platform native C# console utility that frees up system memory. It automatically detects the host operating system at runtime and applies native optimization techniques for **Windows**, **Linux**, and **macOS**.

> **⚠️ Note:** Memory cleaning tools should be used with caution. Over-aggressive cleanup can cause I/O spikes and temporary performance degradation. Recommended for testing or debugging environments; frequent use in production is not advised.

## Quick Start

1. Download a build artifact for your platform from GitHub Actions or Releases.
2. Run the executable with elevated privileges if you want full cleanup behavior.

```bash
# Linux / macOS
sudo ./MemFree
```

```powershell
# Windows (run in elevated terminal)
.\MemFree.exe
```

### Platform Features & Optimizations

| Feature / Operation | Windows | Linux | macOS |
| :--- | :---: | :---: | :---: |
| **Process Working Sets Cleanup** | Yes (`EmptyWorkingSet`) | Not Implemented | Not Implemented |
| **Memory Compaction** | N/A | Yes (`compact_memory`) | N/A |
| **Fragmentation Diagnostics** | N/A | Yes (`buddy_info`) | N/A |
| **System Cache Purge** | Yes (Standby / Low-priority / System File Cache) | Yes (`drop_caches` / `sync`) | Yes (Native `purge` command) |
| **Registry Cache Reconcile** | Yes (`NtSetSystemInformation`) | N/A | N/A |
| **Physical Page Combining** | Yes (Memory Compression) | N/A | N/A |
| **Auto-Elevation (UAC)** | Yes | No (requires `sudo`) | No (requires `sudo`) |
| **Fallback Mode (Standard User)** | Yes (Memory stats only) | Yes (Memory stats only) | Yes (Memory stats only) |

---

## Platform Behavior

### Windows
When running on Windows, MemFree utilizes low-level Win32/NT APIs (`ntdll.dll`, `kernel32.dll`, `advapi32.dll`, `psapi.dll`):
- **Standard User Mode:** Displays memory status only; no process-level cleanup is performed.
- **Administrator Mode:** Automatically attempts UAC elevation to enable necessary security privileges (`SeDebugPrivilege`, `SeProfileSingleProcessPrivilege`, `SeIncreaseQuotaPrivilege`). Once elevated, it performs:
  - Purging standby and low-priority standby page lists.
  - Flushing system modified page lists.
  - Flushing the system file cache via `SetSystemFileCacheSize`.
  - Reconciling the registry cache.
  - Combining physical memory pages.

> **Note:** Process working set cleanup (`EmptyWorkingSet`) is a Windows-exclusive feature. Linux/macOS kernels do not provide an equivalent API, so this feature is not implemented on those platforms.

### Linux
When running on Linux, MemFree interacts with system configuration and procfs:
- **Standard User Mode:** Reads system-wide memory metrics from `/proc/meminfo` and shows initial/final memory states.
- **Root Mode (run via `sudo`):** Executes the following in sequence:
  1. Flushes dirty page cache buffers using the `sync` command.
  2. Drops memory pagecaches, dentries, and inodes by writing `3` to `/proc/sys/vm/drop_caches`.
  3. Triggers kernel page compaction by writing `1` to `/proc/sys/vm/compact_memory` (defragments RAM).
  4. Sets `/proc/sys/vm/compaction_proactiveness` to `80` for more aggressive compaction.
- **Fragmentation Diagnostics:** Calculates a memory fragmentation index (0-100) by reading `/proc/buddy_info`, helping users understand memory health.

### macOS
When running on macOS, MemFree leverages native system utilities:
- **Standard User Mode:** Queries memory statistics via `sysctl` (`hw.memsize`) and `vm_stat` to display memory load, total, and available physical memory.
- **Root Mode (run via `sudo`):** Executes the native `purge` command to clear the OS-level system cache.

---

## Console Output
- **True Color Gradients:** Uses ANSI virtual terminal processing to output elegant gradient headings (Ice Blue to Bright Cyan/Blue) and color-coded status prefixes.
- **Muted Console Output:** Keeps the terminal output neat and clean by only writing `Error` messages to the console.

---

## Usage

### Prerequisites
- **Windows:** .NET 10 runtime or self-contained publish
- **Linux:** Kernel 2.6.29+ (supports `/proc/sys/vm/compact_memory`)
- **macOS:** macOS 10.7 (Lion) or later

### Windows
1. Run `MemFree.exe`.
2. If not running as Administrator, UAC will prompt you for elevation.
   - Choose **Yes** to run a complete cleanup (all 6 optimization steps).
   - Choose **No** to fall back to Standard User mode (memory stats only).
3. The program will execute the cleanup and display before-and-after statistics.

### Linux
1. Open a terminal and run the binary.
2. To run with root privileges for full cleanup (including memory compaction):
   ```bash
   sudo ./MemFree
   ```
   *Note: If run without `sudo`, it will report memory statistics but skip system cache purging.*

### macOS
1. Open a terminal and run the binary.
2. To run with root privileges for system cache purging:
   ```bash
   sudo ./MemFree
   ```
   *Note: If run without `sudo`, it will report memory statistics but skip system cache purging.*

---

## Build Configurations
The project currently ships with one publish configuration defined in [MemFree.csproj](MemFree/MemFree.csproj):

| Configuration | Description | Output | Runtime Dependency |
|---|---|---|---|
| **JIT** (self-contained) | Regular JIT compilation, but the publish bundles the .NET runtime. | `publish/JIT/MemFree.exe` | None – fully bundled |

### Compilation Commands

The project supports these runtime identifiers (RIDs): `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`.
Use JIT publish with the RID you need:

```powershell
# Windows x64 self-contained single-file
dotnet publish -c JIT -r win-x64 --self-contained -p:PublishSingleFile=true -o publish/win-x64

# Linux x64 self-contained single-file
dotnet publish -c JIT -r linux-x64 --self-contained -p:PublishSingleFile=true -o publish/linux-x64

# macOS ARM64 (Apple Silicon) self-contained single-file
dotnet publish -c JIT -r osx-arm64 --self-contained -p:PublishSingleFile=true -o publish/osx-arm64
```

### CI Artifacts

GitHub Actions publishes one artifact per RID, using this naming format:

- `MemFree-JIT-win-x64`
- `MemFree-JIT-win-arm64`
- `MemFree-JIT-linux-x64`
- `MemFree-JIT-linux-arm64`
- `MemFree-JIT-osx-x64`
- `MemFree-JIT-osx-arm64`

---

## Configuration & Roadmap
* **Configuration File (Roadmap):** In future versions, behavior can be customized by placing a `memfree.json` configuration file beside the executable.

---

## Contributing
Contributions are welcome! Please ensure:
- Code follows the existing style
- All platforms build successfully (`dotnet publish -c JIT -r <RID> --self-contained`)
- Documentation is updated accordingly

---

## License
This project is licensed under the **Apache License, Version 2.0**.
See the [LICENSE](LICENSE) file for the full license text.
For additional attribution notices, please see the [NOTICE](NOTICE) file.
