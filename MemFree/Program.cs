#pragma warning disable CA1416
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MemFree
{
    internal class Program
    {
        // --- Colors ---
        private static readonly (byte r, byte g, byte b) ColorStart = (0, 242, 254);   // Ice Blue
        private static readonly (byte r, byte g, byte b) ColorEnd = (79, 172, 254);   // Bright Cyan/Blue
        private static readonly (byte r, byte g, byte b) Green = (46, 213, 115);      // Emerald Green
        private static readonly (byte r, byte g, byte b) Red = (255, 71, 87);         // Rose Red
        private static readonly (byte r, byte g, byte b) Orange = (255, 127, 80);      // Coral Orange
        private static readonly (byte r, byte g, byte b) Gray = (160, 160, 160);        // Muted Gray
        private static readonly (byte r, byte g, byte b) LightGray = (180, 189, 210);   // Gray Blue

        // --- Console Virtual Terminal APIs for True Color ---
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

        private const int STD_OUTPUT_HANDLE = -11;
        private const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;

        // --- True Color & Rendering Utilities ---
        private static bool _ansiEnabled = false;

        private static void EnableAnsiTrueColor()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    IntPtr handle = GetStdHandle(STD_OUTPUT_HANDLE);
                    if (handle != IntPtr.Zero && GetConsoleMode(handle, out uint mode))
                    {
                        _ansiEnabled = SetConsoleMode(handle, mode | ENABLE_VIRTUAL_TERMINAL_PROCESSING);
                    }
                }
                catch
                {
                    _ansiEnabled = false;
                }
            }
            else
            {
                // Unix terminals natively support ANSI true colors
                _ansiEnabled = true;
            }
        }

        private static string Rgb(byte r, byte g, byte b, string text)
        {
            if (_ansiEnabled)
            {
                return $"\x1b[38;2;{r};{g};{b}m{text}\x1b[0m";
            }
            return text;
        }

        private static string Gradient(string text, (byte r, byte g, byte b) start, (byte r, byte g, byte b) end)
        {
            if (!_ansiEnabled || string.IsNullOrEmpty(text))
            {
                return text;
            }

            var sb = new System.Text.StringBuilder();
            int len = text.Length;
            for (int i = 0; i < len; i++)
            {
                float ratio = len > 1 ? (float)i / (len - 1) : 0f;
                byte r = (byte)(start.r + (end.r - start.r) * ratio);
                byte g = (byte)(start.g + (end.g - start.g) * ratio);
                byte b = (byte)(start.b + (end.b - start.b) * ratio);
                sb.Append($"\x1b[38;2;{r};{g};{b}m{text[i]}");
            }
            sb.Append("\x1b[0m");
            return sb.ToString();
        }

        public static void Log(LogLevel level, string message, Exception? ex = null)
        {
            // Output to Console only for errors.
            if (level == LogLevel.Error)
            {
                string prefix = Rgb(Red.r, Red.g, Red.b, "[-] ");
                string msg = Rgb(Red.r, Red.g, Red.b, message);
                Console.WriteLine($"{prefix}{msg}");
                if (ex != null)
                {
                    Console.WriteLine(Rgb(Gray.r, Gray.g, Gray.b, $"    Exception: {ex.Message}"));
                }
            }
        }

        static void Main(string[] args)
        {
            // Initialize True Color Output
            EnableAnsiTrueColor();

            // Set OS-specific console title
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Console.Title = "Windows Memory Cleaner (MemFree)";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Console.Title = "Linux Memory Cleaner (MemFree)";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Console.Title = "macOS Memory Cleaner (MemFree)";
            }

            string border = new string('=', 50);
            Console.WriteLine(Gradient(border, ColorStart, ColorEnd));
            Console.WriteLine(Gradient("                Memory Cleaner (MemFree)          ", ColorStart, ColorEnd));
            Console.WriteLine(Gradient(border, ColorStart, ColorEnd));

            IMemoryCleaner cleaner;
            try
            {
                cleaner = MemoryCleanerFactory.GetCleaner();
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, "Failed to initialize memory cleaner for this platform.", ex);
                Console.WriteLine(Rgb(Red.r, Red.g, Red.b, $"[!] Error: {ex.Message}"));
                Console.WriteLine("\nPress any key to exit...");
                Console.ReadKey();
                return;
            }

            bool isAdmin = cleaner.IsAdmin;
            if (!isAdmin)
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Console.WriteLine(Rgb(LightGray.r, LightGray.g, LightGray.b, "[~] Requesting Administrator privileges for standard cleanup (UAC)..."));
                }
                else
                {
                    Console.WriteLine(Rgb(LightGray.r, LightGray.g, LightGray.b, "[~] Administrative/root privileges needed for full optimization..."));
                }

                bool shouldExit = cleaner.RunElevationRequest(Log);
                if (shouldExit)
                {
                    return;
                }

                string warnMsg;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    warnMsg = "[!] UAC elevation declined (User clicked No).\n" +
                              "    Running in standard user mode. Only EmptyWorkingSet will be performed.";
                }
                else
                {
                    warnMsg = "[!] Running in standard user mode. Memory optimization may be limited.\n" +
                              "    Please run the application with administrative/root privileges (e.g. using sudo).";
                }
                Console.WriteLine(Rgb(Orange.r, Orange.g, Orange.b, warnMsg));
                Console.WriteLine();
            }
            else
            {
                Console.WriteLine(Rgb(Green.r, Green.g, Green.b, "[+] Running with Administrator/Root privileges."));
                Console.WriteLine();
                Log(LogLevel.Info, "Started MemFree with Administrator/Root privileges.");
            }

            // Enable privileges
            if (isAdmin)
            {
                cleaner.EnablePrivileges(Log);
            }

            try
            {
                // Show initial memory
                MemoryStatus beforeStatus = cleaner.GetMemoryStatus(Log);
                PrintMemoryStatus("Initial Memory Status", beforeStatus);

                Console.WriteLine(Rgb(LightGray.r, LightGray.g, LightGray.b, "\n[~] Beginning memory optimization..."));
                Log(LogLevel.Info, "Starting memory cleanup operations...");

                // Execute cleanup
                int currentLineCursor = -1;
                cleaner.CleanMemory(
                    onProcessProgress: (current, total) =>
                    {
                        if (currentLineCursor == -1)
                        {
                            Console.Write(Rgb(LightGray.r, LightGray.g, LightGray.b, "  [~] Optimizing process working sets: "));
                            currentLineCursor = Console.CursorLeft;
                        }
                        
                        Console.SetCursorPosition(currentLineCursor, Console.CursorTop);
                        Console.Write(Rgb(ColorStart.r, ColorStart.g, ColorStart.b, $"{current}/{total} processed..."));
                    },
                    onSummary: summaryMsg =>
                    {
                        if (currentLineCursor != -1)
                        {
                            Console.WriteLine();
                            currentLineCursor = -1;
                        }
                        Console.WriteLine(Rgb(Green.r, Green.g, Green.b, $"  [+] {summaryMsg}"));
                    },
                    logger: Log
                );

                // Wait a moment for OS to settle stats
                System.Threading.Thread.Sleep(1000);

                // Show final memory
                MemoryStatus afterStatus = cleaner.GetMemoryStatus(Log);
                Console.WriteLine();
                PrintMemoryStatus("Final Memory Status", afterStatus);

                long freedBytes = (long)afterStatus.AvailPhys - (long)beforeStatus.AvailPhys;
                
                Console.WriteLine(Gradient(border, ColorStart, ColorEnd));
                if (freedBytes > 0)
                {
                    string successMsg = $"[+] Success! Freed: {FormatBytes((ulong)freedBytes)} of physical memory.";
                    Console.WriteLine(Rgb(Green.r, Green.g, Green.b, successMsg));
                    Log(LogLevel.Info, $"Memory cleanup successful. Freed: {FormatBytes((ulong)freedBytes)}.");
                }
                else
                {
                    string neutralMsg = "[*] Optimization complete (Memory was already optimized).";
                    Console.WriteLine(Rgb(LightGray.r, LightGray.g, LightGray.b, neutralMsg));
                    Log(LogLevel.Info, "Memory cleanup complete. No significant memory freed.");
                }
                Console.WriteLine(Gradient(border, ColorStart, ColorEnd));
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, "An error occurred during memory optimization.", ex);
                Console.WriteLine(Rgb(Red.r, Red.g, Red.b, $"[!] Error during execution: {ex.Message}"));
            }

            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }

        private static void PrintMemoryStatus(string title, MemoryStatus status)
        {
            Console.WriteLine(Rgb(255, 255, 255, $"--- {title} ---"));
            
            string labelColor(string label) => Rgb(LightGray.r, LightGray.g, LightGray.b, label);
            string valueColor(string val) => Rgb(ColorStart.r, ColorStart.g, ColorStart.b, val);

            Console.WriteLine($"  {labelColor("Memory Load (Usage):")}  {valueColor("{status.MemoryLoad}%")}");
            Console.WriteLine($"  {labelColor("Total Physical RAM: ")}  {valueColor(FormatBytes(status.TotalPhys))}");
            Console.WriteLine($"  {labelColor("Available Physical: ")}  {valueColor(FormatBytes(status.AvailPhys))}");

            // Linux-specific detailed breakdown
            if (status.Buffers > 0 || status.Cached > 0 || status.Slab > 0 || status.Dirty > 0)
            {
                Console.WriteLine();
                Console.WriteLine($"  {labelColor("  ── Detailed Breakdown:")}");
                if (status.Buffers > 0)
                    Console.WriteLine($"  {labelColor("    Buffers:     ")}  {valueColor(FormatBytes(status.Buffers))}");
                if (status.Cached > 0)
                    Console.WriteLine($"  {labelColor("    Cached:      ")}  {valueColor(FormatBytes(status.Cached))}");
                if (status.Slab > 0)
                    Console.WriteLine($"  {labelColor("    Slab:        ")}  {valueColor(FormatBytes(status.Slab))}");
                if (status.Dirty > 0)
                    Console.WriteLine($"  {labelColor("    Dirty:       ")}  {valueColor(FormatBytes(status.Dirty))}");
                if (status.FragmentationIndex >= 0)
                {
                    string fragLabel = status.FragmentationIndex > 70 ? "Low" :
                                       status.FragmentationIndex > 40 ? "Moderate" : "High";
                    Console.WriteLine($"  {labelColor("    Frag. Index: ")}  {valueColor($"{status.FragmentationIndex}/100 ({fragLabel})")}");
                }
            }

            // Page file / swap stats
            if (status.TotalPageFile > 0)
            {
                Console.WriteLine();
                Console.WriteLine($"  {labelColor("Total Page File/Swap: ")}  {valueColor(FormatBytes(status.TotalPageFile))}");
                Console.WriteLine($"  {labelColor("Available Page File/Swap:")}  {valueColor(FormatBytes(status.AvailPageFile))}");
            }
        }


        private static string FormatBytes(ulong bytes)
        {
            string[] Suffix = { "B", "KB", "MB", "GB", "TB" };
            double dblBytes = bytes;
            int i = 0;
            while (dblBytes >= 1024 && i < Suffix.Length - 1)
            {
                dblBytes /= 1024.0;
                i++;
            }
            return $"{dblBytes:F2} {Suffix[i]}";
        }
    }
}
