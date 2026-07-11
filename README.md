# MemFree – Cross-Platform Memory Cleaner
[![License](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](https://opensource.org/licenses/Apache-2.0)

English | [简体中文](README.zh.md)

## Overview
MemFree is a lightweight, cross-platform native C# console utility that frees up system memory. It automatically detects the host operating system at runtime and applies native optimization techniques for **Windows**, **Linux**, and **macOS**.

> **⚠️ Note:** Memory cleaning tools should be used with caution. Over-aggressive cleanup can cause I/O spikes and temporary performance degradation. Recommended for testing or debugging environments; frequent use in production is not advised.

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

## Detailed Platform Behavior

### 1. Windows
When running on Windows, MemFree utilizes low-level Win32/NT APIs (`ntdll.dll`, `kernel32.dll`, `advapi32.dll`, `psapi.dll`):
- **Standard User Mode:** Displays memory status only; no process-level cleanup is performed.
- **Administrator Mode:** Automatically attempts UAC elevation to enable necessary security privileges (`SeDebugPrivilege`, `SeProfileSingleProcessPrivilege`, `SeIncreaseQuotaPrivilege`). Once elevated, it performs:
  - Purging standby and low-priority standby page lists.
  - Flushing system modified page lists.
  - Flushing the system file cache via `SetSystemFileCacheSize`.
  - Reconciling the registry cache.
  - Combining physical memory pages.

> **Note:** Process working set cleanup (`EmptyWorkingSet`) is a Windows-exclusive feature. Linux/macOS kernels do not provide an equivalent API, so this feature is not implemented on those platforms.

### 2. Linux
When running on Linux, MemFree interacts with system configuration and procfs:
- **Standard User Mode:** Reads system-wide memory metrics from `/proc/meminfo` and shows initial/final memory states.
- **Root Mode (run via `sudo`):** Executes the following in sequence:
  1. Flushes dirty page cache buffers using the `sync` command.
  2. Drops memory pagecaches, dentries, and inodes by writing `3` to `/proc/sys/vm/drop_caches`.
  3. Triggers kernel page compaction by writing `1` to `/proc/sys/vm/compact_memory` (defragments RAM).
  4. Sets `/proc/sys/vm/compaction_proactiveness` to `80` for more aggressive compaction.
- **Fragmentation Diagnostics:** Calculates a memory fragmentation index (0-100) by reading `/proc/buddy_info`, helping users understand memory health.

### 3. macOS
When running on macOS, MemFree leverages native system utilities:
- **Standard User Mode:** Queries memory statistics via `sysctl` (`hw.memsize`) and `vm_stat` to display memory load, total, and available physical memory.
- **Root Mode (run via `sudo`):** Executes the native `purge` command to clear the OS-level system cache.

---

## UI and Console Aesthetics
- **True Color Gradients:** Uses ANSI virtual terminal processing to output elegant gradient headings (Ice Blue to Bright Cyan/Blue) and color-coded status prefixes.
- **Muted Console Output:** Keeps the terminal output neat and clean by only writing `Error` messages to the console.
- **Structured File Logging:** All log entries (`Info`, `Warning`, `Error`) are logged to a daily file inside a `log` directory next to the executable (e.g., `log/memfree_YYYYMMDD.log`).

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
The project ships with two publish configurations defined in [MemFree.csproj](MemFree/MemFree.csproj):

| Configuration | Description | Output | Runtime Dependency |
|---|---|---|---|
| **JIT** (self-contained) | Regular JIT compilation, but the publish bundles the .NET runtime. | `publish/JIT/MemFree.exe` | None – fully bundled |
| **R2R** | Ready-to-Run (pre-compiled) + self-contained runtime – faster start-up. | `publish/R2R/MemFree.exe` | None – fully bundled |

### Compilation Commands

By default, the publish configurations in the csproj target `win-x64`. You can compile for other platforms by specifying the appropriate runtime identifier (RID):

```powershell
# Linux x64 Self-Contained
dotnet publish -c Release -r linux-x64 --self-contained -o publish/linux

# macOS ARM64 (Apple Silicon) Self-Contained
dotnet publish -c Release -r osx-arm64 --self-contained -o publish/osx
```

---

## Configuration & Roadmap
* **Configuration File (Roadmap):** In future versions, behavior can be customized by placing a `memfree.json` configuration file beside the executable.

---

## Contributing
Contributions are welcome! Please ensure:
- Code follows the existing style
- All platforms build successfully (`dotnet build -c Release`)
- Documentation is updated accordingly

---

## License
This project is licensed under the **Apache License, Version 2.0**.
See the [LICENSE](LICENSE) file for the full license text.
For additional attribution notices, please see the [NOTICE](NOTICE) file.
