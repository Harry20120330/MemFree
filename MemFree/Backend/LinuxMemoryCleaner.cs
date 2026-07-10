using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace MemFree
{
    public class LinuxMemoryCleaner : IMemoryCleaner
    {
        [DllImport("libc", EntryPoint = "getuid")]
        private static extern uint getuid();

        public bool IsAdmin
        {
            get
            {
                try
                {
                    return getuid() == 0;
                }
                catch
                {
                    return Environment.UserName == "root";
                }
            }
        }

        public bool RunElevationRequest(Action<LogLevel, string, Exception?> logger)
        {
            logger(LogLevel.Warning, "Automatic root elevation is not supported on Linux. Please rerun the program using 'sudo'.", null);
            return false;
        }

        public bool EnablePrivileges(Action<LogLevel, string, Exception?> logger)
        {
            // No token privileges to enable on Linux (root has absolute permission)
            return true;
        }

        public MemoryStatus GetMemoryStatus(Action<LogLevel, string, Exception?> logger)
        {
            ulong totalPhys = 0;
            ulong availPhys = 0;
            ulong buffers = 0;
            ulong cached = 0;
            ulong slab = 0;
            ulong dirty = 0;
            ulong totalSwap = 0;
            ulong availSwap = 0;

            try
            {
                if (File.Exists("/proc/meminfo"))
                {
                    string[] lines = File.ReadAllLines("/proc/meminfo");
                    foreach (string line in lines)
                    {
                        string[] parts = line.Split(':', StringSplitOptions.TrimEntries);
                        if (parts.Length < 2) continue;

                        string key = parts[0];
                        string valStr = parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
                        if (ulong.TryParse(valStr, out ulong valKb))
                        {
                            ulong valBytes = valKb * 1024;
                            switch (key)
                            {
                                case "MemTotal": totalPhys = valBytes; break;
                                case "MemAvailable": availPhys = valBytes; break;
                                case "Buffers": buffers = valBytes; break;
                                case "Cached": cached = valBytes; break;
                                case "Slab": slab = valBytes; break;
                                case "Dirty": dirty = valBytes; break;
                                case "SwapTotal": totalSwap = valBytes; break;
                                case "SwapFree": availSwap = valBytes; break;
                            }
                        }
                    }

                    // Fallback if MemAvailable is not reported (older kernels)
                    if (availPhys == 0 && totalPhys > 0)
                    {
                        ulong free = 0;
                        foreach (string line in lines)
                        {
                            string[] parts = line.Split(':', StringSplitOptions.TrimEntries);
                            if (parts.Length < 2) continue;
                            string key = parts[0];
                            string valStr = parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
                            if (ulong.TryParse(valStr, out ulong valKb))
                            {
                                if (key == "MemFree") free = valKb * 1024;
                            }
                        }
                        availPhys = free + buffers + cached;
                    }
                }
            }
            catch (Exception ex)
            {
                logger(LogLevel.Error, "Failed to read memory status from /proc/meminfo.", ex);
            }

            // Standard fallback if we couldn't parse system files
            if (totalPhys == 0)
            {
                var gcInfo = GC.GetGCMemoryInfo();
                totalPhys = (ulong)gcInfo.TotalAvailableMemoryBytes;
                availPhys = totalPhys - (ulong)GC.GetTotalMemory(false);
            }

            uint memoryLoad = 0;
            if (totalPhys > 0)
            {
                memoryLoad = (uint)((totalPhys - availPhys) * 100 / totalPhys);
            }

            return new MemoryStatus
            {
                MemoryLoad = memoryLoad,
                TotalPhys = totalPhys,
                AvailPhys = availPhys,
                TotalPageFile = totalSwap,
                AvailPageFile = availSwap,
                Buffers = buffers,
                Cached = cached,
                Slab = slab,
                Dirty = dirty,
                FragmentationIndex = GetFragmentationIndex()
            };
        }

        public void CleanMemory(Action<int, int> onProcessProgress, Action<string> onSummary, Action<LogLevel, string, Exception?> logger)
        {
            logger(LogLevel.Info, "Starting Linux-specific memory cleanup operations...", null);

            // Note: Process working set cleanup is Windows-only (EmptyWorkingSet).
            // Linux/macOS kernels do not provide an equivalent API.
            // Proceed directly to system cache cleanup.

            // 2. Clear System Cache (requires root)
            if (IsAdmin)
            {
                try
                {
                    logger(LogLevel.Info, "Flushing dirty buffers to disk using 'sync'...", null);
                    using (Process? syncProc = Process.Start(new ProcessStartInfo
                    {
                        FileName = "sync",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }))
                    {
                        syncProc?.WaitForExit();
                    }

                    logger(LogLevel.Info, "Writing '3' to /proc/sys/vm/drop_caches...", null);
                    File.WriteAllText("/proc/sys/vm/drop_caches", "3");
                    
                    logger(LogLevel.Info, "Pagecache, dentries, and inodes dropped.", null);

                    // 3. Trigger memory compaction
                    try
                    {
                        logger(LogLevel.Info, "Triggering memory compaction via compact_memory...", null);
                        File.WriteAllText("/proc/sys/vm/compact_memory", "1");
                        logger(LogLevel.Info, "Memory compaction triggered successfully.", null);
                    }
                    catch (Exception ex)
                    {
                        logger(LogLevel.Warning, "Failed to trigger memory compaction (kernel may not support it).", ex);
                    }

                    // 4. Set compaction proactiveness
                    try
                    {
                        logger(LogLevel.Info, "Setting compaction_proactiveness to 80...", null);
                        File.WriteAllText("/proc/sys/vm/compaction_proactiveness", "80");
                        logger(LogLevel.Info, "Compaction proactiveness set successfully.", null);
                    }
                    catch (Exception ex)
                    {
                        logger(LogLevel.Warning, "Failed to set compaction_proactiveness (kernel may not support it).", ex);
                    }

                    onSummary("System pagecache, dentries, inodes dropped and memory compacted successfully.");
                }
                catch (Exception ex)
                {
                    logger(LogLevel.Error, "Failed to drop caches. Verify root permissions.", ex);
                    onSummary("Failed to drop system caches due to permission issues.");
                }
            }
            else
            {
                logger(LogLevel.Warning, "Standard user mode: skipping system cache purge.", null);
                onSummary("Optimization complete. (Skipped system drop_caches - root privileges needed).");
            }
        }

        /// <summary>
        /// Reads /proc/buddy_info to calculate a memory fragmentation index (0-100).
        /// 100 = not fragmented, 0 = heavily fragmented, -1 = unavailable.
        /// </summary>
        private int GetFragmentationIndex()
        {
            try
            {
                if (!File.Exists("/proc/buddy_info"))
                    return -1;

                string[] lines = File.ReadAllLines("/proc/buddy_info");
                ulong totalHighOrderPages = 0;
                ulong totalFreePages = 0;

                foreach (string line in lines)
                {
                    string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 4) continue;

                    if (!int.TryParse(parts[1], out int order)) continue;

                    // Columns after "Order N:" are per-node per-zone page counts
                    for (int i = 2; i < parts.Length; i++)
                    {
                        if (ulong.TryParse(parts[i], out ulong count))
                        {
                            ulong pages = count * (1UL << order);
                            totalFreePages += pages;
                            // High-order pages (>= 32 contiguous pages = order >= 5) indicate less fragmentation
                            if (order >= 5)
                            {
                                totalHighOrderPages += pages;
                            }
                        }
                    }
                }

                if (totalFreePages == 0) return 0;

                // Fragmentation index: percentage of free pages that ARE high-order
                // Higher = less fragmented
                int fragIndex = (int)((double)totalHighOrderPages / (double)totalFreePages * 100);
                return Math.Max(0, Math.Min(100, fragIndex));
            }
            catch
            {
                return -1;
            }
        }
    }
}
