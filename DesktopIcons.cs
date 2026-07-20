using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text;
using System.Threading;

namespace DesktopPager
{
    public static class DesktopIcons
    {
        // Win32 Constants
        private const uint LVM_FIRST = 0x1000;
        private const uint LVM_GETITEMCOUNT = LVM_FIRST + 4;
        private const uint LVM_SETITEMPOSITION = LVM_FIRST + 15;
        private const uint LVM_GETITEMPOSITION = LVM_FIRST + 16;
        private const uint LVM_SETITEMPOSITION32 = LVM_FIRST + 49; // 0x1031
        private const uint LVM_GETITEMTEXTW = LVM_FIRST + 115;

        private const uint PROCESS_VM_OPERATION = 0x0008;
        private const uint PROCESS_VM_READ = 0x0010;
        private const uint PROCESS_VM_WRITE = 0x0020;
        
        private const uint MEM_COMMIT = 0x1000;
        private const uint MEM_RESERVE = 0x2000;
        private const uint MEM_RELEASE = 0x8000;
        private const uint PAGE_READWRITE = 0x04;

        private const uint LVIF_TEXT = 0x0001;

        private const uint LVM_GETEXTENDEDLISTVIEWSTYLE = LVM_FIRST + 55;
        private const uint LVM_SETEXTENDEDLISTVIEWSTYLE = LVM_FIRST + 54;
        private const uint LVS_EX_AUTOAUTOARRANGE = 0x01000000;

        // P/Invoke Definitions
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string className, string windowTitle);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint dwFreeType);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, IntPtr lpBuffer, uint nSize, out IntPtr lpNumberOfBytesWritten);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, IntPtr lpBuffer, uint nSize, out IntPtr lpNumberOfBytesRead);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct Point
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LVITEM
        {
            public uint mask;
            public int iItem;
            public int iSubItem;
            public uint state;
            public uint stateMask;
            public IntPtr pszText;
            public int cchTextMax;
            public int iImage;
            public IntPtr lParam;
            public int iIndent;
            public int iGroupId;
            public uint cColumns;
            public IntPtr puColumns;
            public IntPtr piColFmt;
            public int iGroup;
        }

        private static string GetLogPath()
        {
             return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "DesktopPager", "System", "error_log.txt");
        }

        private static void LogError(string msg)
        {
            return;
            /* try
            {
                string path = GetLogPath();
                string? dir = Path.GetDirectoryName(path);
                if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.AppendAllText(path, $"[DesktopIcons] {DateTime.Now}: {msg}{Environment.NewLine}");
            }
            catch { } */
        }

        // Serializable class for JSON
        private class IconPosition
        {
            public int X { get; set; }
            public int Y { get; set; }
        }

        public static void SaveLayout(string path)
        {
            try
            {
                Dictionary<string, Point> icons = new Dictionary<string, Point>();
                
                // Retry logic: sometimes getting icons fails (returns 0) if desktop is refreshing
                for (int i = 0; i < 5; i++)
                {
                    icons = GetIconPositions();
                    if (icons.Count > 0) break;
                    Thread.Sleep(200);
                }

                // If we still have 0 icons, avoiding overwriting the file with empty data might be safer
                // UNLESS the user really has 0 icons. 
                // But usually, one has at least Recycle Bin. 
                if (icons.Count == 0)
                {
                    LogError($"SaveLayout: Warning - 0 icons found. Skipping save to avoid data loss for {path}");
                    return;
                }
                
                // Convert Point to IconPosition for JSON serialization
                var serializableIcons = new Dictionary<string, IconPosition>();
                foreach (var kvp in icons)
                {
                    serializableIcons[kvp.Key] = new IconPosition { X = kvp.Value.X, Y = kvp.Value.Y };
                }
                
                string json = JsonSerializer.Serialize(serializableIcons, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
                
                LogError($"SaveLayout: Saved {serializableIcons.Count} icon positions to {path}");
            }
            catch (Exception ex)
            {
                LogError($"Failed to save layout to {path}: {ex.Message}");
            }
        }

        public static void RestoreLayout(string path)
        {
            try
            {
                if (!File.Exists(path)) return;
                
                string json = File.ReadAllText(path);
                var serializableIcons = JsonSerializer.Deserialize<Dictionary<string, IconPosition>>(json);
                
                if (serializableIcons != null && serializableIcons.Count > 0)
                {
                    // Convert IconPosition back to Point
                    var icons = new Dictionary<string, Point>();
                    foreach (var kvp in serializableIcons)
                    {
                        icons[kvp.Key] = new Point { X = kvp.Value.X, Y = kvp.Value.Y };
                    }
                    
                    int expectedCount = icons.Count;
                    LogError($"RestoreLayout: Start (Expecting {expectedCount} icons)");
                    
                    // Continuous Loop
                    // We try for a fixed duration (e.g. 4 seconds) to catch icons as they appear.
                    // We do NOT break early unless we have placed ALL matched icons successfully.
                    int maxAttempts = 30; // 30 * 150ms ~ 4.5 seconds
                    
                    for (int attempt = 0; attempt < maxAttempts; attempt++)
                    {
                        IntPtr hWnd = GetDesktopListView();
                        if (hWnd == IntPtr.Zero)
                        {
                            Thread.Sleep(100);
                            continue;
                        }

                        // Always ensure auto-arrange is off first
                        DisableAutoArrange(hWnd);

                        // Try to position what we have
                        int placedCount = SetIconPositions(icons);
                        
                        if (placedCount >= expectedCount)
                        {
                             LogError($"RestoreLayout: All {placedCount} icons placed successfully. Done.");
                             break;
                        }

                        // Log progress periodically (e.g. every 5th attempt)
                        if (attempt % 5 == 0)
                        {
                            LogError($"RestoreLayout: Placed {placedCount}/{expectedCount} icons... (Attempt {attempt})");
                        }
                        
                        Thread.Sleep(150);
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"Failed to restore layout from {path}: {ex.Message}");
            }
        }

        public static void SaveShortcutPositions(string path, List<string> shortcutNames)
        {
            try
            {
                var allPositions = GetIconPositions();
                var shortcutPositions = new Dictionary<string, IconPosition>();
                
                foreach (var name in shortcutNames)
                {
                    if (allPositions.ContainsKey(name))
                    {
                        var pt = allPositions[name];
                        shortcutPositions[name] = new IconPosition { X = pt.X, Y = pt.Y };
                    }
                }
                
                if (shortcutPositions.Count > 0)
                {
                    string json = JsonSerializer.Serialize(shortcutPositions, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(path, json);
                    LogError($"SaveShortcutPositions: Saved {shortcutPositions.Count} shortcut positions");
                }
            }
            catch (Exception ex)
            {
                LogError($"Failed to save shortcut positions to {path}: {ex.Message}");
            }
        }

        public static void RestoreShortcutPositions(string path)
        {
            try
            {
                if (!File.Exists(path)) return;
                
                string json = File.ReadAllText(path);
                var shortcutPositions = JsonSerializer.Deserialize<Dictionary<string, IconPosition>>(json);
                
                if (shortcutPositions != null && shortcutPositions.Count > 0)
                {
                    // Convert to Point dictionary
                    var positions = new Dictionary<string, Point>();
                    foreach (var kvp in shortcutPositions)
                    {
                        positions[kvp.Key] = new Point { X = kvp.Value.X, Y = kvp.Value.Y };
                    }
                    
                    LogError($"RestoreShortcutPositions: Applying {positions.Count} shortcut positions");
                    
                    // Apply positions
                    IntPtr hWnd = GetDesktopListView();
                    if (hWnd != IntPtr.Zero)
                    {
                        DisableAutoArrange(hWnd);
                        Thread.Sleep(50);
                        SetIconPositions(positions);
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"Failed to restore shortcut positions from {path}: {ex.Message}");
            }
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        private const int GWL_STYLE = -16;
        private const uint LVS_AUTOARRANGE = 0x0100;

        private static void DisableAutoArrange(IntPtr hWnd)
        {
            try
            {
                // 1. Clear Extended Style (LVS_EX_AUTOAUTOARRANGE)
                IntPtr currentExStyle = SendMessage(hWnd, LVM_GETEXTENDEDLISTVIEWSTYLE, IntPtr.Zero, IntPtr.Zero);
                IntPtr newExStyle = (IntPtr)((long)currentExStyle & ~LVS_EX_AUTOAUTOARRANGE);
                SendMessage(hWnd, LVM_SETEXTENDEDLISTVIEWSTYLE, IntPtr.Zero, newExStyle);

                // 2. Clear Standard Style (LVS_AUTOARRANGE)
                // This is often the primary culprit for "Snap to Grid/Auto Arrange" behavior
                IntPtr style = GetWindowLongPtr(hWnd, GWL_STYLE);
                long styleLong = (long)style;
                
                if ((styleLong & LVS_AUTOARRANGE) == LVS_AUTOARRANGE)
                {
                    LogError("Disabling LVS_AUTOARRANGE standard style...");
                    styleLong &= ~LVS_AUTOARRANGE;
                    SetWindowLongPtr(hWnd, GWL_STYLE, (IntPtr)styleLong);
                }
            }
            catch (Exception ex)
            {
                LogError($"Failed to disable auto-arrange: {ex.Message}");
            }
        }

        private static Dictionary<string, Point> GetIconPositions()
        {
            var result = new Dictionary<string, Point>();
            IntPtr hWnd = GetDesktopListView();
            if (hWnd == IntPtr.Zero) 
            {
                LogError("GetDesktopListView returned zero.");
                return result;
            }

            GetWindowThreadProcessId(hWnd, out uint processId);
            IntPtr hProcess = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE, false, processId);
            if (hProcess == IntPtr.Zero) 
            {
                LogError($"OpenProcess failed. Error: {Marshal.GetLastWin32Error()}");
                return result;
            }

            try
            {
                int count = (int)SendMessage(hWnd, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
                if (count <= 0) 
                {
                    // This is common if desktop is refreshing, so we won't log error always, but good for debug
                    return result; 
                }

                // Allocate memory in remote process
                IntPtr ptAddress = VirtualAllocEx(hProcess, IntPtr.Zero, (uint)Marshal.SizeOf(typeof(Point)), MEM_COMMIT, PAGE_READWRITE);
                
                // LVITEM struct + text buffer
                int textSize = 512;
                int lvItemSize = Marshal.SizeOf(typeof(LVITEM));
                uint totalSize = (uint)(lvItemSize + textSize);
                IntPtr lvItemAddress = VirtualAllocEx(hProcess, IntPtr.Zero, totalSize, MEM_COMMIT, PAGE_READWRITE);
                IntPtr textAddress = IntPtr.Add(lvItemAddress, lvItemSize);

                for (int i = 0; i < count; i++)
                {
                    // 1. Get Text
                    LVITEM lvi = new LVITEM();
                    lvi.mask = LVIF_TEXT;
                    lvi.cchTextMax = textSize / 2; // WCHAR
                    lvi.pszText = textAddress;
                    lvi.iItem = i;
                    lvi.iSubItem = 0;

                    // Write struct to remote
                    IntPtr localLvItem = Marshal.AllocHGlobal(lvItemSize);
                    Marshal.StructureToPtr(lvi, localLvItem, false);
                    WriteProcessMemory(hProcess, lvItemAddress, localLvItem, (uint)lvItemSize, out _);
                    Marshal.FreeHGlobal(localLvItem);

                    // Send Message
                    SendMessage(hWnd, LVM_GETITEMTEXTW, (IntPtr)i, lvItemAddress);

                    // Read text back
                    IntPtr localText = Marshal.AllocHGlobal(textSize);
                    ReadProcessMemory(hProcess, textAddress, localText, (uint)textSize, out _);
                    string text = Marshal.PtrToStringUni(localText);
                    Marshal.FreeHGlobal(localText);

                    // 2. Get Position
                    // Use LVM_GETITEMPOSITION (0x1010)
                    SendMessage(hWnd, LVM_GETITEMPOSITION, (IntPtr)i, ptAddress);
                    
                    IntPtr localPt = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Point)));
                    ReadProcessMemory(hProcess, ptAddress, localPt, (uint)Marshal.SizeOf(typeof(Point)), out _);
                    Point pt = Marshal.PtrToStructure<Point>(localPt);
                    Marshal.FreeHGlobal(localPt);

                    LogError($"GetIconPositions: Read '{text}' at position ({pt.X}, {pt.Y})");

                    if (!string.IsNullOrEmpty(text) && !result.ContainsKey(text))
                    {
                        result[text] = pt;
                    }
                }

                LogError($"GetIconPositions: Total {result.Count} positions saved");

                VirtualFreeEx(hProcess, ptAddress, 0, MEM_RELEASE);
                VirtualFreeEx(hProcess, lvItemAddress, 0, MEM_RELEASE);
            }
            finally
            {
                // Unconditional close handle not available without kernel32 import, relies on OS process cleanup
            }

            return result;
        }

        private static int SetIconPositions(Dictionary<string, Point> positions)
        {
            int matchedCount = 0;
            IntPtr hWnd = GetDesktopListView();
            if (hWnd == IntPtr.Zero) return 0;

            GetWindowThreadProcessId(hWnd, out uint processId);
            IntPtr hProcess = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE, false, processId);
            if (hProcess == IntPtr.Zero) return 0;

            try
            {
                int count = (int)SendMessage(hWnd, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
                if (count <= 0) return 0;

                // LogError($"SetIconPositions: Scanning {count} icons...");

                int textSize = 512;
                int lvItemSize = Marshal.SizeOf(typeof(LVITEM));
                int pointSize = Marshal.SizeOf(typeof(Point));
                uint totalSize = (uint)(lvItemSize + textSize + pointSize);
                
                // Allocate for LVITEM, Text, and Point
                IntPtr remoteMem = VirtualAllocEx(hProcess, IntPtr.Zero, totalSize, MEM_COMMIT, PAGE_READWRITE);
                IntPtr lvItemAddress = remoteMem;
                IntPtr textAddress = IntPtr.Add(remoteMem, lvItemSize);
                IntPtr pointAddress = IntPtr.Add(remoteMem, lvItemSize + textSize);

                for (int i = 0; i < count; i++)
                {
                    // Get Text to identify icon
                    LVITEM lvi = new LVITEM();
                    lvi.mask = LVIF_TEXT;
                    lvi.cchTextMax = textSize / 2;
                    lvi.pszText = textAddress;
                    lvi.iItem = i;
                    
                    IntPtr localLvItem = Marshal.AllocHGlobal(lvItemSize);
                    Marshal.StructureToPtr(lvi, localLvItem, false);
                    WriteProcessMemory(hProcess, lvItemAddress, localLvItem, (uint)lvItemSize, out _);
                    Marshal.FreeHGlobal(localLvItem);

                    SendMessage(hWnd, LVM_GETITEMTEXTW, (IntPtr)i, lvItemAddress);

                    IntPtr localText = Marshal.AllocHGlobal(textSize);
                    ReadProcessMemory(hProcess, textAddress, localText, (uint)textSize, out _);
                    string text = Marshal.PtrToStringUni(localText);
                    Marshal.FreeHGlobal(localText);

                    // If matches, set position
                    if (!string.IsNullOrEmpty(text) && positions.ContainsKey(text))
                    {
                        Point pt = positions[text];
                        
                        // Use LVM_SETITEMPOSITION32 (0x1031)
                        // It requires a pointer to a POINT structure in the remote process
                        
                        IntPtr localPt = Marshal.AllocHGlobal(pointSize);
                        Marshal.StructureToPtr(pt, localPt, false);
                        WriteProcessMemory(hProcess, pointAddress, localPt, (uint)pointSize, out _);
                        Marshal.FreeHGlobal(localPt);

                        SendMessage(hWnd, LVM_SETITEMPOSITION32, (IntPtr)i, pointAddress);
                        matchedCount++;
                    }
                }
                
                VirtualFreeEx(hProcess, remoteMem, 0, MEM_RELEASE);
            }
            catch (Exception ex)
            {
                LogError($"Error during SetIconPositions: {ex.Message}");
            }
            finally
            {
                // Cleanup
            }
            return matchedCount;
        }

        private static long MakeLParam(int x, int y)
        {
            return (long)((ulong)((ushort)x) | ((ulong)((ushort)y) << 16));
        }

        private static IntPtr GetDesktopListView()
        {
            IntPtr progman = FindWindow("Progman", null);
            IntPtr defView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            
            if (defView == IntPtr.Zero)
            {
                // Try WorkerW for Windows 7+ with wallpapers
                IntPtr workerW = IntPtr.Zero;
                while (true)
                {
                    workerW = FindWindowEx(IntPtr.Zero, workerW, "WorkerW", null);
                    if (workerW == IntPtr.Zero) break;

                    defView = FindWindowEx(workerW, IntPtr.Zero, "SHELLDLL_DefView", null);
                    if (defView != IntPtr.Zero) break;
                }
            }

            return FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
        }
    }
}
