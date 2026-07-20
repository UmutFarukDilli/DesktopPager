using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;

namespace DesktopPager
{
    public class PageManager
    {
        private readonly string _baseDir;
        private readonly string _pagesDir;
        private readonly string _systemDir;
        private readonly string _layoutsDir;
        private readonly string _iconsDir;
        private readonly string _stateFile;
        private readonly string _desktopPath;
        private readonly string _logFile;
        private readonly string _ignoreFile;
        private readonly string _pageNamesFile;
        private HashSet<string> _ignoredNames;
        private Dictionary<int, string> _pageNames;
        private WallpaperManager _wallpaperManager;
        private bool _isFirstRun;

        public string BaseDirectory => _baseDir;
        public bool IsJunctionActive => IsJunction(_desktopPath);
        public bool IsFirstRun => _isFirstRun;

        public PageManager()
        {
            // User requested data to be stored in %USERPROFILE%\DesktopPager
            _baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "DesktopPager");
            _isFirstRun = !Directory.Exists(_baseDir);

            _pagesDir = Path.Combine(_baseDir, "Pages");
            _systemDir = Path.Combine(_baseDir, "System");
            _layoutsDir = Path.Combine(_systemDir, "Layouts");
            _iconsDir = Path.Combine(_baseDir, "Icons");
            
            _stateFile = Path.Combine(_systemDir, "current_page.txt");
            _pageNamesFile = Path.Combine(_systemDir, "page_names.txt");
            
            _desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory); // Use DesktopDirectory for physical path behavior
            _logFile = Path.Combine(_baseDir, "error_log.txt");
            _ignoreFile = Path.Combine(_baseDir, "ignore.txt");

            _pageNames = new Dictionary<int, string>();

            EnsureDirectories();

            _wallpaperManager = new WallpaperManager(_systemDir);
            
            // Log the detected desktop path for debugging
            LogError($"Initialized. Detected Desktop Path: {_desktopPath}");
            
            LoadIgnoredFiles();
            LoadPageNames();
        }


        private void LoadIgnoredFiles()
        {
            _ignoredNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            _ignoredNames.Add("current_page.txt"); 
            _ignoredNames.Add("ignore.txt");
            _ignoredNames.Add("error_log.txt");
            _ignoredNames.Add("desktop.ini");
            _ignoredNames.Add("System");

            if (File.Exists(_ignoreFile))
            {
                try
                {
                    var lines = File.ReadAllLines(_ignoreFile);
                    foreach (var line in lines)
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            _ignoredNames.Add(line.Trim());
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogError($"Failed to read ignore file: {ex.Message}");
                }
            }
        }

        public void LogError(string message)
        {
            try
            {
                File.AppendAllText(_logFile, $"[{DateTime.Now}] ERROR: {message}{Environment.NewLine}");
            }
            catch { }
        }

        private void LoadPageNames()
        {
            try
            {
                if (File.Exists(_pageNamesFile))
                {
                    var lines = File.ReadAllLines(_pageNamesFile);
                    foreach (var line in lines)
                    {
                        var parts = line.Split('=');
                        if (parts.Length == 2 && int.TryParse(parts[0], out int pageNum))
                        {
                            _pageNames[pageNum] = parts[1];
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"Failed to load page names: {ex.Message}");
            }
        }

        private void SavePageNames()
        {
            try
            {
                var lines = _pageNames.Select(kvp => $"{kvp.Key}={kvp.Value}");
                File.WriteAllLines(_pageNamesFile, lines);
            }
            catch (Exception ex)
            {
                LogError($"Failed to save page names: {ex.Message}");
            }
        }

        public string GetPageName(int pageNum)
        {
            if (_pageNames.TryGetValue(pageNum, out string? customName))
            {
                return customName;
            }
            return $"Page {pageNum}";
        }

        public void RenamePage(int pageNum, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName))
            {
                throw new ArgumentException("Page name cannot be empty");
            }

            _pageNames[pageNum] = newName.Trim();
            SavePageNames();

            // Update shortcuts if this is the current page
            if (GetCurrentPage() == pageNum)
            {
                NativeMethods.RefreshDesktop(_desktopPath);
            }
        }

        public void DeletePage(int pageNum)
        {
            var pages = GetAvailablePages();
            if (pages.Count <= 1)
            {
                throw new InvalidOperationException("Cannot delete the last page");
            }

            if (!pages.Contains(pageNum))
            {
                throw new ArgumentException($"Page {pageNum} does not exist");
            }

            int currentPage = GetCurrentPage();
            
            // Prevent deletion of current page
            if (currentPage == pageNum)
            {
                throw new InvalidOperationException("Cannot delete the active page. Please switch to another page first.");
            }

            // Delete the page folder
            string pageDir = Path.Combine(_pagesDir, $"Page{pageNum}");
            try
            {
                if (Directory.Exists(pageDir))
                {
                    Directory.Delete(pageDir, true);
                }

                // Remove custom name if exists
                if (_pageNames.ContainsKey(pageNum))
                {
                    _pageNames.Remove(pageNum);
                    SavePageNames();
                }

                // Delete layout file
                string layoutFile = Path.Combine(_layoutsDir, $"Page{pageNum}.json");
                if (File.Exists(layoutFile))
                {
                    File.Delete(layoutFile);
                }
            }
            catch (Exception ex)
            {
                throw new IOException($"Failed to delete page: {ex.Message}", ex);
            }
        }

        private void EnsureDirectories()
        {
            try
            {
                if (!Directory.Exists(_baseDir)) Directory.CreateDirectory(_baseDir);
                if (!Directory.Exists(_pagesDir)) Directory.CreateDirectory(_pagesDir);
                if (!Directory.Exists(_systemDir)) Directory.CreateDirectory(_systemDir);
                if (!Directory.Exists(_layoutsDir)) Directory.CreateDirectory(_layoutsDir);
                if (!Directory.Exists(_iconsDir)) Directory.CreateDirectory(_iconsDir);

                if (!File.Exists(_ignoreFile))
                {
                    File.WriteAllLines(_ignoreFile, new[] { "desktop.ini", "Recycle Bin.lnk" }); 
                }
            }
            catch (Exception ex)
            {
                LogError($"Failed to create directories: {ex.Message}");
            }
        }

        public int GetCurrentPage()
        {
            try
            {
                if (File.Exists(_stateFile))
                {
                    string content = File.ReadAllText(_stateFile).Trim();
                    if (int.TryParse(content, out int page))
                    {
                        return page;
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"Failed to read state file: {ex.Message}");
            }
            return 1; // Default to Page 1
        }

        private void SetCurrentPage(int page)
        {
            try
            {
                File.WriteAllText(_stateFile, page.ToString());
            }
            catch (Exception ex)
            {
                LogError($"Failed to write state file: {ex.Message}");
            }
        }

        public List<int> GetAvailablePages()
        {
            var pages = new List<int>();
            try
            {
                var dirs = Directory.GetDirectories(_pagesDir);
                foreach (var dir in dirs)
                {
                    var name = Path.GetFileName(dir); 
                    if (name.StartsWith("Page") && int.TryParse(name.Substring(4), out int num))
                    {
                        pages.Add(num);
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"Failed to list pages: {ex.Message}");
            }
            
            if (pages.Count == 0)
            {
                pages.Add(1);
                pages.Add(2);
                CreatePageFolder(1);
                CreatePageFolder(2);
            }

            pages.Sort();
            return pages;
        }

        private void CreatePageFolder(int pageNum)
        {
            try
            {
                string path = Path.Combine(_pagesDir, $"Page{pageNum}");
                if (!Directory.Exists(path)) 
                {
                    Directory.CreateDirectory(path);
                    // Create shortcut only when the page is first created
                    EnsureShortcuts(path, pageNum);
                }
            }
            catch (Exception ex)
            {
                LogError($"Failed to create page {pageNum}: {ex.Message}");
            }
        }

        public void SwitchNext()
        {
            int current = GetCurrentPage();
            var pages = GetAvailablePages();
            if (pages.Count == 0) return;

            int next = pages.FirstOrDefault(p => p > current);
            if (next == 0) next = pages.Min(); 

            SwitchPage(next);
        }

        public void SwitchPrev()
        {
            int current = GetCurrentPage();
            var pages = GetAvailablePages();
            if (pages.Count == 0) return;

            int prev = pages.LastOrDefault(p => p < current);
            if (prev == 0) prev = pages.Max(); 

            SwitchPage(prev);
        }

        public void CreateNewPage()
        {
            var pages = GetAvailablePages();
            int maxPage = pages.Count > 0 ? pages.Max() : 0;
            int newPageNum = maxPage + 1;

            // Create the new page folder
            CreatePageFolder(newPageNum);
            
            // Switch to it
            SwitchPage(newPageNum);
        }

        public void SwitchPage(int toPage)
        {
            int fromPage = GetCurrentPage();
            
            // If we are already on the page AND it's a junction, do nothing. 
            // BUT verify if it is a junction.
            if (fromPage == toPage && IsJunctionActive) return;

            // 0. Apply Wallpaper IMMEDIATELY for seamless transition
            // This prevents "flicker" of standard wallpaper during the file operations
            _wallpaperManager.ApplyWallpaper(toPage);

            LoadIgnoredFiles();

            // 1. Save current page layout BEFORE switching
            // This is critical because once we change the junction, the old page is no longer accessible
            string fromLayoutFile = Path.Combine(_layoutsDir, $"Page{fromPage}.json");
            DesktopIcons.SaveLayout(fromLayoutFile);
            
            // Also save shortcut positions separately for syncing across pages
            SaveShortcutPositions();

            // 2. Ensure we are in Junction Mode (Migration or Update)
            EnsureJunction(fromPage, toPage);

            // 3. Update State
            SetCurrentPage(toPage);

            // 4. Refresh Explorer
            NativeMethods.RefreshDesktop(_desktopPath);

            // 5. Restore Layout (if exists)
            // Reduced sleep because RestoreLayout now polls continuously
            System.Threading.Thread.Sleep(200); 
            
            string layoutFile = Path.Combine(_layoutsDir, $"Page{toPage}.json");
            DesktopIcons.RestoreLayout(layoutFile);
            
            // 6. Apply shortcut positions from shared config
            System.Threading.Thread.Sleep(200);
            ApplyShortcutPositions();
        }
        
        public void SetWallpaperForPage(int page, string path)
        {
            _wallpaperManager.SetWallpaper(page, path, isEngine: false);
            // If current page, apply now
            if (GetCurrentPage() == page) _wallpaperManager.ApplyWallpaper(page);
        }

        public void SetWallpaperEngineForPage(int page, string idOrPath)
        {
            _wallpaperManager.SetWallpaper(page, idOrPath, isEngine: true);
            // If current page, apply now
            if (GetCurrentPage() == page) _wallpaperManager.ApplyWallpaper(page);
        }

        public void RemoveWallpaper(int page)
        {
            _wallpaperManager.RemoveWallpaper(page);
            if (GetCurrentPage() == page) _wallpaperManager.ApplyWallpaper(page); // Will apply default/fallback
        }

        public void CopyWallpaperToAll(int sourcePage)
        {
            var config = _wallpaperManager.GetConfig(sourcePage);
            if (config == null) return;

            var pages = GetAvailablePages();
            foreach (var page in pages)
            {
                if (page == sourcePage) continue;
                _wallpaperManager.SetWallpaper(page, config.Value, config.Type == "Engine");
            }
        }

        private void SaveShortcutPositions()
        {
            try
            {
                string shortcutPosFile = Path.Combine(_systemDir, "shortcut_positions.json");
                DesktopIcons.SaveShortcutPositions(shortcutPosFile, GetShortcutNames());
            }
            catch (Exception ex)
            {
                LogError($"Failed to save shortcut positions: {ex.Message}");
            }
        }
        
        private void ApplyShortcutPositions()
        {
            try
            {
                string shortcutPosFile = Path.Combine(_systemDir, "shortcut_positions.json");
                DesktopIcons.RestoreShortcutPositions(shortcutPosFile);
            }
            catch (Exception ex)
            {
                LogError($"Failed to apply shortcut positions: {ex.Message}");
            }
        }
        
        private List<string> GetShortcutNames()
        {
            return new List<string>
            {
                "Desktop Pager"
            };
        }
        
        public void SaveCurrentLayout()
        {
            int current = GetCurrentPage();
            string layoutFile = Path.Combine(_layoutsDir, $"Page{current}.json");
            DesktopIcons.SaveLayout(layoutFile);
        }

        public bool IsJunction(string path)
        {
            if (!Directory.Exists(path) && !File.Exists(path)) return false;
            // GetAttributes works for directories too.
            var attr = File.GetAttributes(path);
            return (attr & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
        }

        private void EnsureJunction(int fromPage, int toPage)
        {
            string targetDir = Path.Combine(_pagesDir, $"Page{toPage}");
            if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

            if (IsJunction(_desktopPath))
            {
                // Already a junction, just update it
                // To update, we must delete the junction and recreate it
                SafeDeleteJunction(_desktopPath);
                CreateJunction(_desktopPath, targetDir);
            }
            else
            {
                // Not a junction - implies we are in "Normal Directory" mode.
                // We MUST migrate contents to Page{fromPage} first.
                string fromDir = Path.Combine(_pagesDir, $"Page{fromPage}");
                if (!Directory.Exists(fromDir)) Directory.CreateDirectory(fromDir);

                // Move content from Real Desktop to Page{fromPage}
                MigrateContent(_desktopPath, fromDir);

                // Now Desktop should be empty.
                // We must delete the Desktop directory to create a junction there.
                // Strategy: Rename it first to unblock the path, then create Junction, then try delete backup.
                
                string backupPath = _desktopPath + $"_Backup_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
                
                bool moved = false;
                Exception? lastEx = null;

                // Try a few times with a delay to let Explorer release locks
                for (int i = 0; i < 5; i++)
                {
                    try
                    {
                        Directory.Move(_desktopPath, backupPath);
                        moved = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        lastEx = ex;
                        LogError($"Attempt {i+1} to rename Desktop failed: {ex.Message}");
                        System.Threading.Thread.Sleep(500);
                    }
                }

                if (!moved)
                {
                    string errorMsg = $"Failed to prepare Desktop folder for paging logic.\n\n" +
                                     $"Error: {lastEx?.Message}\n\n" +
                                     "This usually happens when a file on your Desktop is open in another program, or Windows Explorer is locking the folder.\n\n" +
                                     "Please:\n" +
                                     "1. Close all open files/folders on your Desktop.\n" +
                                     "2. Try running the application again.\n" +
                                     "3. If it still fails, try restarting your computer.";
                    
                    LogError(errorMsg);
                    throw new IOException(errorMsg);
                }

                // Now _desktopPath is free.
                CreateJunction(_desktopPath, targetDir);
                
                // Cleanup backup
                try
                {
                    Directory.Delete(backupPath, true);
                }
                catch (Exception ex)
                {
                    LogError($"Warning: Could not delete temporary backup '{backupPath}' currently. You may delete it manually. Error: {ex.Message}");
                }
            }
        }

        private void SafeDeleteJunction(string path)
        {
            try
            {
                // Directory.Delete on a junction removes the junction, NOT the target contents.
                Directory.Delete(path, false);
            }
            catch (Exception ex)
            {
                LogError($"Failed to delete junction directly: {ex.Message}. Trying Rename strategy.");
                
                // If direct delete fails (locked), try renaming it aside
                string backupPath = path + $"_OldLink_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
                try 
                {
                    Directory.Move(path, backupPath);
                    // If successful, delete the renamed link
                    try { Directory.Delete(backupPath, false); } catch { /* Ignore cleanup failure */ }
                }
                catch (Exception renameEx)
                {
                    LogError($"Failed to rename locked junction: {renameEx.Message}");
                    throw; // Verify: If we can't move it, we can't replace it.
                }
            }
        }

        private void CreateJunction(string junctionPath, string targetDir)
        {
            // Use CMD /C mklink /J
            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c mklink /J \"{junctionPath}\" \"{targetDir}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            using (var proc = Process.Start(startInfo))
            {
                proc.WaitForExit();
                if (proc.ExitCode != 0)
                {
                    string err = proc.StandardError.ReadToEnd();
                    string outMsg = proc.StandardOutput.ReadToEnd();
                    throw new IOException($"Failed to create junction: {err} {outMsg}");
                }
            }
        }

        private void MigrateContent(string sourceDir, string destDir, HashSet<string> ignoreList = null)
        {
            try
            {
                var dirInfo = new DirectoryInfo(sourceDir);
                
                // Move Files
                foreach (var file in dirInfo.GetFiles())
                {
                    // If ignoreList provided, skip ignored. If null, move everything.
                    if (ignoreList != null && IsIgnored(file.Name)) continue;

                    try
                    {
                        string targetPath = Path.Combine(destDir, file.Name);
                        if (File.Exists(targetPath)) File.Delete(targetPath); // Overwrite in target
                        file.MoveTo(targetPath);
                    }
                    catch (Exception ex)
                    {
                        LogError($"Failed to move file {file.Name}: {ex.Message}");
                    }
                }

                // Move Directories
                foreach (var subdir in dirInfo.GetDirectories())
                {
                    if (ignoreList != null && IsIgnored(subdir.Name)) continue;

                    try
                    {
                        string targetPath = Path.Combine(destDir, subdir.Name);
                        if (Directory.Exists(targetPath))
                        {
                            // Merge
                            MigrateContent(subdir.FullName, targetPath, ignoreList: null); // Recursive merge always moves all subcontents
                            if (!subdir.GetFileSystemInfos().Any()) subdir.Delete();
                        }
                        else
                        {
                            subdir.MoveTo(targetPath);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogError($"Failed to move directory {subdir.Name}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"Fatal error accessing directory {sourceDir}: {ex.Message}");
            }
        }

        private bool IsIgnored(string name)
        {
            if (_ignoredNames == null) return false;
            return _ignoredNames.Contains(name);
        }

        private void EnsureShortcuts(string desktopPath, int pageNum)
        {
            try
            {
                // Create only one shortcut - "Desktop Pager" that opens the tray menu
                string appIconPath = IconGenerator.GenerateApplicationIcon(_iconsDir);
                
                if (!string.IsNullOrEmpty(appIconPath) && File.Exists(appIconPath))
                {
                    CreateShortcut(desktopPath, "Desktop Pager", "tray", appIconPath);
                }
                else
                {
                    // Fallback to system icon
                    CreateShortcut(desktopPath, "Desktop Pager", "tray", SystemIcons.Application.ToString());
                }
                
                // Note: User can manually position the shortcut in bottom-right corner
            }
            catch (Exception ex)
            {
                LogError($"Failed to create shortcut: {ex.Message}");
            }
        }

        private void CreateShortcut(string folder, string name, string arg, string iconLocation)
        {
             string shortcutPath = Path.Combine(folder, $"{name}.lnk");
             
             // Do NOT delete existing shortcut to preserve desktop position!
             // WScript.Shell.CreateShortcut opens existing or creates new.

             try
             {
                 Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                 if (shellType == null) return;
                 
                 dynamic shell = Activator.CreateInstance(shellType);
                 dynamic shortcut = shell.CreateShortcut(shortcutPath);
                 
                 string exePath = Process.GetCurrentProcess().MainModule.FileName;
                 
                 shortcut.TargetPath = exePath;
                 shortcut.Arguments = arg;
                 shortcut.WorkingDirectory = Path.GetDirectoryName(exePath);
                 shortcut.Description = $"Switch to {name}";
                 shortcut.IconLocation = iconLocation;
                 shortcut.Save();
             }
             catch (Exception ex)
             {
                 LogError($"Failed to create/update shortcut '{name}': {ex.Message}");
             }
        }
        public WallpaperConfig? GetWallpaperConfig(int page)
        {
            return _wallpaperManager.GetConfig(page);
        }
    }
}
