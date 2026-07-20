using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace DesktopPager
{
    public class WallpaperConfig
    {
        public string Type { get; set; } = "Image"; // "Image" or "Engine"
        public string Value { get; set; } = "";     // File path or Workshop ID
    }

    public class WallpaperManager
    {
        private readonly string _configFile;
        private readonly string _logFile;
        private Dictionary<int, WallpaperConfig> _wallpapers;
        private string? _cachedEnginePath;
        private string? _currentAppliedWallpaper;

        public WallpaperManager(string systemDir)
        {
            _configFile = Path.Combine(systemDir, "wallpapers.json");
            _logFile = Path.Combine(systemDir, "wallpaper_log.txt");
            _wallpapers = new Dictionary<int, WallpaperConfig>();
            LoadConfig();
        }

        private void Log(string message)
        {
            return;
            /* try
            {
                File.AppendAllText(_logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}{Environment.NewLine}");
            }
            catch { } */
        }

        public void RemoveWallpaper(int page)
        {
            if (_wallpapers.ContainsKey(page))
            {
                _wallpapers.Remove(page);
                SaveConfig();
            }
        }

        private void LoadConfig()
        {
            try
            {
                if (File.Exists(_configFile))
                {
                    string json = File.ReadAllText(_configFile);
                    _wallpapers = JsonSerializer.Deserialize<Dictionary<int, WallpaperConfig>>(json) 
                                  ?? new Dictionary<int, WallpaperConfig>();
                }
            }
            catch (Exception ex) 
            { 
                Log($"Error loading config: {ex.Message}");
            }
        }

        private void SaveConfig()
        {
            try
            {
                string json = JsonSerializer.Serialize(_wallpapers, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_configFile, json);
            }
            catch (Exception ex)
            {
                Log($"Error saving config: {ex.Message}");
            }
        }

        public void SetWallpaper(int page, string value, bool isEngine)
        {
            _wallpapers[page] = new WallpaperConfig
            {
                Type = isEngine ? "Engine" : "Image",
                Value = value
            };
            SaveConfig();
        }

        public void ApplyWallpaper(int page, bool force = false)
        {
            if (!_wallpapers.ContainsKey(page)) return;

            var config = _wallpapers[page];
            
            // Redundancy check - if we are already showing this, do nothing.
            // Note: We check config.Value directly. 
            if (!force && _currentAppliedWallpaper == config.Value)
            {
                Log($"Skipping redundant wallpaper application: {config.Value}");
                return;
            }

            string? enginePath = FindWallpaperEngineExe();
            bool isEngineAvailable = !string.IsNullOrEmpty(enginePath);

            try
            {
                if (isEngineAvailable)
                {
                    // If WE is available, use it for EVERYTHING (Images and Engine configs)
                    // WE supports local files via -file
                    Log($"WE Available ({enginePath}). Using WE for wallpaper.");
                    ApplyWallpaperEngine(config.Value);
                    _currentAppliedWallpaper = config.Value; // Update state
                }
                else
                {
                    // WE not found
                    if (config.Type == "Engine")
                    {
                        // If it's a file path, we might be able to set it as standard wallpaper?
                        // If it's an ID, we can't do anything.
                        if (File.Exists(config.Value))
                        {
                            Log("WE missing, but value is file. Falling back to standard wallpaper.");
                            ApplyStandardWallpaper(config.Value);
                        }
                        else
                        {
                            Log("Wallpaper Engine not detected. Cannot apply Engine ID/Wallpaper.");
                        }
                    }
                    else
                    {
                        // Standard Image, WE missing -> Standard fallback
                        Log("WE missing. Using standard wallpaper API.");
                        ApplyStandardWallpaper(config.Value);
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Failed to apply wallpaper for page {page}: {ex.Message}");
            }
        }

        public void StopWallpaperEngine()
        {
            // Only stop if we are explicitly confident we have it?
            // Or just try blindly?
            // If we are using WE for images now, we rarely "Stop" it unless needed.
            
            Log("Stopping Wallpaper Engine...");
            try 
            {
                 RunWallpaperEngineCommand("-control close");
                 _currentAppliedWallpaper = null; // Reset state
            } 
            catch (Exception ex)
            {
                Log($"Error stopping engine: {ex.Message}");
            }
        }

        public WallpaperConfig? GetConfig(int page)
        {
            if (_wallpapers.TryGetValue(page, out var config))
            {
                return config;
            }
            return null;
        }

        private void ApplyStandardWallpaper(string path)
        {
            Log($"Applying Standard Wallpaper: {path}");
            
            // Ensure WE is closed so it doesn't cover the standard wallpaper
            StopWallpaperEngine();

            if (!File.Exists(path)) 
            {
                Log("Image file not found.");
                return;
            }

            NativeMethods.SystemParametersInfo(
                NativeMethods.SPI_SETDESKWALLPAPER, 
                0, 
                path, 
                NativeMethods.SPIF_UPDATEINIFILE | NativeMethods.SPIF_SENDWININICHANGE
            );
            _currentAppliedWallpaper = null; // Standard wallpaper resets WE state
        }

        private string? ResolveWorkshopPath(string id)
        {
            // Prerequisite: We need the engine path to find 'steamapps'
            string? exePath = FindWallpaperEngineExe();
            if (string.IsNullOrEmpty(exePath)) return null;

            // Typical Structure:
            // .../steamapps/common/wallpaper_engine/wallpaper64.exe
            // .../steamapps/workshop/content/431960/<ID>/project.json

            try
            {
                // Go up from exe to 'wallpaper_engine' to 'common' to 'steamapps'
                // exePath = .../wallpaper64.exe
                string? dir1 = Path.GetDirectoryName(exePath); // wallpaper_engine
                if (dir1 == null) return null;
                string? dir2 = Path.GetDirectoryName(dir1);    // common
                if (dir2 == null) return null;
                string? steamapps = Path.GetDirectoryName(dir2); // steamapps
                if (steamapps == null) return null;

                string workshopContent = Path.Combine(steamapps, "workshop", "content", "431960", id);
                
                if (Directory.Exists(workshopContent))
                {
                    // Priority 1: project.json (Scene)
                    string project = Path.Combine(workshopContent, "project.json");
                    if (File.Exists(project)) return project;

                    // Priority 2: scene.pkg (Packaged Scene)
                    string genericPkg = Path.Combine(workshopContent, "scene.pkg");
                    if (File.Exists(genericPkg)) return genericPkg;
                    
                    // Priority 3: Video files
                    var videos = Directory.GetFiles(workshopContent, "*.mp4");
                    if (videos.Length > 0) return videos[0];
                    videos = Directory.GetFiles(workshopContent, "*.webm");
                    if (videos.Length > 0) return videos[0];

                    // Priority 4: Web
                    string index = Path.Combine(workshopContent, "index.html");
                    if (File.Exists(index)) return index;
                    
                    // Priority 5: Any file? (Risky, but maybe)
                    // Let's stick to known types for now.
                }
            }
            catch (Exception ex)
            {
                Log($"Error resolving workshop path: {ex.Message}");
            }
            
            return null;
        }

        private void ApplyWallpaperEngine(string idOrPath)
        {
            idOrPath = idOrPath.Trim();
            Log($"Input value: '{idOrPath}'");
            
            // Smart Parsing (URL -> ID) ... (Existing logic) ...
            if (idOrPath.Contains("steamcommunity.com") && idOrPath.Contains("id="))
            {
                try 
                {
                    int index = idOrPath.IndexOf("id=");
                    if (index != -1)
                    {
                        string sub = idOrPath.Substring(index + 3);
                        string extracted = "";
                        foreach(char c in sub) { if (char.IsDigit(c)) extracted += c; else break; }
                        if (!string.IsNullOrEmpty(extracted)) { Log($"Extracted ID: {extracted}"); idOrPath = extracted; }
                    }
                } catch {}
            }

            string args = "";
            
            // If it identifies as an ID (numeric), we MUST resolve it to a file path
            // because -workshopId flag does NOT exist in CLI.
            if (ulong.TryParse(idOrPath, out _))
            {
                Log($"Input is ID: {idOrPath}. Attempting to resolve file path...");
                string? resolvedPath = ResolveWorkshopPath(idOrPath);
                
                if (!string.IsNullOrEmpty(resolvedPath))
                {
                    Log($"Resolved ID {idOrPath} -> {resolvedPath}");
                    args = $"-control openWallpaper -file \"{resolvedPath}\"";
                }
                else
                {
                    Log($"Could not resolve file path for ID {idOrPath}. Is it downloaded?");
                    return; // Cannot open
                }
            }
            else
            {
                Log($"Identified as File Path: {idOrPath}");
                args = $"-control openWallpaper -file \"{idOrPath}\"";
            }

            // We do NOT need to close before opening. 
            // WE handles switching internally.
            RunWallpaperEngineCommand(args);
        }

        private string? FindWallpaperEngineExe()
        {
            if (_cachedEnginePath != null && File.Exists(_cachedEnginePath)) return _cachedEnginePath;
            
            // 1. Check known paths
            string[] commonPaths = 
            {
                @"C:\Program Files (x86)\Steam\steamapps\common\wallpaper_engine\wallpaper64.exe",
                @"D:\Steam\steamapps\common\wallpaper_engine\wallpaper64.exe",
                @"E:\Steam\steamapps\common\wallpaper_engine\wallpaper64.exe",
                @"D:\SteamLibrary\steamapps\common\wallpaper_engine\wallpaper64.exe",
                @"E:\SteamLibrary\steamapps\common\wallpaper_engine\wallpaper64.exe"
            };

            foreach (var p in commonPaths)
            {
                if (File.Exists(p)) 
                {
                    _cachedEnginePath = p;
                    return p;
                }
            }

            // 2. Registry Check
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                if (key != null)
                {
                    var steamPath = key.GetValue("SteamPath")?.ToString();
                    if (!string.IsNullOrEmpty(steamPath))
                    {
                        steamPath = steamPath.Replace("/", "\\");
                        var fullPath = Path.Combine(steamPath, @"steamapps\common\wallpaper_engine\wallpaper64.exe");
                        if (File.Exists(fullPath)) 
                        {
                            _cachedEnginePath = fullPath;
                            return fullPath;
                        }
                    }
                }
            }
            catch { }

            // 3. Last resort: PATH check logic? 
            // If we return null, we assume NOT installed.
            // If the user has it in PATH but not in standard locations, we might miss it.
            // But checking PATH in C# safely is tedious (splitting env var).
            // Let's rely on standard detection. If it returns null, we fallback to standard images.
            return null;
        }

        private void RunWallpaperEngineCommand(string arguments)
        {
            string? exePath = FindWallpaperEngineExe();
            if (string.IsNullOrEmpty(exePath))
            {
                Log("Wallpaper Engine EXE not found. Cannot execute command.");
                return;
            }

            Log($"Executing: {exePath} {arguments}");

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                Log($"Command Execution Failed: {ex.Message}");
            }
        }
    }
}
