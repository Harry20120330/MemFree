using System;

namespace MemCls
{
    public enum LogLevel
    {
        Info,
        Warning,
        Error
    }

    public struct MemoryStatus
    {
        public uint MemoryLoad;       // 0 to 100
        public ulong TotalPhys;       // in bytes
        public ulong AvailPhys;       // in bytes
        public ulong TotalPageFile;   // in bytes
        public ulong AvailPageFile;   // in bytes
    }

    public interface IMemoryCleaner
    {
        /// <summary>
        /// Gets a value indicating whether the current user is running with administrative or root privileges.
        /// </summary>
        bool IsAdmin { get; }

        /// <summary>
        /// Attempts to request elevation / administrative privileges.
        /// Returns true if an elevated process has been successfully spawned and the current process should exit.
        /// </summary>
        bool RunElevationRequest(Action<LogLevel, string, Exception?> logger);

        /// <summary>
        /// Enables platform-specific security privileges required for standard cleaning.
        /// </summary>
        bool EnablePrivileges(Action<LogLevel, string, Exception?> logger);

        /// <summary>
        /// Retrieves the current memory status.
        /// </summary>
        MemoryStatus GetMemoryStatus(Action<LogLevel, string, Exception?> logger);

        /// <summary>
        /// Performs the memory cleaning operation.
        /// </summary>
        /// <param name="onProcessProgress">Callback for process working set optimization progress (current, total).</param>
        /// <param name="onSummary">Callback to return a success summary message to be printed by the frontend.</param>
        /// <param name="logger">Callback for logging messages.</param>
        void CleanMemory(Action<int, int> onProcessProgress, Action<string> onSummary, Action<LogLevel, string, Exception?> logger);
    }
}
