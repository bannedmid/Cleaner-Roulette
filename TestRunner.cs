using System;
using System.IO;
using System.Collections.Generic;

namespace WinCleaner
{
    class TestRunner
    {
        static int Main(string[] args)
        {
            Console.WriteLine("=== Running WinCleaner Engine Test Suite ===");
            int failed = 0;

            // Test 1: Byte formatting
            Console.Write("Test 1: FormatBytes... ");
            if (CleanerEngine.FormatBytes(0) == "0 B" &&
                CleanerEngine.FormatBytes(1024) == "1 KB" &&
                CleanerEngine.FormatBytes(1048576) == "1 MB" &&
                CleanerEngine.FormatBytes(1073741824) == "1 GB")
            {
                Console.WriteLine("PASSED");
            }
            else
            {
                Console.WriteLine("FAILED: " + CleanerEngine.FormatBytes(1048576));
                failed++;
            }

            // Test 2: Category Discovery
            Console.Write("Test 2: Category Discovery... ");
            CleanerEngine engine = new CleanerEngine();
            List<CategoryItem> categories = engine.GetDefaultCategories();
            if (categories != null && categories.Count >= 10)
            {
                Console.WriteLine(string.Format("PASSED ({0} categories initialized)", categories.Count));
            }
            else
            {
                Console.WriteLine("FAILED");
                failed++;
            }

            // Test 3: Safe Clean & Locked File Handling
            Console.Write("Test 3: Safe Clean & Locked File Skipping... ");
            string testDir = Path.Combine(Path.GetTempPath(), "WinCleaner_Test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);
            string normalFile = Path.Combine(testDir, "test_delete_me.tmp");
            string lockedFile = Path.Combine(testDir, "test_locked.tmp");

            File.WriteAllText(normalFile, "This file should be deleted.");
            File.WriteAllText(lockedFile, "This file is locked and should be skipped.");

            FileStream lockStream = null;
            try
            {
                lockStream = new FileStream(lockedFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

                CategoryItem testCat = new CategoryItem
                {
                    Type = CategoryType.UserTemp,
                    Name = "Test Category",
                    IsChecked = true
                };
                testCat.TargetDirectories.Add(testDir);

                engine.Scan(new List<CategoryItem> { testCat });
                CleanSummary cleanSum = engine.Clean(new List<CategoryItem> { testCat });

                bool normalDeleted = !File.Exists(normalFile);
                bool lockedStillExists = File.Exists(lockedFile);

                if (normalDeleted && lockedStillExists && cleanSum.FilesDeleted >= 1 && cleanSum.FilesSkipped >= 1)
                {
                    Console.WriteLine("PASSED (Unlocked deleted, locked skipped safely)");
                }
                else
                {
                    Console.WriteLine("FAILED");
                    failed++;
                }
            }
            finally
            {
                if (lockStream != null)
                {
                    lockStream.Close();
                    lockStream.Dispose();
                }
                try { Directory.Delete(testDir, true); } catch { }
            }

            // Test 4: Protected Path Guard
            Console.Write("Test 4: Protected Path Guard... ");
            string reason;
            bool rootBlocked = CleanerEngine.IsProtectedPath(@"C:\", out reason);
            bool winBlocked = CleanerEngine.IsProtectedPath(Environment.GetFolderPath(Environment.SpecialFolder.Windows), out reason);
            bool progBlocked = CleanerEngine.IsProtectedPath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), out reason);
            bool userProfileBlocked = CleanerEngine.IsProtectedPath(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), out reason);
            bool desktopBlocked = CleanerEngine.IsProtectedPath(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), out reason);
            bool safeFolderAllowed = !CleanerEngine.IsProtectedPath(Path.Combine(Path.GetTempPath(), "MyFolder"), out reason);

            if (rootBlocked && winBlocked && progBlocked && userProfileBlocked && desktopBlocked && safeFolderAllowed)
            {
                Console.WriteLine("PASSED (System roots blocked, user subdirectories allowed)");
            }
            else
            {
                Console.WriteLine("FAILED");
                failed++;
            }

            // Test 5: Custom User Folder Deletion Modes
            Console.Write("Test 5: Custom User Folder Deletion Modes... ");
            string customTestDir1 = Path.Combine(Path.GetTempPath(), "WinCleaner_Custom1_" + Guid.NewGuid().ToString("N"));
            string customTestDir2 = Path.Combine(Path.GetTempPath(), "WinCleaner_Custom2_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(customTestDir1);
            Directory.CreateDirectory(customTestDir2);

            File.WriteAllText(Path.Combine(customTestDir1, "doc1.txt"), "hello");
            File.WriteAllText(Path.Combine(customTestDir2, "doc2.txt"), "world");

            var targetEmpty = new CustomCleanTarget(customTestDir1, true, false);
            var targetDeleteEntire = new CustomCleanTarget(customTestDir2, true, true);

            var customTargets = new List<CustomCleanTarget> { targetEmpty, targetDeleteEntire };
            engine.ScanCustomTargets(customTargets);
            engine.CleanCustomTargets(customTargets);

            bool dir1StillExists = Directory.Exists(customTestDir1);
            bool dir1FilesDeleted = Directory.GetFiles(customTestDir1).Length == 0;
            bool dir2Deleted = !Directory.Exists(customTestDir2);

            if (dir1StillExists && dir1FilesDeleted && dir2Deleted)
            {
                Console.WriteLine("PASSED");
            }
            else
            {
                Console.WriteLine("FAILED");
                failed++;
            }
            try { Directory.Delete(customTestDir1, true); } catch { }

            // Test 6: Custom Targets Persistence
            Console.Write("Test 6: Custom Targets Persistence... ");
            string dummyPath = Path.Combine(Path.GetTempPath(), "WinCleaner_DummyPersist");
            Directory.CreateDirectory(dummyPath);
            engine.SaveCustomTargets(new List<CustomCleanTarget> { new CustomCleanTarget(dummyPath, true, false) });
            List<CustomCleanTarget> loaded = engine.LoadCustomTargets();
            bool foundDummy = loaded.Exists(t => string.Equals(t.TargetPath, dummyPath, StringComparison.OrdinalIgnoreCase));

            if (foundDummy)
            {
                Console.WriteLine("PASSED");
            }
            else
            {
                Console.WriteLine("FAILED");
                failed++;
            }
            try { Directory.Delete(dummyPath, true); } catch { }

            // Setup isolated sandbox directory for tests
            string originalSandbox = CleanerEngine.GetSandboxDirectory();
            string testSandbox = Path.Combine(Path.GetTempPath(), "WinCleaner_TestSandbox_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testSandbox);
            CleanerEngine.SetSandboxDirectory(testSandbox);

            try
            {
                // Test 7: Sandbox Roulette File & Folder Generation & Random Deletion
                Console.Write("Test 7: Sandbox Roulette (Files & Folders)... ");
                engine.ClearAllSandboxFiles();
                engine.GenerateSandboxDummyData(4, 2); // 4 files, 2 folders
                List<SandboxEntry> beforeEntries = engine.GetSandboxEntries();

                bool hasDir = beforeEntries.Exists(e => e.IsDirectory);
                bool hasFile = beforeEntries.Exists(e => !e.IsDirectory);

                bool isDir;
                string deletedName;
                int remaining;
                string victimPath = engine.DeleteRandomSandboxItem(out isDir, out deletedName, out remaining);
                List<SandboxEntry> afterEntries = engine.GetSandboxEntries();

                bool victimExists = isDir ? Directory.Exists(victimPath) : File.Exists(victimPath);

                if (beforeEntries.Count == 6 && hasDir && hasFile && !string.IsNullOrEmpty(victimPath) &&
                    remaining == 5 && afterEntries.Count == 5 && !victimExists)
                {
                    Console.WriteLine(string.Format("PASSED (Found files & folders; obliterated {0} '{1}', 5 remain)",
                        isDir ? "FOLDER" : "FILE", deletedName));
                }
                else
                {
                    Console.WriteLine("FAILED: Sandbox random item deletion failed.");
                    failed++;
                }
                engine.ClearAllSandboxFiles();

                // Test 8: Sandbox Roulette Target Modes (FilesOnly & FoldersOnly)
                Console.Write("Test 8: Sandbox Target Modes (FilesOnly vs FoldersOnly)... ");
                engine.GenerateSandboxDummyData(3, 2); // 3 files, 2 folders
                
                bool test8FilesOnlyIsDir;
                string test8FileDeleted;
                int test8Remaining1;
                string test8FilePath = engine.DeleteRandomSandboxItem(RouletteTargetMode.FilesOnly, out test8FilesOnlyIsDir, out test8FileDeleted, out test8Remaining1);

                bool test8FoldersOnlyIsDir;
                string test8FolderDeleted;
                int test8Remaining2;
                string test8FolderPath = engine.DeleteRandomSandboxItem(RouletteTargetMode.FoldersOnly, out test8FoldersOnlyIsDir, out test8FolderDeleted, out test8Remaining2);

                if (!test8FilesOnlyIsDir && !File.Exists(test8FilePath) &&
                    test8FoldersOnlyIsDir && !Directory.Exists(test8FolderPath) &&
                    test8Remaining2 == 3)
                {
                    Console.WriteLine("PASSED (FilesOnly strictly picked file, FoldersOnly strictly picked folder)");
                }
                else
                {
                    Console.WriteLine("FAILED: Target mode filtering failed.");
                    failed++;
                }
                engine.ClearAllSandboxFiles();
            }
            finally
            {
                try { Directory.Delete(testSandbox, true); } catch { }
                CleanerEngine.SetSandboxDirectory(originalSandbox);
            }

            Console.WriteLine();
            if (failed == 0)
            {
                Console.WriteLine("=== ALL TESTS PASSED SUCCESSFULLY! ===");
                return 0;
            }
            else
            {
                Console.WriteLine(string.Format("=== {0} TEST(S) FAILED! ===", failed));
                return 1;
            }
        }
    }
}
