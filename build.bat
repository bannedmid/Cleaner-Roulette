@echo off
setlocal

echo ========================================================
echo  Building WinCleaner (Windows 7 - 11 Compatible)
echo ========================================================

set CSC_PATH=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC_PATH%" (
    set CSC_PATH=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe
)

if not exist "%CSC_PATH%" (
    echo ERROR: Could not find .NET Framework csc.exe compiler!
    echo Please make sure .NET Framework 4.0 or higher is installed.
    exit /b 1
)

echo Found C# compiler at: %CSC_PATH%
echo Compiling source code...

"%CSC_PATH%" /target:winexe /platform:anycpu /optimize+ /win32manifest:app.manifest /out:WinCleaner.exe CleanerEngine.cs MainForm.cs Program.cs /r:System.dll,System.Core.dll,System.Drawing.dll,System.Windows.Forms.dll

if %ERRORLEVEL% equ 0 (
    echo ========================================================
    echo  BUILD SUCCESSFUL: WinCleaner.exe created!
    echo ========================================================
    exit /b 0
) else (
    echo ========================================================
    echo  BUILD FAILED! Check error messages above.
    echo ========================================================
    exit /b %ERRORLEVEL%
)
