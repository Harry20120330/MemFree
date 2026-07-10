using System;
using System.Runtime.InteropServices;

namespace MemFree
{
    public static class MemoryCleanerFactory
    {
        public static IMemoryCleaner GetCleaner()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return new WindowsMemoryCleaner();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return new LinuxMemoryCleaner();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return new MacMemoryCleaner();
            }
            else
            {
                throw new PlatformNotSupportedException("Unsupported operating system platform.");
            }
        }
    }
}
