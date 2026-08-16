using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace DesktopPager.Uninstaller
{
    class Program
    {
        [DllImport("shell32.dll")]
        public static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
        public const uint SHCNE_ASSOCCHANGED = 0x08000000;
        public const uint SHCNE_UPDATEDIR = 0x00001000;
        public const uint SHCNF_IDLIST = 0x0000;
        public const uint SHCNF_PATHW = 0x0005;

        static int Main(string[] args)
        {
            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            bool success = false;

            try
            {
                Console.WriteLine("Desktop Pager Uninstaller / Restorer");
                Console.WriteLine("====================================");
                Console.WriteLine();

                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string baseDir = Path.Combine(userProfile, "DesktopPager");
                string pagesPath = Path.Combine(baseDir, "Pages");

                Console.WriteLine("[1/4] Stopping Desktop Pager...");
                StopDesktopPager();
                Console.WriteLine($"      Detected Desktop: {desktopPath}");
                if (desktopPath.Contains("OneDrive", StringComparison.OrdinalIgnoreCase))
                    Console.WriteLine("      (OneDrive managed desktop detected)");

                bool needsJunctionRemoval = IsReparsePoint(desktopPath);
                if (needsJunctionRemoval)
                {
                    Console.WriteLine("      Stopping Explorer (it locks the Desktop junction)...");
                    StopExplorer();
                    Thread.Sleep(500);
                }

                Console.WriteLine("[2/4] Restoring Desktop folder...");
                RestoreDesktopFolder(desktopPath, pagesPath);

                Console.WriteLine("[3/4] File Restoration: Skipped.");
                Console.WriteLine($"      Files remain in: {pagesPath}");

                try
                {
                    string appShortcut = Path.Combine(desktopPath, "Desktop Pager.lnk");
                    if (File.Exists(appShortcut)) File.Delete(appShortcut);
                }
                catch { }

                RefreshDesktop(desktopPath);

                Console.WriteLine("[4/4] Creating shortcut to your files...");
                try
                {
                    if (Directory.Exists(pagesPath))
                    {
                        CreateShortcut(pagesPath, "Desktop Pager Pages", desktopPath);
                        Console.WriteLine("      Shortcut 'Desktop Pager Pages' created on your Desktop.");
                    }
                    else
                    {
                        Console.WriteLine("      Pages folder not found; skipped shortcut.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"      Failed to create shortcut: {ex.Message}");
                }

                if (!IsNormalDirectory(desktopPath))
                {
                    throw new IOException(
                        "Desktop was not restored as a normal folder. Close any open Desktop windows and try again.");
                }

                success = true;
                Console.WriteLine();
                Console.WriteLine("====================================");
                Console.WriteLine("Uninstallation/Restoration Finished.");
                Console.WriteLine("Desktop is a normal folder again (not a junction).");
                Console.WriteLine("NO FILES WERE MOVED.");
                Console.WriteLine($"Files remain in: {pagesPath}");
                Console.WriteLine("====================================");
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!");
                Console.WriteLine("ERROR DURING UNINSTALL:");
                Console.WriteLine(ex.Message);
                Console.WriteLine("!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!");
                Console.WriteLine();
                Console.WriteLine("Uninstallation was not fully completed.");
            }
            finally
            {
                try { EnsureRealDesktop(desktopPath); }
                catch { }

                Console.WriteLine("      Restarting Explorer...");
                StartExplorer();
            }

            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
            return success ? 0 : 1;
        }

        static void StopDesktopPager()
        {
            bool stoppedAny = false;
            foreach (var process in Process.GetProcessesByName("DesktopPager"))
            {
                try
                {
                    process.Kill();
                    process.WaitForExit(5000);
                    Console.WriteLine("      Stopped process: " + process.Id);
                    stoppedAny = true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"      Failed to stop process: {ex.Message}");
                }
            }

            if (!stoppedAny)
                Console.WriteLine("      Desktop Pager was not running.");

            Thread.Sleep(stoppedAny ? 400 : 100);
        }

        static void StopExplorer()
        {
            foreach (var process in Process.GetProcessesByName("explorer"))
            {
                try
                {
                    process.Kill();
                    process.WaitForExit(5000);
                }
                catch { }
            }
        }

        static void StartExplorer()
        {
            Thread.Sleep(300);
            if (Process.GetProcessesByName("explorer").Length > 0)
                return;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"      Failed to restart Explorer: {ex.Message}");
            }
        }

        static void RestoreDesktopFolder(string desktopPath, string pagesPath)
        {
            if (!Directory.Exists(desktopPath))
            {
                Console.WriteLine("      Desktop folder was missing. Creating a normal directory.");
                Directory.CreateDirectory(desktopPath);
                return;
            }

            if (!IsReparsePoint(desktopPath))
            {
                Console.WriteLine("      Desktop is already a normal directory.");
                return;
            }

            string? target = GetLinkTarget(desktopPath);
            Console.WriteLine($"      Desktop is a reparse point.");
            if (!string.IsNullOrEmpty(target))
                Console.WriteLine($"      Link target: {target}");

            if (!PointsToPages(target, desktopPath, pagesPath))
            {
                throw new IOException(
                    "Desktop is a link, but it does not point to DesktopPager\\Pages.\n" +
                    "Refusing to remove it to avoid deleting an unrelated folder (for example OneDrive).\n" +
                    $"Target: {target ?? "(unknown)"}");
            }

            Console.WriteLine("      Removing DesktopPager junction (page files will stay in Pages)...");
            if (!TryRemoveJunction(desktopPath))
            {
                throw new IOException(
                    "Could not remove the Desktop junction.\n" +
                    "Close any files or Explorer windows open on the Desktop and try again.");
            }

            EnsureRealDesktop(desktopPath);
            Console.WriteLine("      Restored Desktop as a normal directory.");
        }

        static bool TryRemoveJunction(string path)
        {
            for (int i = 0; i < 8; i++)
            {
                if (!Directory.Exists(path) || !IsReparsePoint(path))
                    return true;

                // Explorer can auto-restart and lock Desktop again.
                StopExplorer();

                try
                {
                    // recursive:false removes the junction only, not the target contents.
                    Directory.Delete(path, false);
                    if (!Directory.Exists(path) || !IsReparsePoint(path))
                        return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"      Attempt {i + 1}: Directory.Delete failed ({ex.Message})");
                }

                try
                {
                    // rmdir (no /s) is the classic way to drop a junction without touching the target.
                    var psi = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c rmdir \"{path}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardError = true,
                        RedirectStandardOutput = true
                    };
                    using var proc = Process.Start(psi);
                    proc?.WaitForExit(5000);
                    if (!Directory.Exists(path) || !IsReparsePoint(path))
                        return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"      Attempt {i + 1}: rmdir failed ({ex.Message})");
                }

                Thread.Sleep(400 * (i + 1));
            }

            if (!Directory.Exists(path) || !IsReparsePoint(path))
                return true;

            // Last resort: move the reparse point aside so the Desktop path is free.
            try
            {
                string backupLink = path + "_OldLink_" + Guid.NewGuid().ToString("N")[..8];
                Directory.Move(path, backupLink);
                try { Directory.Delete(backupLink, false); }
                catch { Console.WriteLine($"      Left leftover link at: {backupLink}"); }
                return !Directory.Exists(path) || !IsReparsePoint(path);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"      Move strategy failed: {ex.Message}");
                return false;
            }
        }

        static void EnsureRealDesktop(string desktopPath)
        {
            if (!Directory.Exists(desktopPath))
            {
                Directory.CreateDirectory(desktopPath);
                return;
            }

            if (IsReparsePoint(desktopPath))
            {
                throw new IOException("Desktop path still points to a junction/link.");
            }
        }

        static bool IsNormalDirectory(string path)
        {
            try
            {
                return Directory.Exists(path) && !IsReparsePoint(path);
            }
            catch
            {
                return false;
            }
        }

        static bool IsReparsePoint(string path)
        {
            try
            {
                if (!Directory.Exists(path)) return false;
                var attr = File.GetAttributes(path);
                return (attr & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
            }
            catch
            {
                return false;
            }
        }

        static string? GetLinkTarget(string path)
        {
            try
            {
                return new DirectoryInfo(path).LinkTarget;
            }
            catch
            {
                return null;
            }
        }

        static bool PointsToPages(string? target, string desktopPath, string pagesPath)
        {
            if (string.IsNullOrWhiteSpace(target))
                return false;

            string resolved = Path.IsPathRooted(target)
                ? Path.GetFullPath(target)
                : Path.GetFullPath(Path.Combine(desktopPath, target));

            string pagesFull = Path.GetFullPath(pagesPath);
            if (resolved.Equals(pagesFull, StringComparison.OrdinalIgnoreCase)
                || resolved.StartsWith(pagesFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Pages may already be gone; still accept a path that clearly lives under DesktopPager\Pages.
            string marker = Path.Combine("DesktopPager", "Pages");
            return resolved.Contains(marker + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || resolved.EndsWith(marker, StringComparison.OrdinalIgnoreCase);
        }

        static void RefreshDesktop(string desktopPath)
        {
            try
            {
                SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
                IntPtr pathPtr = Marshal.StringToHGlobalUni(desktopPath);
                try { SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_PATHW, pathPtr, IntPtr.Zero); }
                finally { Marshal.FreeHGlobal(pathPtr); }
            }
            catch { }
        }

        static void CreateShortcut(string targetPath, string shortcutName, string desktopPath)
        {
            string shortcutPath = Path.Combine(desktopPath, $"{shortcutName}.lnk");

            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(shortcutPath);

            shortcut.TargetPath = targetPath;
            shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath);
            shortcut.Description = "Access your Desktop Pager page files";
            shortcut.Save();
        }
    }
}
