using System.Windows.Forms;
using System.Diagnostics;
using System.Linq;

namespace DesktopPager
{
    public class Program
    {
        private static PageManager? _pageManager;
        private static MainWindow? _mainWindow;

        [STAThread]
        public static void Main(string[] args)
        {
            const string appName = "DesktopPagerAppMutex";
            bool createdNew;

            using (var mutex = new System.Threading.Mutex(true, appName, out createdNew))
            {
                if (!createdNew)
                {
                    // App is already running - just activate the existing window
                    var currentProcess = Process.GetCurrentProcess();
                    var existingProcess = Process.GetProcessesByName(currentProcess.ProcessName)
                        .FirstOrDefault(p => p.Id != currentProcess.Id && p.MainWindowHandle != IntPtr.Zero);

                    if (existingProcess != null)
                    {
                        IntPtr hWnd = existingProcess.MainWindowHandle;
                        NativeMethods.ShowWindow(hWnd, NativeMethods.SW_RESTORE);
                        NativeMethods.SetForegroundWindow(hWnd);
                    }
                    return;
                }

                try
                {
                    _pageManager = new PageManager();

                    // If no arguments, show main window
                    if (args.Length == 0)
                    {
                        RunWithWindow();
                    }
                    else
                    {
                        // Command-line mode for backward compatibility
                        RunCommandMode(args);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"An unexpected error occurred:\n{ex.Message}",
                        "Desktop Pager Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
        }

        private static void RunWithWindow()
        {
            // Initialize WPF application
            var app = new System.Windows.Application();

            // Show main window
            ShowMainWindow();

            // Run the WPF application
            app.Run();
        }

        public static void ShowMainWindow()
        {
            if (_mainWindow == null || !_mainWindow.IsLoaded)
            {
                _mainWindow = new MainWindow(_pageManager!);
                _mainWindow.Show();
            }
            else
            {
                _mainWindow.Activate();
                _mainWindow.WindowState = System.Windows.WindowState.Normal;
            }
        }

        private static void RunCommandMode(string[] args)
        {
            string command = args[0].ToLowerInvariant();

            switch (command)
            {
                case "next":
                    _pageManager!.SwitchNext();
                    break;
                case "prev":
                    _pageManager!.SwitchPrev();
                    break;
                case "new":
                    _pageManager!.CreateNewPage();
                    break;
                case "refresh":
                    NativeMethods.RefreshDesktop();
                    break;
                case "set-wallpaper":
                    if (args.Length > 1)
                    {
                        // Supports path with spaces if passed as single arg from shell, but if spaces are split by shell we might need to join keys.
                        // Assuming user passes quoted string.
                        // DesktopPager.exe set-wallpaper "C:\path..."
                        // Windows typically passes "C:\path..." as one arg if quoted.
                        string path = args[1];
                        _pageManager!.SetWallpaperForPage(_pageManager.GetCurrentPage(), path);
                    }
                    break;
                case "set-we":
                    if (args.Length > 1)
                    {
                        string id = args[1];
                        _pageManager!.SetWallpaperEngineForPage(_pageManager.GetCurrentPage(), id);
                    }
                    break;
                case "tray":
                case "menu":
                    RunWithWindow();
                    break;
                default:
                    // Try to parse as page number
                    if (int.TryParse(command, out int pageNum))
                    {
                        _pageManager!.SwitchPage(pageNum);
                    }
                    else 
                    {
                        // If user runs just 'DesktopPager.exe' it goes to simple window. 
                        // If they type unknown command, maybe just show window? Or ignore.
                        // Let's assume user wanted window if it wasn't a known command but might be args.
                        // Actually, previous behavior was specific commands.
                    }
                    break;
            }
        }
    }
}
