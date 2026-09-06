using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace WinCleaner
{
    public enum CategoryType
    {
        UserTemp,
        SystemTemp,
        RecycleBin,
        WindowsUpdate,
        CrashDumps,
        Thumbnails,
        WindowsLogs,
        ChromeCache,
        EdgeCache,
        FirefoxCache,
        InternetExplorerCache,
        UserDownloads
    }

    public enum RouletteTargetMode
    {
        FilesOnly,
        FoldersOnly,
        All
    }

    public class CategoryItem
    {
        public CategoryType Type { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public bool RequiresAdmin { get; set; }
        public bool IsChecked { get; set; }
        public long ScannedBytes { get; set; }
        public int ScannedFiles { get; set; }
        public List<string> TargetDirectories { get; set; }
        public bool IsCustomLogic { get; set; }

        public CategoryItem()
        {
            TargetDirectories = new List<string>();
        }

        public override string ToString()
        {
            if (ScannedBytes > 0)
            {
                return string.Format("{0} ({1} in {2:N0} files)", Name, CleanerEngine.FormatBytes(ScannedBytes), ScannedFiles);
            }
            return Name;
        }
    }

    public class CustomCleanTarget
    {
        public string TargetPath { get; set; }
        public bool IsDirectory { get; set; }
        public bool DeleteRootFolder { get; set; } // false: empty contents; true: delete folder itself
        public bool IsChecked { get; set; }
        public long ScannedBytes { get; set; }
        public int ScannedFiles { get; set; }

        public CustomCleanTarget()
        {
            IsChecked = true;
            DeleteRootFolder = false; // default safe: empty contents
        }

        public CustomCleanTarget(string path, bool isDirectory, bool deleteRootFolder = false)
        {
            TargetPath = path;
            IsDirectory = isDirectory;
            DeleteRootFolder = deleteRootFolder;
            IsChecked = true;
        }
    }

    public class ScanSummary
    {
        public long TotalBytes { get; set; }
        public int TotalFiles { get; set; }
        public int TotalCategoriesScanned { get; set; }
    }

    public class CleanSummary
    {
        public long BytesFreed { get; set; }
        public int FilesDeleted { get; set; }
        public int FilesSkipped { get; set; }
        public int Errors { get; set; }
    }

    public class SandboxEntry
    {
        public string Name { get; set; }
        public string FullPath { get; set; }
        public bool IsDirectory { get; set; }
        public long Size { get; set; }
        public int ItemCount { get; set; }
        public DateTime CreatedTime { get; set; }
    }

    public class CleanerEngine
    {
        public volatile bool CancelRequested = false;

        public event Action<string> LogMessage;
        public event Action<int, int, string> ProgressChanged; // current, total, description

        #region Win32 API for Recycle Bin
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct SHQUERYRBINFO
        {
            public int cbSize;
            public long i64Size;
            public long i64NumItems;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern int SHQueryRecycleBin(string pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern int SHEmptyRecycleBin(IntPtr hwnd, string pszRootPath, uint dwFlags);

        private const uint SHERB_NOCONFIRMATION = 0x00000001;
        private const uint SHERB_NOPROGRESSUI = 0x00000002;
        private const uint SHERB_NOSOUND = 0x00000004;
        #endregion

        public static bool IsAdministrator()
        {
            try
            {
                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 B";
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int index = 0;
            double size = bytes;
            while (size >= 1024.0 && index < suffixes.Length - 1)
            {
                size /= 1024.0;
                index++;
            }
            return string.Format("{0:0.##} {1}", size, suffixes[index]);
        }

        #region Safety Path Protection Guard

        public static bool IsProtectedPath(string path, out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrEmpty(path))
            {
                reason = "Path cannot be empty.";
                return true;
            }

            try
            {
                string fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string root = Path.GetPathRoot(fullPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                // 1. Root Drives (e.g. C:, D:)
                if (string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
                {
                    reason = "Drive root (e.g. " + fullPath + ") cannot be deleted.";
                    return true;
                }

                // 2. Windows Directory (e.g. C:\Windows, C:\Windows\System32)
                string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd(Path.DirectorySeparatorChar);
                if (string.Equals(fullPath, winDir, StringComparison.OrdinalIgnoreCase) ||
                    fullPath.StartsWith(winDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    string winTemp = Path.Combine(winDir, "Temp");
                    if (!string.Equals(fullPath, winTemp, StringComparison.OrdinalIgnoreCase))
                    {
                        reason = "Windows system directories cannot be targeted for user deletion.";
                        return true;
                    }
                }

                // 3. Program Files
                string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles).TrimEnd(Path.DirectorySeparatorChar);
                if (string.Equals(fullPath, progFiles, StringComparison.OrdinalIgnoreCase) ||
                    fullPath.StartsWith(progFiles + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    reason = "Program Files directory cannot be targeted for user deletion.";
                    return true;
                }

                string progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86).TrimEnd(Path.DirectorySeparatorChar);
                if (!string.IsNullOrEmpty(progFilesX86))
                {
                    if (string.Equals(fullPath, progFilesX86, StringComparison.OrdinalIgnoreCase) ||
                        fullPath.StartsWith(progFilesX86 + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    {
                        reason = "Program Files (x86) directory cannot be targeted for user deletion.";
                        return true;
                    }
                }

                // 4. User Profile Root (e.g. C:\Users\Username)
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd(Path.DirectorySeparatorChar);
                if (string.Equals(fullPath, userProfile, StringComparison.OrdinalIgnoreCase))
                {
                    reason = "User profile root (" + fullPath + ") cannot be deleted.";
                    return true;
                }

                // 5. AppData Root
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData).TrimEnd(Path.DirectorySeparatorChar);
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData).TrimEnd(Path.DirectorySeparatorChar);
                if (string.Equals(fullPath, appData, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(fullPath, localAppData, StringComparison.OrdinalIgnoreCase))
                {
                    reason = "Root AppData directory cannot be deleted.";
                    return true;
                }

                // 6. User Standard Roots (Desktop root, Documents root, Pictures root)
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop).TrimEnd(Path.DirectorySeparatorChar);
                string personal = Environment.GetFolderPath(Environment.SpecialFolder.Personal).TrimEnd(Path.DirectorySeparatorChar);
                string myPictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures).TrimEnd(Path.DirectorySeparatorChar);

                if (!string.IsNullOrEmpty(desktop) && string.Equals(fullPath, desktop, StringComparison.OrdinalIgnoreCase))
                {
                    reason = "Desktop root cannot be targeted directly. Please select or create a dedicated subfolder inside Desktop.";
                    return true;
                }
                if (!string.IsNullOrEmpty(personal) && string.Equals(fullPath, personal, StringComparison.OrdinalIgnoreCase))
                {
                    reason = "Documents root cannot be targeted directly. Please select or create a dedicated subfolder inside Documents.";
                    return true;
                }
                if (!string.IsNullOrEmpty(myPictures) && string.Equals(fullPath, myPictures, StringComparison.OrdinalIgnoreCase))
                {
                    reason = "Pictures root cannot be targeted directly. Please select or create a dedicated subfolder inside Pictures.";
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                reason = "Invalid path: " + ex.Message;
                return true;
            }
        }

        #endregion

        #region Built-in Categories Setup

        public List<CategoryItem> GetDefaultCategories()
        {
            List<CategoryItem> list = new List<CategoryItem>();
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

            // 1. User Temporary Files
            var userTemp = new CategoryItem
            {
                Type = CategoryType.UserTemp,
                Name = "User Temporary Files",
                Description = "Temporary files created by running user programs and installers (%TEMP%).",
                RequiresAdmin = false,
                IsChecked = true
            };
            string tempEnv = Path.GetTempPath();
            if (Directory.Exists(tempEnv)) userTemp.TargetDirectories.Add(tempEnv);
            string localTemp = Path.Combine(localAppData, "Temp");
            if (Directory.Exists(localTemp) && !userTemp.TargetDirectories.Contains(localTemp))
                userTemp.TargetDirectories.Add(localTemp);
            list.Add(userTemp);

            // 2. Recycle Bin
            var recycleBin = new CategoryItem
            {
                Type = CategoryType.RecycleBin,
                Name = "Recycle Bin",
                Description = "Files deleted by the user that are currently in the Windows Recycle Bin.",
                RequiresAdmin = false,
                IsChecked = true,
                IsCustomLogic = true
            };
            list.Add(recycleBin);

            // 3. System Temporary Files
            var sysTemp = new CategoryItem
            {
                Type = CategoryType.SystemTemp,
                Name = "Windows System Temp Files",
                Description = "System-level temporary files created by Windows services and installers (C:\\Windows\\Temp).",
                RequiresAdmin = true,
                IsChecked = true
            };
            string winTemp = Path.Combine(winDir, "Temp");
            if (Directory.Exists(winTemp)) sysTemp.TargetDirectories.Add(winTemp);
            list.Add(sysTemp);

            // 4. Windows Update Cache
            var winUpdate = new CategoryItem
            {
                Type = CategoryType.WindowsUpdate,
                Name = "Windows Update Download Cache",
                Description = "Downloaded installation packages for Windows Updates that have already been installed.",
                RequiresAdmin = true,
                IsChecked = true
            };
            string swDist = Path.Combine(winDir, @"SoftwareDistribution\Download");
            if (Directory.Exists(swDist)) winUpdate.TargetDirectories.Add(swDist);
            list.Add(winUpdate);

            // 5. Crash Dumps & Error Reports
            var crashDumps = new CategoryItem
            {
                Type = CategoryType.CrashDumps,
                Name = "Crash Dumps & Error Reports",
                Description = "Application crash dumps (.dmp) and Windows Error Reporting (WER) logs.",
                RequiresAdmin = false,
                IsChecked = true
            };
            AddDirIfExists(crashDumps.TargetDirectories, Path.Combine(localAppData, "CrashDumps"));
            AddDirIfExists(crashDumps.TargetDirectories, Path.Combine(localAppData, @"Microsoft\Windows\WER\ReportArchive"));
            AddDirIfExists(crashDumps.TargetDirectories, Path.Combine(localAppData, @"Microsoft\Windows\WER\ReportQueue"));
            AddDirIfExists(crashDumps.TargetDirectories, Path.Combine(programData, @"Microsoft\Windows\WER\ReportArchive"));
            AddDirIfExists(crashDumps.TargetDirectories, Path.Combine(programData, @"Microsoft\Windows\WER\ReportQueue"));
            list.Add(crashDumps);

            // 6. Thumbnail Cache
            var thumbs = new CategoryItem
            {
                Type = CategoryType.Thumbnails,
                Name = "Windows Explorer Thumbnail Cache",
                Description = "Cached image/video thumbnails used by Windows Explorer.",
                RequiresAdmin = false,
                IsChecked = false
            };
            AddDirIfExists(thumbs.TargetDirectories, Path.Combine(localAppData, @"Microsoft\Windows\Explorer"));
            list.Add(thumbs);

            // 7. Windows Logs
            var winLogs = new CategoryItem
            {
                Type = CategoryType.WindowsLogs,
                Name = "Windows Log Files",
                Description = "Old CBS, DISM, and setup log files.",
                RequiresAdmin = true,
                IsChecked = false
            };
            AddDirIfExists(winLogs.TargetDirectories, Path.Combine(winDir, @"Logs\CBS"));
            AddDirIfExists(winLogs.TargetDirectories, Path.Combine(winDir, @"Logs\DISM"));
            AddDirIfExists(winLogs.TargetDirectories, Path.Combine(winDir, "Panther"));
            list.Add(winLogs);

            // 8. Google Chrome Cache
            var chrome = new CategoryItem
            {
                Type = CategoryType.ChromeCache,
                Name = "Google Chrome Cache",
                Description = "Cached web pages, images, and script assets from Google Chrome.",
                RequiresAdmin = false,
                IsChecked = true
            };
            string chromeUser = Path.Combine(localAppData, @"Google\Chrome\User Data\Default");
            AddDirIfExists(chrome.TargetDirectories, Path.Combine(chromeUser, "Cache"));
            AddDirIfExists(chrome.TargetDirectories, Path.Combine(chromeUser, "Code Cache"));
            AddDirIfExists(chrome.TargetDirectories, Path.Combine(chromeUser, "GPUCache"));
            list.Add(chrome);

            // 9. Microsoft Edge Cache
            var edge = new CategoryItem
            {
                Type = CategoryType.EdgeCache,
                Name = "Microsoft Edge Cache",
                Description = "Cached web pages, images, and script assets from Microsoft Edge.",
                RequiresAdmin = false,
                IsChecked = true
            };
            string edgeUser = Path.Combine(localAppData, @"Microsoft\Edge\User Data\Default");
            AddDirIfExists(edge.TargetDirectories, Path.Combine(edgeUser, "Cache"));
            AddDirIfExists(edge.TargetDirectories, Path.Combine(edgeUser, "Code Cache"));
            AddDirIfExists(edge.TargetDirectories, Path.Combine(edgeUser, "GPUCache"));
            list.Add(edge);

            // 10. Mozilla Firefox Cache
            var firefox = new CategoryItem
            {
                Type = CategoryType.FirefoxCache,
                Name = "Mozilla Firefox Cache",
                Description = "Cached web pages and network files from Mozilla Firefox.",
                RequiresAdmin = false,
                IsChecked = true,
                IsCustomLogic = true
            };
            list.Add(firefox);

            // 11. Internet Explorer / Legacy Internet Cache
            var ie = new CategoryItem
            {
                Type = CategoryType.InternetExplorerCache,
                Name = "Internet Explorer / Legacy Internet Cache",
                Description = "Temporary internet files and offline webpage cache (Windows 7 / IE / Legacy Edge).",
                RequiresAdmin = false,
                IsChecked = true
            };
            string ieCache = Environment.GetFolderPath(Environment.SpecialFolder.InternetCache);
            AddDirIfExists(ie.TargetDirectories, ieCache);
            AddDirIfExists(ie.TargetDirectories, Path.Combine(localAppData, @"Microsoft\Windows\INetCache"));
            list.Add(ie);

            return list;
        }

        private void AddDirIfExists(List<string> list, string dir)
        {
            if (Directory.Exists(dir) && !list.Contains(dir))
            {
                list.Add(dir);
            }
        }

        #endregion

        #region Scanning Logic (Built-in Categories)

        public ScanSummary Scan(List<CategoryItem> categories)
        {
            CancelRequested = false;
            ScanSummary summary = new ScanSummary();
            int catIndex = 0;

            foreach (var cat in categories)
            {
                if (CancelRequested) break;
                catIndex++;
                cat.ScannedBytes = 0;
                cat.ScannedFiles = 0;

                if (!cat.IsChecked) continue;

                Log(string.Format("Scanning: {0}...", cat.Name));
                ReportProgress(catIndex, categories.Count, cat.Name);

                if (cat.Type == CategoryType.RecycleBin)
                {
                    ScanRecycleBin(cat);
                }
                else if (cat.Type == CategoryType.FirefoxCache)
                {
                    ScanFirefoxCache(cat);
                }
                else
                {
                    foreach (var dir in cat.TargetDirectories)
                    {
                        if (CancelRequested) break;
                        if (!Directory.Exists(dir)) continue;

                        ScanDirectory(dir, cat);
                    }
                }

                summary.TotalBytes += cat.ScannedBytes;
                summary.TotalFiles += cat.ScannedFiles;
                summary.TotalCategoriesScanned++;
                Log(string.Format("Found in {0}: {1} ({2:N0} files)", cat.Name, FormatBytes(cat.ScannedBytes), cat.ScannedFiles));
            }

            return summary;
        }

        private void ScanDirectory(string dirPath, CategoryItem cat)
        {
            try
            {
                DirectoryInfo dir = new DirectoryInfo(dirPath);
                FileInfo[] files = null;
                try
                {
                    files = dir.GetFiles("*", SearchOption.TopDirectoryOnly);
                }
                catch { }

                if (files != null)
                {
                    foreach (var file in files)
                    {
                        if (CancelRequested) return;
                        try
                        {
                            cat.ScannedBytes += file.Length;
                            cat.ScannedFiles++;
                        }
                        catch { }
                    }
                }

                DirectoryInfo[] subDirs = null;
                try
                {
                    subDirs = dir.GetDirectories("*", SearchOption.TopDirectoryOnly);
                }
                catch { }

                if (subDirs != null)
                {
                    foreach (var sub in subDirs)
                    {
                        if (CancelRequested) return;
                        ScanDirectory(sub.FullName, cat);
                    }
                }
            }
            catch { }
        }

        private void ScanRecycleBin(CategoryItem cat)
        {
            try
            {
                DriveInfo[] drives = DriveInfo.GetDrives();
                long totalSize = 0;
                long totalItems = 0;

                foreach (var drive in drives)
                {
                    if (drive.DriveType != DriveType.Fixed && drive.DriveType != DriveType.Removable)
                        continue;

                    try
                    {
                        SHQUERYRBINFO info = new SHQUERYRBINFO();
                        info.cbSize = Marshal.SizeOf(typeof(SHQUERYRBINFO));
                        int hr = SHQueryRecycleBin(drive.Name, ref info);
                        if (hr == 0)
                        {
                            totalSize += info.i64Size;
                            totalItems += info.i64NumItems;
                        }
                    }
                    catch { }
                }

                cat.ScannedBytes = totalSize;
                cat.ScannedFiles = (int)totalItems;
            }
            catch (Exception ex)
            {
                Log("Error scanning Recycle Bin: " + ex.Message);
            }
        }

        private void ScanFirefoxCache(CategoryItem cat)
        {
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string ffProfiles = Path.Combine(localAppData, @"Mozilla\Firefox\Profiles");
                if (Directory.Exists(ffProfiles))
                {
                    string[] profileDirs = Directory.GetDirectories(ffProfiles);
                    foreach (var pDir in profileDirs)
                    {
                        string cache2 = Path.Combine(pDir, "cache2");
                        if (Directory.Exists(cache2))
                        {
                            ScanDirectory(cache2, cat);
                        }
                        string jumpList = Path.Combine(pDir, "jumpListCache");
                        if (Directory.Exists(jumpList))
                        {
                            ScanDirectory(jumpList, cat);
                        }
                    }
                }
            }
            catch { }
        }

        #endregion

        #region Custom Targets (User Files & Folders)

        public ScanSummary ScanCustomTargets(List<CustomCleanTarget> targets)
        {
            CancelRequested = false;
            ScanSummary summary = new ScanSummary();
            int index = 0;

            foreach (var target in targets)
            {
                if (CancelRequested) break;
                index++;
                target.ScannedBytes = 0;
                target.ScannedFiles = 0;

                if (!target.IsChecked) continue;

                Log(string.Format("Scanning user target: {0}...", Path.GetFileName(target.TargetPath)));
                ReportProgress(index, targets.Count, Path.GetFileName(target.TargetPath));

                if (target.IsDirectory)
                {
                    if (Directory.Exists(target.TargetPath))
                    {
                        ScanDirectoryForCustomTarget(target.TargetPath, target);
                    }
                }
                else
                {
                    if (File.Exists(target.TargetPath))
                    {
                        try
                        {
                            FileInfo fi = new FileInfo(target.TargetPath);
                            target.ScannedBytes = fi.Length;
                            target.ScannedFiles = 1;
                        }
                        catch { }
                    }
                }

                summary.TotalBytes += target.ScannedBytes;
                summary.TotalFiles += target.ScannedFiles;
                summary.TotalCategoriesScanned++;
                Log(string.Format("Scanned {0}: {1} ({2:N0} files)",
                    target.TargetPath, FormatBytes(target.ScannedBytes), target.ScannedFiles));
            }

            return summary;
        }

        private void ScanDirectoryForCustomTarget(string dirPath, CustomCleanTarget target)
        {
            try
            {
                DirectoryInfo dir = new DirectoryInfo(dirPath);
                FileInfo[] files = null;
                try
                {
                    files = dir.GetFiles("*", SearchOption.TopDirectoryOnly);
                }
                catch { }

                if (files != null)
                {
                    foreach (var f in files)
                    {
                        if (CancelRequested) return;
                        try
                        {
                            target.ScannedBytes += f.Length;
                            target.ScannedFiles++;
                        }
                        catch { }
                    }
                }

                DirectoryInfo[] subDirs = null;
                try
                {
                    subDirs = dir.GetDirectories("*", SearchOption.TopDirectoryOnly);
                }
                catch { }

                if (subDirs != null)
                {
                    foreach (var sub in subDirs)
                    {
                        if (CancelRequested) return;
                        ScanDirectoryForCustomTarget(sub.FullName, target);
                    }
                }
            }
            catch { }
        }

        public CleanSummary CleanCustomTargets(List<CustomCleanTarget> targets)
        {
            CancelRequested = false;
            CleanSummary summary = new CleanSummary();
            int index = 0;

            foreach (var target in targets)
            {
                if (CancelRequested) break;
                index++;
                if (!target.IsChecked) continue;

                Log(string.Format(">>> Cleaning user target: {0}...", target.TargetPath));
                ReportProgress(index, targets.Count, "Cleaning " + Path.GetFileName(target.TargetPath));

                if (target.IsDirectory)
                {
                    if (Directory.Exists(target.TargetPath))
                    {
                        CleanDirectoryContents(target.TargetPath, summary);

                        if (target.DeleteRootFolder)
                        {
                            try
                            {
                                Directory.Delete(target.TargetPath, true);
                                summary.FilesDeleted++;
                                Log(string.Format("[DELETED FOLDER] {0}", target.TargetPath));
                            }
                            catch (Exception ex)
                            {
                                summary.Errors++;
                                Log(string.Format("[ERROR] Could not delete root folder {0}: {1}", target.TargetPath, ex.Message));
                            }
                        }
                    }
                }
                else
                {
                    if (File.Exists(target.TargetPath))
                    {
                        try
                        {
                            FileInfo fi = new FileInfo(target.TargetPath);
                            DeleteFileSafely(fi, summary);
                        }
                        catch (Exception ex)
                        {
                            summary.Errors++;
                            Log(string.Format("[ERROR] Could not delete file {0}: {1}", target.TargetPath, ex.Message));
                        }
                    }
                }

                target.ScannedBytes = 0;
                target.ScannedFiles = 0;
            }

            Log(string.Format("User clean complete. Deleted: {0:N0} files ({1}). Skipped (locked): {2:N0} files.",
                summary.FilesDeleted, FormatBytes(summary.BytesFreed), summary.FilesSkipped));

            return summary;
        }

        public static string GetCustomTargetsConfigPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string folder = Path.Combine(appData, "WinCleaner");
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }
            return Path.Combine(folder, "custom_targets.txt");
        }

        public void SaveCustomTargets(List<CustomCleanTarget> targets)
        {
            try
            {
                string configPath = GetCustomTargetsConfigPath();
                List<string> lines = new List<string>();
                foreach (var t in targets)
                {
                    lines.Add(string.Format("{0}|{1}|{2}|{3}",
                        t.IsChecked, t.DeleteRootFolder, t.IsDirectory, t.TargetPath));
                }
                File.WriteAllLines(configPath, lines.ToArray());
            }
            catch { }
        }

        public List<CustomCleanTarget> LoadCustomTargets()
        {
            List<CustomCleanTarget> list = new List<CustomCleanTarget>();
            try
            {
                string configPath = GetCustomTargetsConfigPath();
                if (File.Exists(configPath))
                {
                    string[] lines = File.ReadAllLines(configPath);
                    foreach (var line in lines)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        string[] parts = line.Split('|');
                        if (parts.Length >= 4)
                        {
                            bool isChecked = false;
                            bool.TryParse(parts[0], out isChecked);
                            bool delRoot = false;
                            bool.TryParse(parts[1], out delRoot);
                            bool isDir = false;
                            bool.TryParse(parts[2], out isDir);
                            string path = parts[3];

                            string reason;
                            if (!IsProtectedPath(path, out reason) && (Directory.Exists(path) || File.Exists(path)))
                            {
                                list.Add(new CustomCleanTarget
                                {
                                    IsChecked = isChecked,
                                    DeleteRootFolder = delRoot,
                                    IsDirectory = isDir,
                                    TargetPath = path
                                });
                            }
                        }
                    }
                }
            }
            catch { }

            return list;
        }

        #endregion

        #region Sandbox Roulette (Strictly Confined to Dummy Folder)

        private static string _customSandboxDir = null;

        public static string GetSandboxConfigPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string folder = Path.Combine(appData, "WinCleaner");
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            return Path.Combine(folder, "sandbox_path.txt");
        }

        public static string GetSandboxDirectory()
        {
            if (!string.IsNullOrEmpty(_customSandboxDir) && Directory.Exists(_customSandboxDir))
            {
                return _customSandboxDir;
            }

            try
            {
                string configPath = GetSandboxConfigPath();
                if (File.Exists(configPath))
                {
                    string saved = File.ReadAllText(configPath).Trim();
                    string reason;
                    if (!string.IsNullOrEmpty(saved) && !IsProtectedPath(saved, out reason) && Directory.Exists(saved))
                    {
                        _customSandboxDir = saved;
                        return _customSandboxDir;
                    }
                }
            }
            catch { }

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string dir = Path.Combine(appData, "WinCleaner", "Sandbox");
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            _customSandboxDir = dir;
            return dir;
        }

        public static void SetSandboxDirectory(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            _customSandboxDir = path;
            try
            {
                File.WriteAllText(GetSandboxConfigPath(), path);
            }
            catch { }
        }

        public List<string> GenerateSandboxDummyData(int fileCount = 6, int folderCount = 4)
        {
            string dir = GetSandboxDirectory();
            string[] mockFileNames = new string[]
            {
                "grocery_list.txt", "unreleased_album.txt", "grandmas_cookie_recipe.txt",
                "super_secret_notes.txt", "my_diary_entry.txt", "world_domination_plan.txt",
                "cat_memos.txt", "alien_contact_log.txt", "backup_of_a_backup.txt",
                "random_thoughts.txt", "lost_treasure_map.txt", "homework_dont_delete.txt"
            };

            string[] mockFolderNames = new string[]
            {
                "top_secret_project", "meme_archive_2012", "my_great_novel_drafts",
                "ancient_system_backup", "confidential_photos", "abandoned_game_source",
                "crypto_wallet_keys_fake", "suspicious_experiment_logs"
            };

            List<string> created = new List<string>();
            Random rnd = new Random();

            // 1. Create dummy files
            for (int i = 0; i < fileCount; i++)
            {
                string baseName = mockFileNames[i % mockFileNames.Length];
                string filePath = Path.Combine(dir, string.Format("file_{0:00}_{1}", i + 1, baseName));
                string content = string.Format("--- DUMMY SANDBOX FILE #{0} ---\nCreated: {1}\nPayload Seed: {2}\nSafe to delete in sandbox roulette.",
                    i + 1, DateTime.Now, rnd.Next(10000, 99999));
                File.WriteAllText(filePath, content);
                created.Add(filePath);
            }

            // 2. Create dummy subfolders with nested dummy files
            for (int i = 0; i < folderCount; i++)
            {
                string folderName = string.Format("folder_{0:00}_{1}", i + 1, mockFolderNames[i % mockFolderNames.Length]);
                string subDirPath = Path.Combine(dir, folderName);
                if (!Directory.Exists(subDirPath)) Directory.CreateDirectory(subDirPath);

                // Add 2-3 files inside the folder
                int filesInFolder = rnd.Next(2, 4);
                for (int j = 0; j < filesInFolder; j++)
                {
                    string subFileName = Path.Combine(subDirPath, string.Format("subdoc_{0}.txt", j + 1));
                    File.WriteAllText(subFileName, string.Format("Internal file inside {0}, subdoc #{1}.\nSafe to delete.", folderName, j + 1));
                }

                created.Add(subDirPath);
            }

            Log(string.Format("[SANDBOX] Created {0} dummy files and {1} dummy folders in sandbox.", fileCount, folderCount));
            return created;
        }

        public List<string> GenerateSandboxDummyFiles(int count = 10)
        {
            return GenerateSandboxDummyData(count / 2, count / 2);
        }

        public List<SandboxEntry> GetSandboxEntries()
        {
            string dir = GetSandboxDirectory();
            List<SandboxEntry> list = new List<SandboxEntry>();
            if (!Directory.Exists(dir)) return list;

            // Collect folders
            string[] subDirs = Directory.GetDirectories(dir, "*", SearchOption.TopDirectoryOnly);
            foreach (string sub in subDirs)
            {
                try
                {
                    DirectoryInfo di = new DirectoryInfo(sub);
                    long folderSize = 0;
                    int fileCount = 0;
                    FileInfo[] allFiles = di.GetFiles("*", SearchOption.AllDirectories);
                    foreach (var f in allFiles)
                    {
                        folderSize += f.Length;
                        fileCount++;
                    }

                    list.Add(new SandboxEntry
                    {
                        Name = di.Name,
                        FullPath = di.FullName,
                        IsDirectory = true,
                        Size = folderSize,
                        ItemCount = fileCount,
                        CreatedTime = di.CreationTime
                    });
                }
                catch { }
            }

            // Collect files
            string[] files = Directory.GetFiles(dir, "*.*", SearchOption.TopDirectoryOnly);
            foreach (string file in files)
            {
                try
                {
                    FileInfo fi = new FileInfo(file);
                    list.Add(new SandboxEntry
                    {
                        Name = fi.Name,
                        FullPath = fi.FullName,
                        IsDirectory = false,
                        Size = fi.Length,
                        ItemCount = 1,
                        CreatedTime = fi.CreationTime
                    });
                }
                catch { }
            }

            return list;
        }

        public List<string> GetSandboxFiles()
        {
            List<string> result = new List<string>();
            foreach (var entry in GetSandboxEntries())
            {
                result.Add(entry.FullPath);
            }
            return result;
        }

        public string DeleteRandomSandboxItem(RouletteTargetMode mode, out bool isDirectory, out string deletedName, out long deletedBytes, out int remainingCount)
        {
            isDirectory = false;
            deletedName = null;
            deletedBytes = 0;
            remainingCount = 0;

            string dir = GetSandboxDirectory();
            if (!Directory.Exists(dir)) return null;

            List<SandboxEntry> allEntries = GetSandboxEntries();
            if (allEntries.Count == 0) return null;

            List<SandboxEntry> eligibleEntries;
            if (mode == RouletteTargetMode.FilesOnly)
            {
                eligibleEntries = allEntries.FindAll(e => !e.IsDirectory);
            }
            else if (mode == RouletteTargetMode.FoldersOnly)
            {
                eligibleEntries = allEntries.FindAll(e => e.IsDirectory);
            }
            else
            {
                eligibleEntries = allEntries;
            }

            if (eligibleEntries.Count == 0)
            {
                remainingCount = allEntries.Count;
                return null;
            }

            Random rnd = new Random();
            SandboxEntry victim = eligibleEntries[rnd.Next(eligibleEntries.Count)];
            deletedName = victim.Name;
            isDirectory = victim.IsDirectory;
            deletedBytes = victim.Size;

            try
            {
                if (victim.IsDirectory)
                {
                    Directory.Delete(victim.FullPath, true);
                    remainingCount = allEntries.Count - 1;
                    Log(string.Format("[SANDBOX] 💥 Cleaned folder: '{0}' ({1}, {2} files)! ({3} items remaining)",
                        deletedName, FormatBytes(deletedBytes), victim.ItemCount, remainingCount));
                }
                else
                {
                    File.Delete(victim.FullPath);
                    remainingCount = allEntries.Count - 1;
                    Log(string.Format("[SANDBOX] 💥 Cleaned file: '{0}' ({1})! ({2} items remaining)",
                        deletedName, FormatBytes(deletedBytes), remainingCount));
                }
                return victim.FullPath;
            }
            catch (Exception ex)
            {
                Log(string.Format("[ERROR] Could not delete {0}: {1}", deletedName, ex.Message));
                return null;
            }
        }

        public string DeleteRandomSandboxItem(RouletteTargetMode mode, out bool isDirectory, out string deletedName, out int remainingCount)
        {
            long bytes;
            return DeleteRandomSandboxItem(mode, out isDirectory, out deletedName, out bytes, out remainingCount);
        }

        public string DeleteRandomSandboxItem(out bool isDirectory, out string deletedName, out int remainingCount)
        {
            long bytes;
            return DeleteRandomSandboxItem(RouletteTargetMode.All, out isDirectory, out deletedName, out bytes, out remainingCount);
        }

        public string DeleteRandomSandboxFile(out int remainingCount)
        {
            bool isDir;
            string deletedName;
            long bytes;
            return DeleteRandomSandboxItem(RouletteTargetMode.FilesOnly, out isDir, out deletedName, out bytes, out remainingCount);
        }

        public void ClearAllSandboxFiles()
        {
            string dir = GetSandboxDirectory();
            if (Directory.Exists(dir))
            {
                string[] subDirs = Directory.GetDirectories(dir);
                foreach (var d in subDirs)
                {
                    try { Directory.Delete(d, true); } catch { }
                }

                string[] files = Directory.GetFiles(dir);
                foreach (var f in files)
                {
                    try { File.Delete(f); } catch { }
                }
                Log("[SANDBOX] Emptied all files and folders from sandbox.");
            }
        }

        #endregion

        #region Cleaning Logic (Built-in Categories)

        public CleanSummary Clean(List<CategoryItem> categories)
        {
            CancelRequested = false;
            CleanSummary summary = new CleanSummary();
            int catIndex = 0;

            foreach (var cat in categories)
            {
                if (CancelRequested) break;
                catIndex++;
                if (!cat.IsChecked) continue;

                Log(string.Format(">>> Cleaning category: {0}...", cat.Name));
                ReportProgress(catIndex, categories.Count, "Cleaning " + cat.Name);

                if (cat.Type == CategoryType.RecycleBin)
                {
                    CleanRecycleBin(cat, summary);
                }
                else if (cat.Type == CategoryType.FirefoxCache)
                {
                    CleanFirefoxCache(cat, summary);
                }
                else
                {
                    foreach (var dir in cat.TargetDirectories)
                    {
                        if (CancelRequested) break;
                        if (!Directory.Exists(dir)) continue;

                        CleanDirectoryContents(dir, summary);
                    }
                }

                cat.ScannedBytes = 0;
                cat.ScannedFiles = 0;
            }

            Log(string.Format("Clean operation finished. Deleted: {0:N0} files ({1}). Skipped (locked): {2:N0} files.",
                summary.FilesDeleted, FormatBytes(summary.BytesFreed), summary.FilesSkipped));

            return summary;
        }

        private void CleanDirectoryContents(string rootDir, CleanSummary summary)
        {
            try
            {
                DirectoryInfo dir = new DirectoryInfo(rootDir);
                FileInfo[] files = null;
                try
                {
                    files = dir.GetFiles("*", SearchOption.TopDirectoryOnly);
                }
                catch { }

                if (files != null)
                {
                    foreach (var file in files)
                    {
                        if (CancelRequested) return;
                        DeleteFileSafely(file, summary);
                    }
                }

                DirectoryInfo[] subDirs = null;
                try
                {
                    subDirs = dir.GetDirectories("*", SearchOption.TopDirectoryOnly);
                }
                catch { }

                if (subDirs != null)
                {
                    foreach (var sub in subDirs)
                    {
                        if (CancelRequested) return;
                        CleanDirectoryContents(sub.FullName, summary);

                        try
                        {
                            if (Directory.GetFileSystemEntries(sub.FullName).Length == 0)
                            {
                                sub.Delete();
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        private void DeleteFileSafely(FileInfo file, CleanSummary summary)
        {
            try
            {
                long size = 0;
                try { size = file.Length; } catch { }

                if ((file.Attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                {
                    file.Attributes = FileAttributes.Normal;
                }

                file.Delete();
                summary.FilesDeleted++;
                summary.BytesFreed += size;
                Log(string.Format("[DELETED] {0} ({1})", file.Name, FormatBytes(size)));
            }
            catch (IOException)
            {
                summary.FilesSkipped++;
            }
            catch (UnauthorizedAccessException)
            {
                summary.FilesSkipped++;
            }
            catch (Exception ex)
            {
                summary.Errors++;
                Log(string.Format("[ERROR] Could not delete {0}: {1}", file.Name, ex.Message));
            }
        }

        private void CleanRecycleBin(CategoryItem cat, CleanSummary summary)
        {
            try
            {
                long sizeBefore = cat.ScannedBytes;
                int countBefore = cat.ScannedFiles;

                int hr = SHEmptyRecycleBin(IntPtr.Zero, null,
                    SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);

                if (hr == 0)
                {
                    summary.BytesFreed += sizeBefore;
                    summary.FilesDeleted += countBefore;
                    Log(string.Format("[CLEANED] Recycle Bin emptied successfully ({0}).", FormatBytes(sizeBefore)));
                }
                else
                {
                    Log(string.Format("[WARNING] Empty Recycle Bin returned status code: 0x{0:X8}", hr));
                }
            }
            catch (Exception ex)
            {
                summary.Errors++;
                Log("Error emptying Recycle Bin: " + ex.Message);
            }
        }

        private void CleanFirefoxCache(CategoryItem cat, CleanSummary summary)
        {
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string ffProfiles = Path.Combine(localAppData, @"Mozilla\Firefox\Profiles");
                if (Directory.Exists(ffProfiles))
                {
                    string[] profileDirs = Directory.GetDirectories(ffProfiles);
                    foreach (var pDir in profileDirs)
                    {
                        string cache2 = Path.Combine(pDir, "cache2");
                        if (Directory.Exists(cache2))
                        {
                            CleanDirectoryContents(cache2, summary);
                        }
                        string jumpList = Path.Combine(pDir, "jumpListCache");
                        if (Directory.Exists(jumpList))
                        {
                            CleanDirectoryContents(jumpList, summary);
                        }
                    }
                }
            }
            catch { }
        }

        #endregion

        private void Log(string message)
        {
            var handler = LogMessage;
            if (handler != null)
            {
                handler(string.Format("[{0:HH:mm:ss}] {1}", DateTime.Now, message));
            }
        }

        private void ReportProgress(int current, int total, string description)
        {
            var handler = ProgressChanged;
            if (handler != null)
            {
                handler(current, total, description);
            }
        }
    }
}
