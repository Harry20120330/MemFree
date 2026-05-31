#pragma warning disable CA1416
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace MemCls
{
    public class WindowsMemoryCleaner : IMemoryCleaner
    {
        // --- Win32 APIs for Memory Status ---
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [return: MarshalAs(UnmanagedType.Bool)]
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        // --- Win32 APIs for Emptying Working Set ---
        [DllImport("psapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        // --- Win32 APIs for System File Cache ---
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetSystemFileCacheSize(
            UIntPtr MinimumFileCacheSize,
            UIntPtr MaximumFileCacheSize,
            int Flags);

        // --- Win32 APIs for Adjusting Token Privileges ---
        private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
        private const uint TOKEN_QUERY = 0x0008;
        private const uint SE_PRIVILEGE_ENABLED = 0x0002;

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID lpLuid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(
            IntPtr TokenHandle, 
            bool DisableAllPrivileges, 
            ref TOKEN_PRIVILEGES NewState, 
            uint BufferLength, 
            IntPtr PreviousState, 
            IntPtr ReturnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID { public uint LowPart; public int HighPart; }

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID_AND_ATTRIBUTES { public LUID Luid; public uint Attributes; }

        [StructLayout(LayoutKind.Sequential)]
        private struct TOKEN_PRIVILEGES { public uint PrivilegeCount; public LUID_AND_ATTRIBUTES Privileges; }

        // --- Win32 APIs for Native System Info Settings ---
        private enum SYSTEM_MEMORY_LIST_COMMAND
        {
            MemoryCaptureState = 1,
            MemoryFailFreeHeaders = 2,
            MemoryFlushModifiedList = 3,
            MemoryPurgeStandbyList = 4,
            MemoryPurgeLowPriorityStandbyList = 5,
            MemoryCommandMax = 6
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORY_COMBINE_INFORMATION
        {
            public IntPtr Handle;
            public IntPtr PagesCombined;
        }

        [DllImport("ntdll.dll")]
        private static extern int NtSetSystemInformation(
            int SystemInformationClass, 
            IntPtr SystemInformation, 
            int SystemInformationLength);

        // --- IMemoryCleaner Implementation ---

        public bool IsAdmin
        {
            get
            {
                try
                {
                    WindowsIdentity identity = WindowsIdentity.GetCurrent();
                    WindowsPrincipal principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
                catch
                {
                    return false;
                }
            }
        }

        public bool RunElevationRequest(Action<LogLevel, string, Exception?> logger)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "",
                UseShellExecute = true,
                Verb = "runas"
            };
            try
            {
                Process.Start(startInfo);
                logger(LogLevel.Info, "Requested UAC elevation and restarted process.", null);
                return true;
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                logger(LogLevel.Warning, "UAC elevation declined by user. Operating in fallback mode.", ex);
                return false;
            }
        }

        public bool EnablePrivileges(Action<LogLevel, string, Exception?> logger)
        {
            bool p1 = TryEnablePrivilege("SeDebugPrivilege", logger);
            bool p2 = TryEnablePrivilege("SeProfileSingleProcessPrivilege", logger);
            bool p3 = TryEnablePrivilege("SeIncreaseQuotaPrivilege", logger);
            return p1 && p2 && p3;
        }

        public MemoryStatus GetMemoryStatus(Action<LogLevel, string, Exception?> logger)
        {
            MEMORYSTATUSEX memStatus = new MEMORYSTATUSEX();
            memStatus.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            if (!GlobalMemoryStatusEx(ref memStatus))
            {
                int err = Marshal.GetLastWin32Error();
                var ex = new System.ComponentModel.Win32Exception(err);
                logger(LogLevel.Error, "GlobalMemoryStatusEx failed.", ex);
                throw ex;
            }

            return new MemoryStatus
            {
                MemoryLoad = memStatus.dwMemoryLoad,
                TotalPhys = memStatus.ullTotalPhys,
                AvailPhys = memStatus.ullAvailPhys,
                TotalPageFile = memStatus.ullTotalPageFile,
                AvailPageFile = memStatus.ullAvailPageFile
            };
        }

        public void CleanMemory(Action<int, int> onProcessProgress, Action<string> onSummary, Action<LogLevel, string, Exception?> logger)
        {
            logger(LogLevel.Info, "Starting Windows-specific memory cleanup operations...", null);

            // 1. Clean processes working sets
            CleanWorkingSets(onProcessProgress, onSummary, logger);

            // Additional memory optimization features (require Admin)
            if (IsAdmin)
            {
                // 2. Clear standby lists
                PurgeStandbyLists(logger);

                // 3. Flush modified page list
                FlushModifiedPageList(logger);

                // 4. Flush system file cache
                FlushSystemFileCache(logger);

                // 5. Reconcile registry cache
                ReconcileRegistryCache(logger);

                // 6. Combine physical memory
                CombinePhysicalMemoryPages(logger);
            }
        }

        // --- Helper Methods ---

        private bool TryEnablePrivilege(string privilegeName, Action<LogLevel, string, Exception?> logger)
        {
            IntPtr tokenHandle = IntPtr.Zero;
            try
            {
                if (!OpenProcessToken(Process.GetCurrentProcess().Handle, TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out tokenHandle))
                {
                    logger(LogLevel.Warning, $"Failed to open process token for privilege: {privilegeName}", null);
                    return false;
                }

                if (!LookupPrivilegeValue(null, privilegeName, out LUID luid))
                {
                    logger(LogLevel.Warning, $"Failed to lookup privilege value: {privilegeName}", null);
                    return false;
                }

                var tp = new TOKEN_PRIVILEGES
                {
                    PrivilegeCount = 1,
                    Privileges = new LUID_AND_ATTRIBUTES { Luid = luid, Attributes = SE_PRIVILEGE_ENABLED }
                };

                if (!AdjustTokenPrivileges(tokenHandle, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero))
                {
                    logger(LogLevel.Warning, $"Failed to adjust token privileges for: {privilegeName}", null);
                    return false;
                }

                int lastError = Marshal.GetLastWin32Error();
                if (lastError == 0)
                {
                    logger(LogLevel.Info, $"Privilege adjusted successfully: {privilegeName}", null);
                    return true;
                }
                logger(LogLevel.Warning, $"AdjustTokenPrivileges returned last error: {lastError} for privilege: {privilegeName}", null);
                return false;
            }
            catch (Exception ex)
            {
                logger(LogLevel.Error, $"Exception adjusting token privilege: {privilegeName}", ex);
                return false;
            }
            finally
            {
                if (tokenHandle != IntPtr.Zero)
                {
                    CloseHandle(tokenHandle);
                }
            }
        }

        private void CleanWorkingSets(Action<int, int> onProcessProgress, Action<string> onSummary, Action<LogLevel, string, Exception?> logger)
        {
            int successCount = 0;
            int failCount = 0;
            Process[] processes = Process.GetProcesses();

            logger(LogLevel.Info, $"Beginning optimization of working sets for {processes.Length} processes.", null);

            for (int i = 0; i < processes.Length; i++)
            {
                Process proc = processes[i];
                try
                {
                    IntPtr handle = proc.Handle;
                    if (EmptyWorkingSet(handle))
                    {
                        successCount++;
                    }
                    else
                    {
                        failCount++;
                    }
                }
                catch
                {
                    failCount++;
                }
                finally
                {
                    proc.Dispose();
                }

                if (i % 10 == 0 || i == processes.Length - 1)
                {
                    onProcessProgress(i + 1, processes.Length);
                }
            }

            string summaryMsg = $"Working sets optimized: {successCount} processes. (Skipped/Failed: {failCount})";
            logger(LogLevel.Info, summaryMsg, null);
            onSummary(summaryMsg);
        }

        private void PurgeStandbyLists(Action<LogLevel, string, Exception?> logger)
        {
            logger(LogLevel.Info, "Purging system standby lists...", null);
            int SystemMemoryListInformation = 0x50; 

            // Purge Standby List
            int status = CallNtSetSystemInformation(SystemMemoryListInformation, SYSTEM_MEMORY_LIST_COMMAND.MemoryPurgeStandbyList, logger);
            if (status == 0)
            {
                logger(LogLevel.Info, "Standby list purged successfully.", null);
            }
            else
            {
                logger(LogLevel.Error, $"Failed to purge standby list. NTSTATUS: 0x{status:X}", null);
            }

            // Purge Low Priority Standby List
            status = CallNtSetSystemInformation(SystemMemoryListInformation, SYSTEM_MEMORY_LIST_COMMAND.MemoryPurgeLowPriorityStandbyList, logger);
            if (status == 0)
            {
                logger(LogLevel.Info, "Low-priority standby list purged successfully.", null);
            }
            else
            {
                logger(LogLevel.Warning, $"Low-priority standby list response/status: 0x{status:X}", null);
            }
        }

        private void FlushModifiedPageList(Action<LogLevel, string, Exception?> logger)
        {
            logger(LogLevel.Info, "Flushing system modified page list...", null);
            int SystemMemoryListInformation = 0x50; 

            int status = CallNtSetSystemInformation(SystemMemoryListInformation, SYSTEM_MEMORY_LIST_COMMAND.MemoryFlushModifiedList, logger);
            if (status == 0)
            {
                logger(LogLevel.Info, "Modified page list flushed successfully.", null);
            }
            else
            {
                logger(LogLevel.Warning, $"Modified page list response/status: 0x{status:X}", null);
            }
        }

        private void FlushSystemFileCache(Action<LogLevel, string, Exception?> logger)
        {
            logger(LogLevel.Info, "Flushing system file cache...", null);
            try
            {
                UIntPtr purgeVal = (UIntPtr.Size == 8) 
                    ? new UIntPtr(0xFFFFFFFFFFFFFFFF) 
                    : new UIntPtr(0xFFFFFFFF);

                if (SetSystemFileCacheSize(purgeVal, purgeVal, 0))
                {
                    logger(LogLevel.Info, "System file cache flushed successfully.", null);
                }
                else
                {
                    int lastError = Marshal.GetLastWin32Error();
                    logger(LogLevel.Error, $"Failed to flush system file cache. Win32 Error: {lastError}", null);
                }
            }
            catch (Exception ex)
            {
                logger(LogLevel.Error, "Exception flushing system file cache.", ex);
            }
        }

        private void ReconcileRegistryCache(Action<LogLevel, string, Exception?> logger)
        {
            logger(LogLevel.Info, "Reconciling registry cache...", null);
            int SystemRegistryReconciliationInformation = 155; 

            try
            {
                int status = NtSetSystemInformation(
                    SystemRegistryReconciliationInformation, 
                    IntPtr.Zero, 
                    0);

                if (status == 0)
                {
                    logger(LogLevel.Info, "Registry cache reconciled successfully.", null);
                }
                else
                {
                    logger(LogLevel.Warning, $"Registry cache reconciliation response/status: 0x{status:X}", null);
                }
            }
            catch (Exception ex)
            {
                logger(LogLevel.Error, "Exception reconciling registry.", ex);
            }
        }

        private void CombinePhysicalMemoryPages(Action<LogLevel, string, Exception?> logger)
        {
            logger(LogLevel.Info, "Triggering physical memory page combining...", null);
            int SystemCombinePhysicalMemoryInformation = 130; 

            var info = new MEMORY_COMBINE_INFORMATION
            {
                Handle = IntPtr.Zero,
                PagesCombined = IntPtr.Zero
            };

            IntPtr pInfo = IntPtr.Zero;
            try
            {
                pInfo = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(MEMORY_COMBINE_INFORMATION)));
                Marshal.StructureToPtr(info, pInfo, false);

                int status = NtSetSystemInformation(
                    SystemCombinePhysicalMemoryInformation, 
                    pInfo, 
                    Marshal.SizeOf(typeof(MEMORY_COMBINE_INFORMATION)));

                if (status == 0)
                {
                    logger(LogLevel.Info, "Physical memory combining triggered successfully.", null);
                }
                else
                {
                    logger(LogLevel.Warning, $"Physical memory combining response/status: 0x{status:X}", null);
                }
            }
            catch (Exception ex)
            {
                logger(LogLevel.Error, "Exception combining physical memory.", ex);
            }
            finally
            {
                if (pInfo != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(pInfo);
                }
            }
        }

        private int CallNtSetSystemInformation(int infoClass, SYSTEM_MEMORY_LIST_COMMAND command, Action<LogLevel, string, Exception?> logger)
        {
            IntPtr pCommand = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(int)));
            Marshal.WriteInt32(pCommand, (int)command);

            try
            {
                int status = NtSetSystemInformation(
                    infoClass, 
                    pCommand, 
                    Marshal.SizeOf(typeof(int)));
                return status;
            }
            catch (Exception ex)
            {
                logger(LogLevel.Error, $"Exception calling NtSetSystemInformation for command {command}.", ex);
                return -1;
            }
            finally
            {
                Marshal.FreeHGlobal(pCommand);
            }
        }
    }
}
