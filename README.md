# WinCleaner - Windows Disk Space Cleaner

A fast, lightweight, zero-dependency disk cleanup tool for Windows.
Engineered to work on **Windows 7 (SP1), Windows 8, 8.1, Windows 10, and Windows 11**.

---

## Features

- **100% Native & Portable**: Single self-contained `.exe` (~48 KB), requires no external installers, Python runtimes, or Electron overhead.
- **Full Windows 7 Compatibility**: Built against .NET Framework 4.0/4.5 using standard Windows APIs (`csc.exe`).
- **Tabbed Interface**:
  - **Tab 1: 🧹 Junk & Cache Cleaner**
    - User Temporary Files (`%TEMP%`, Local Temp)
    - Windows System Temp Files (`C:\Windows\Temp`)
    - Windows Recycle Bin (via native `SHEmptyRecycleBin`)
    - Windows Update Download Cache (`SoftwareDistribution\Download`)
    - Crash Dumps & Windows Error Reports (`CrashDumps`, WER)
    - Windows Explorer Thumbnail Cache
    - Windows Log Files (`CBS`, `DISM`, `Panther`)
    - Browser Caches (Google Chrome, Microsoft Edge, Mozilla Firefox, Internet Explorer / Legacy Cache)
  - **Tab 2: 📁 User Files & Custom Folders**
    - **Add Folder**: Select any user directory to clean.
    - **Add Files**: Pick specific user files to delete.
    - **Add Downloads**: Quick-button to add `%USERPROFILE%\Downloads`.
    - **Folder Modes**: Choose between *Empty Contents Only* (keeps folder) or *Delete Entire Folder*.
    - **Persistent Targets**: Remembers your custom folder list across app restarts.
- **Safety First**:
  - **Protected Path Guard**: Refuses to target critical directories (`C:\`, `C:\Windows`, `C:\Program Files`, root user profile).
  - **Locked File Protection**: Automatically and safely skips files currently in use by other running processes.
  - **Scan / Analyze Mode**: Calculates recoverable disk space without deleting anything.
  - **Explicit Confirmation Dialog**: Displays warning before wiping personal files.
  - **UAC Elevation**: One-click "Run as Admin" button when cleaning system-level temp files.
- **Real-Time Activity Log**: Live status updates, progress bar, with Copy & Clear functions.

---

## How to Run

Simply double-click:
```
WinCleaner.exe
```

Or from PowerShell / Command Prompt:
```cmd
WinCleaner.exe
```

---

## How to Build from Source

Double-click `build.bat` or run:
```cmd
build.bat
```
It compiles using the native C# compiler (`csc.exe`) built into Windows. No Visual Studio installation is needed.

---

## Running Tests

To run the automated engine test suite:
```powershell
csc.exe /target:exe /out:TestRunner.exe CleanerEngine.cs TestRunner.cs /r:System.dll,System.Core.dll
.\TestRunner.exe
```
