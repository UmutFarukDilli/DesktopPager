using System.IO;
using System.Runtime.InteropServices;

namespace DesktopPager
{
    internal static class NativeMethods
    {
        [DllImport("shell32.dll")]
        public static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

        public const uint SHCNE_ASSOCCHANGED = 0x08000000;
        public const uint SHCNE_UPDATEDIR = 0x00001000;
        public const uint SHCNE_UPDATEITEM = 0x00002000;
        public const uint SHCNF_IDLIST = 0x0000;
        public const uint SHCNF_PATHW = 0x0005; 
        
        public const int SPI_SETDESKWALLPAPER = 20;
        public const int SPIF_UPDATEINIFILE = 0x01;
        public const int SPIF_SENDWININICHANGE = 0x02;

        public const int SW_RESTORE = 9;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);
        
        public static void RefreshDesktop(string? desktopPath = null)
        {
            // 1. Refresh the specific directory if path is provided
            // We removed SHCNE_ASSOCCHANGED to prevent system-wide flickering and window popping.
            if (!string.IsNullOrEmpty(desktopPath) && Directory.Exists(desktopPath))
            {
                IntPtr pathPtr = Marshal.StringToHGlobalUni(desktopPath);
                try
                {
                    SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_PATHW, pathPtr, IntPtr.Zero);
                }
                finally
                {
                    Marshal.FreeHGlobal(pathPtr);
                }

                // 3. Explicitly notify updates for each file to force icon redraw
                try
                {
                    foreach (var file in Directory.GetFiles(desktopPath))
                    {
                        if (Path.GetFileName(file).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;

                        try 
                        {
                            // "Touch" the file to force explorer to re-read metadata/icon
                            File.SetLastWriteTime(file, DateTime.Now);
                        }
                        catch { /* Skip files we can't write to */ }

                        IntPtr filePtr = Marshal.StringToHGlobalUni(file);
                        try
                        {
                            SHChangeNotify(SHCNE_UPDATEITEM, SHCNF_PATHW, filePtr, IntPtr.Zero);
                        }
                        finally
                        {
                            Marshal.FreeHGlobal(filePtr);
                        }
                    }
                }
                catch 
                { 
                    // Ignore access errors during refresh
                }

                // 4. Also notify for directories (folders) as they can also have black background issues
                try
                {
                    foreach (var dir in Directory.GetDirectories(desktopPath))
                    {
                        try 
                        {
                             // Touching directories is also safe and forces refresh
                             Directory.SetLastWriteTime(dir, DateTime.Now);
                        }
                        catch { }

                        IntPtr dirPtr = Marshal.StringToHGlobalUni(dir);
                        try
                        {
                            SHChangeNotify(SHCNE_UPDATEITEM, SHCNF_PATHW, dirPtr, IntPtr.Zero);
                        }
                        finally
                        {
                            Marshal.FreeHGlobal(dirPtr);
                        }
                    }
                }
                catch { }
            }
        }
    }
}
