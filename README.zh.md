# MemFree – 跨平台内存清理工具
[![License](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](https://opensource.org/licenses/Apache-2.0)

[English](README.md) | 简体中文

## 项目概述
MemFree 是一个轻量级的跨平台 C# 本机控制台工具，用于释放系统内存。它会在运行时自动检测宿主操作系统，并针对 **Windows**、**Linux** 和 **macOS** 应用各自平台的原生优化技术。

> **⚠️ 注意：** 内存清理工具应在了解风险的前提下使用。过度清理可能导致 I/O 激增和短暂的性能下降。建议在测试或调试环境中使用，不建议在生产环境中频繁运行。

### 平台功能与优化对比

| 功能 / 操作 | Windows | Linux | macOS |
| :--- | :---: | :---: | :---: |
| **进程工作集清理** | 支持 (`EmptyWorkingSet`) | 未实现 | 未实现 |
| **内存压缩** | 不适用 | 支持 (`compact_memory`) | 不适用 |
| **碎片率诊断** | 不适用 | 支持 (`buddy_info`) | 不适用 |
| **系统缓存净化** | 支持 (备用列表/低优先级/系统文件缓存) | 支持 (`drop_caches` / `sync`) | 支持 (原生 `purge` 命令) |
| **注册表缓存整理** | 支持 (`NtSetSystemInformation`) | 不适用 | 不适用 |
| **物理页面合并** | 支持 (内存压缩) | 不适用 | 不适用 |
| **自动权限提升 (UAC)** | 支持 | 不支持 (需手动 `sudo`) | 不支持 (需手动 `sudo`) |
| **降级模式 (普通用户)** | 支持 (仅显示内存状态) | 支持 (仅显示内存状态) | 支持 (仅显示内存状态) |

---

## 平台细节行为

### 1. Windows
在 Windows 上运行时，MemFree 使用底层 Win32/NT API (`ntdll.dll`, `kernel32.dll`, `advapi32.dll`, `psapi.dll`)：
- **普通用户模式：** 仅显示内存状态，不执行进程级清理。
- **管理员模式：** 自动请求 UAC 提权以启用所需的安全特权（`SeDebugPrivilege`、`SeProfileSingleProcessPrivilege`、`SeIncreaseQuotaPrivilege`）。提权成功后会执行：
  - 清理备用列表和低优先级备用页面列表。
  - 刷新系统已修改页面列表。
  - 通过 `SetSystemFileCacheSize` 刷新系统文件缓存。
  - 整理注册表缓存。
  - 合并物理内存页面。

> **注意：** 进程工作集清理（`EmptyWorkingSet`）为 Windows 专属功能。Linux/macOS 内核不提供等价 API，因此在这两个平台上该功能未实现。

### 2. Linux
在 Linux 上运行时，MemFree 通过系统配置和 procfs 进行交互：
- **普通用户模式：** 从 `/proc/meminfo` 读取系统级内存指标并显示清理前后的内存状态。
- **Root 模式 (通过 `sudo` 运行)：** 依次执行以下操作：
  1. 使用 `sync` 命令将脏页缓存缓冲区刷新到磁盘。
  2. 通过写入 `/proc/sys/vm/drop_caches` 为 `3` 来释放内存页缓存 (pagecache)、目录项 (dentries) 和索引节点 (inodes)。
  3. 通过写入 `/proc/sys/vm/compact_memory` 为 `1` 触发内核页面压缩（消除碎片）。
  4. 设置 `/proc/sys/vm/compaction_proactiveness` 为 `80`，使内核更积极地压缩内存。
- **碎片率诊断：** 通过 `/proc/buddy_info` 计算内存碎片指数（0-100），帮助用户了解内存健康状况。

### 3. macOS
在 macOS 上运行时，MemFree 利用原生系统工具：
- **普通用户模式：** 通过 `sysctl` (`hw.memsize`) 和 `vm_stat` 查询内存统计信息，以显示内存负载、物理内存总量及可用量。
- **Root 模式 (通过 `sudo` 运行)：** 执行原生 `purge` 命令清除操作系统级的系统缓存。

---

## UI 和控制台美化
- **真彩色渐变：** 使用 ANSI 虚拟终端处理来输出优雅的渐变标题（冰蓝色到高亮青/蓝色）以及带有颜色标记的状态前缀。
- **清爽的控制台输出：** 终端界面仅打印 `Error` 级别的日志，保持输出整洁。
- **结构化文件日志：** 所有级别的日志（`Info`、`Warning`、`Error`）都会被记录到可执行文件旁 `log` 目录下的每日日志文件中（如 `log/memfree_YYYYMMDD.log`）。

---

## 使用方法

### 前置要求
- **Windows：** .NET 10 运行时或自包含发布
- **Linux：** 内核 2.6.29+（支持 `/proc/sys/vm/compact_memory`）
- **macOS：** macOS 10.7 (Lion) 或更高版本

### Windows
1. 运行 `MemFree.exe`。
2. 若当前不是管理员，程序会弹出 UAC 提示请求提升。
   - 选择 **Yes** 执行完整清理（全部六项优化）。
   - 选择 **No** 降级为普通用户模式运行（仅显示内存状态）。
3. 程序执行清理并显示清理前后的内存状态统计。

### Linux
1. 打开终端并运行编译生成的二进制文件。
2. 若要以 root 权限运行完整清理（包括内存压缩）：
   ```bash
   sudo ./MemFree
   ```
   *注意：如果未使用 `sudo` 运行，程序会报告内存状态，但会跳过系统缓存清理操作。*

### macOS
1. 打开终端并运行编译生成的二进制文件。
2. 若要以 root 权限运行系统缓存清理：
   ```bash
   sudo ./MemFree
   ```
   *注意：如果未使用 `sudo` 运行，程序会报告内存状态，但会跳过系统缓存清理操作。*

---

## 构建配置
项目在 [MemFree.csproj](MemFree/MemFree.csproj) 中定义了两种发布配置：

| 配置 | 描述 | 输出路径 | 运行时依赖 |
|---|---|---|---|
| **JIT** (自包含) | 常规 JIT 编译，但发布时捆绑 .NET 运行时。 | `publish/JIT/MemFree.exe` | 完全捆绑，无外部依赖 |
| **R2R** | Ready-to-Run (预编译) + 自包含运行时，启动更快。 | `publish/R2R/MemFree.exe` | 完全捆绑 |

### 编译命令示例

默认情况下，项目文件中的 publish 配置针对 `win-x64`。您可以通过指定对应的运行时标识符 (RID) 来为其他平台编译：

```powershell
# Linux x64 自包含发布
dotnet publish -c Release -r linux-x64 --self-contained -o publish/linux

# macOS ARM64 (Apple Silicon) 自包含发布
dotnet publish -c Release -r osx-arm64 --self-contained -o publish/osx
```

---

## 配置与路线图
* **配置文件 (未来规划)：** 后续版本将支持通过在可执行文件旁放置 `memfree.json` 配置文件来自定义清理行为。

---

## 贡献
欢迎提交 Issue 和 Pull Request！请确保：
- 代码遵循现有风格
- 所有平台编译通过 (`dotnet build -c Release`)
- 更新相关文档

---

## 许可证
本项目基于 **Apache License, Version 2.0** 进行授权。
完整条款见 [LICENSE](LICENSE)，归属声明见 [NOTICE](NOTICE)。
