using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MemCls
{
    public class MacMemoryCleaner : IMemoryCleaner
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
            logger(LogLevel.Warning, "Automatic root elevation is not supported on macOS. Please rerun the program using 'sudo'.", null);
            return false;
        }

        public bool EnablePrivileges(Action<LogLevel, string, Exception?> logger)
        {
            // No token privileges to adjust on macOS
            return true;
        }

        public MemoryStatus GetMemoryStatus(Action<LogLevel, string, Exception?> logger)
        {
            ulong totalPhys = 0;
            ulong availPhys = 0;

            try
            {
                string memSizeStr = RunCommand("sysctl", "-n hw.memsize").Trim();
                if (ulong.TryParse(memSizeStr, out ulong bytes))
                {
                    totalPhys = bytes;
                }

                string vmStatStr = RunCommand("vm_stat", "");
                if (!string.IsNullOrEmpty(vmStatStr))
                {
                    ulong pageSize = 4096;
                    ulong freePages = 0;
                    ulong speculativePages = 0;
                    ulong inactivePages = 0;

                    string[] lines = vmStatStr.Split('\n');
                    foreach (string line in lines)
                    {
                        if (line.Contains("page size of"))
                        {
                            int idxStart = line.IndexOf("page size of") + 12;
                            int idxEnd = line.IndexOf("bytes");
                            if (idxStart > 11 && idxEnd > idxStart)
                            {
                                string sizeStr = line.Substring(idxStart, idxEnd - idxStart).Trim();
                                ulong.TryParse(sizeStr, out pageSize);
                            }
                        }
                        else if (line.StartsWith("Pages free:"))
                        {
                            string val = line.Replace("Pages free:", "").Replace(".", "").Trim();
                            ulong.TryParse(val, out freePages);
                        }
                        else if (line.StartsWith("Pages speculative:"))
                        {
                            string val = line.Replace("Pages speculative:", "").Replace(".", "").Trim();
                            ulong.TryParse(val, out speculativePages);
                        }
                        else if (line.StartsWith("Pages inactive:"))
                        {
                            string val = line.Replace("Pages inactive:", "").Replace(".", "").Trim();
                            ulong.TryParse(val, out inactivePages);
                        }
                    }

                    availPhys = (freePages + speculativePages + inactivePages) * pageSize;
                }
            }
            catch (Exception ex)
            {
                logger(LogLevel.Error, "Failed to query macOS memory status.", ex);
            }

            // Fallback
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
                TotalPageFile = 0,
                AvailPageFile = 0
            };
        }

        public void CleanMemory(Action<int, int> onProcessProgress, Action<string> onSummary, Action<LogLevel, string, Exception?> logger)
        {
            logger(LogLevel.Info, "Starting macOS-specific memory cleanup operations...", null);

            // 1. Process cleanup progress simulation
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

            // 2. Execute purge command (requires root)
            if (IsAdmin)
            {
                try
                {
                    logger(LogLevel.Info, "Running 'purge' command to clear system cache...", null);
                    using (Process? purgeProc = Process.Start(new ProcessStartInfo
                    {
                        FileName = "purge",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }))
                    {
                        purgeProc?.WaitForExit();
                    }
                    logger(LogLevel.Info, "macOS system cache purged successfully.", null);
                    onSummary("System cache purged using native 'purge' command.");
                }
                catch (Exception ex)
                {
                    logger(LogLevel.Error, "Failed to execute 'purge' command.", ex);
                    onSummary("Failed to purge system cache.");
                }
            }
            else
            {
                logger(LogLevel.Warning, "Standard user mode: skipping system cache purge.", null);
                onSummary("Optimization complete. (Skipped system purge - root privileges needed).");
            }
        }

        private string RunCommand(string fileName, string arguments)
        {
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (Process? process = Process.Start(startInfo))
                {
                    if (process == null) return string.Empty;
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                    return output;
                }
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
