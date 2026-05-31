using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace MemCls
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
                            if (key == "MemTotal") totalPhys = valBytes;
                            else if (key == "MemAvailable") availPhys = valBytes;
                            else if (key == "SwapTotal") totalSwap = valBytes;
                            else if (key == "SwapFree") availSwap = valBytes;
                        }
                    }

                    // Fallback if MemAvailable is not reported (older kernels)
                    if (availPhys == 0)
                    {
                        ulong free = 0, buffers = 0, cached = 0;
                        foreach (string line in lines)
                        {
                            string[] parts = line.Split(':', StringSplitOptions.TrimEntries);
                            if (parts.Length < 2) continue;
                            string key = parts[0];
                            string valStr = parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
                            if (ulong.TryParse(valStr, out ulong valKb))
                            {
                                ulong valBytes = valKb * 1024;
                                if (key == "MemFree") free = valBytes;
                                else if (key == "Buffers") buffers = valBytes;
                                else if (key == "Cached") cached = valBytes;
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
                AvailPageFile = availSwap
            };
        }

        public void CleanMemory(Action<int, int> onProcessProgress, Action<string> onSummary, Action<LogLevel, string, Exception?> logger)
        {
            logger(LogLevel.Info, "Starting Linux-specific memory cleanup operations...", null);

            // 1. Process cleanup simulation
            try
            {
                Process[] processes = Process.GetProcesses();
                logger(LogLevel.Info, $"Found {processes.Length} processes running.", null);
                for (int i = 0; i < processes.Length; i++)
                {
                    if (i % 10 == 0 || i == processes.Length - 1)
                    {
                        onProcessProgress(i + 1, processes.Length);
                    }
                }
            }
            catch (Exception ex)
            {
                logger(LogLevel.Warning, "Failed to query process list.", ex);
            }

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
                    onSummary("System pagecache, dentries, and inodes dropped successfully.");
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
    }
}
