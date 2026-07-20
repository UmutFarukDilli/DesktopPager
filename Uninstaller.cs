using System;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Linq;

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

        static void Main(string[] args)
        {
            try
            {
                Console.WriteLine("Desktop Pager Uninstaller / Restorer");
                Console.WriteLine("====================================");
                Console.WriteLine();

                // 1. Kill DesktopPager
                Console.WriteLine("[1/4] Stopping Desktop Pager...");
                foreach (var process in Process.GetProcessesByName("DesktopPager"))
                {
                    try
                    {
                        process.Kill();
                        process.WaitForExit(5000);
                        Console.WriteLine("      Stopped process: " + process.Id);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"      Failed to stop process: {ex.Message}");
                    }
                }

                // 2. Paths
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string baseDir = Path.Combine(userProfile, "DesktopPager");
                
                // Robust Desktop Detection
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                
                // Log detected path for transparency
                Console.WriteLine($"      Detected Desktop: {desktopPath}");
                if (desktopPath.Contains("OneDrive", StringComparison.OrdinalIgnoreCase))
                    Console.WriteLine("      (OneDrive managed desktop detected)");

                if (!Directory.Exists(baseDir))
                {
                    Console.WriteLine("\n[Note] Desktop Pager data directory not found.");
                }

                // 3. Handle Desktop Junction
                if (IsJunction(desktopPath))
                {
                    Console.WriteLine("[2/4] Removing Desktop junction...");
                    
                    try
                    {
                        // Directory.Delete on a junction removes the link, not the target content.
                        Directory.Delete(desktopPath, false);
                        Console.WriteLine("      Junction link removed.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"      Failed to remove junction: {ex.Message}. Trying rename strategy...");
                        string backupLink = desktopPath + "_OldLink_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                        try
                        {
                            Directory.Move(desktopPath, backupLink);
                            Directory.Delete(backupLink, false);
                            Console.WriteLine("      Junction removed via move strategy.");
                        }
                        catch
                        {
                            throw new IOException("Could not remove Desktop junction. Please close any files/folders open on the Desktop and try again.", ex);
                        }
                    }

                    // Create real directory
                    if (!Directory.Exists(desktopPath))
                    {
                        Directory.CreateDirectory(desktopPath);
                        Console.WriteLine("      Success: Restored Desktop as a normal directory.");
                    }
                }
                else
                {
                    Console.WriteLine("[2/4] Desktop is already a normal directory.");
                }

                // 4. File Movement (SKIPPED per user request)
                Console.WriteLine("[3/4] File Restoration: Skipped (User requested no file movement).");
                Console.WriteLine($"      Note: Your files are still safe in: {baseDir}\\Pages");

                // 5. Cleanup Application Data
                try
                {
                    // Let's remove the app shortcut from desktop.
                    string appShortcut = Path.Combine(desktopPath, "Desktop Pager.lnk");
                    if (File.Exists(appShortcut)) File.Delete(appShortcut);
                }
                catch { }

                // 6. Refresh
                RefreshDesktop(desktopPath);
                
                // 7. Create shortcut to Pages
                Console.WriteLine("[4/4] Creating shortcut to your files...");
                try
                {
                    string pagesPath = Path.Combine(baseDir, "Pages");
                    if (Directory.Exists(pagesPath))
                    {
                        CreateShortcut(pagesPath, "Desktop Pager Pages", desktopPath);
                        Console.WriteLine("      Shortcut 'Desktop Pager Pages' created on your Desktop.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"      Failed to create shortcut: {ex.Message}");
                }

                Console.WriteLine("\n====================================");
                Console.WriteLine("Uninstallation/Restoration Finished.");
                Console.WriteLine("Your Desktop is now normal.");
                Console.WriteLine("NO FILES WERE MOVED.");
                Console.WriteLine($"Files remain in: {baseDir}\\Pages");
                Console.WriteLine("A shortcut to your files has been added to the Desktop.");
                Console.WriteLine("====================================");
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
            }
            catch (Exception ex)
            {
                Console.WriteLine("\n!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!");
                Console.WriteLine("ERROR DURING UNINSTALL:");
                Console.WriteLine(ex.Message);
                Console.WriteLine("!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!");
                Console.WriteLine("\nUninstallation was not fully completed.");
                Console.WriteLine("Press any key to close.");
                Console.ReadKey();
            }
        }

        static bool IsJunction(string path)
        {
            if (!Directory.Exists(path)) return false;
            try
            {
                var attr = File.GetAttributes(path);
                return (attr & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
            }
            catch { return false; }
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
            try
            {
                string shortcutPath = Path.Combine(desktopPath, $"{shortcutName}.lnk");
                
                Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return;

                dynamic shell = Activator.CreateInstance(shellType);
                dynamic shortcut = shell.CreateShortcut(shortcutPath);

                shortcut.TargetPath = targetPath;
                shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath);
                shortcut.Description = "Access your Desktop Pager page files";
                shortcut.Save();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"      [Debug] Shortcut error: {ex.Message}");
            }
        }
    }
}
