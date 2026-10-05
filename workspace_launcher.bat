@echo off
title Multi-Workspace Launcher
cls
echo Launcher trace: desktop-routing-v1 - %~f0

:menu
echo ====================================================
echo             SELECT A WORKSPACE TO LAUNCH
echo ====================================================
echo  [1] Admin (Outlook, MS Teams, ManicTime)
echo  [2] Work (GitHub Desktop, VS Code, Anylogic, GitHub Website)
echo  [3] Entertainment (Youtube Music)
echo  [4] All (Admin, Work, Entertainment)
echo  [5] Exit
echo ====================================================
set /p choice="Enter workspace number (1-5): "

if "%choice%"=="1" goto admin
if "%choice%"=="2" goto work
if "%choice%"=="3" goto ent
if "%choice%"=="4" goto all
if "%choice%"=="5" goto exit
echo.
echo [!] Invalid selection. Please try again.
timeout /t 2 >nul
cls
goto menu

:: ====================================================
:: WORKSPACE 1: ADMIN
:: ====================================================
:admin
cls
echo ====================================================
echo Loading [Workspace 1: Admin]...
echo ====================================================
	call :switchDesktop 0
	if errorlevel 1 goto desktopError
echo Launcher trace: entering Admin selection
call :launchAdminApps
goto success

:: ====================================================
:: WORKSPACE 2: WORK
:: ====================================================
:work
cls
echo ====================================================
echo Loading [Workspace 2: Work]...
echo ====================================================
	call :switchDesktop 1
	if errorlevel 1 goto desktopError
call :launchWorkApps
goto success

:: ====================================================
:: WORKSPACE 3: ENTERTAINMENT
:: ====================================================
:ent
cls
echo ====================================================
echo Loading [Workspace 3: Entertainment]...
echo ====================================================
	call :switchDesktop 2
	if errorlevel 1 goto desktopError
call :launchEntertainmentApps
goto success

:: ====================================================
:: WORKSPACE 4: ALL WORKSPACES
:: ====================================================
:all
cls
echo ====================================================
echo Loading all workspaces...
echo ====================================================
	call :switchDesktop 0
	if errorlevel 1 goto desktopError
call :launchAdminApps
	call :switchDesktop 1
	if errorlevel 1 goto desktopError
call :launchWorkApps
	call :switchDesktop 2
	if errorlevel 1 goto desktopError
call :launchEntertainmentApps

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0workspace_desktop.ps1" -DesktopIndex 1
if errorlevel 1 goto desktopError
goto success

:: ====================================================
:: SUCCESS & EXIT ROUTINES
:: ====================================================
:desktopError
	echo.
	echo [!] A desktop switch or app startup failed. Check desktop order and app paths.
	pause
	exit /b 1

:success
echo.
echo ====================================================
echo [+] Workspace loaded successfully!
echo ====================================================
pause
exit

:exit
exit

:switchDesktop
	powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0workspace_desktop.ps1" -DesktopIndex %~1
	exit /b %errorlevel%

:launchIfNotRunning
set "appName=%~2"
set "appCommand=%~3"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0workspace_desktop.ps1" -ProcessName "%~n1" -WindowTitle "%~6"
if errorlevel 1 (
	echo Launching %appName%...
	if /I "%~4"=="quiet" (
		start "" "%appCommand%" >nul 2>&1
	) else (
		start "" "%appCommand%"
	)
	powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0workspace_desktop.ps1" -ProcessName "%~n1" -WindowTitle "%~6" -WaitForWindow -TimeoutSeconds 60
	if errorlevel 1 goto desktopError
) else (
	echo %appName% is already running. Skipping.
)
exit /b

:launchAdminApps
echo Launcher trace: entered launchAdminApps
call :launchIfNotRunning "olk.exe" "Outlook" "olk.exe"
timeout /t 2 /nobreak >nul
call :launchIfNotRunning "ms-teams.exe" "Microsoft Teams" "ms-teams.exe"
timeout /t 2 /nobreak >nul
call :launchIfNotRunning "ManicTimeClient.exe" "ManicTime" "C:\Program Files\ManicTime\ManicTime.exe" "" "" "ManicTime"
exit /b

:launchWorkApps
echo Launcher trace: entered launchWorkApps
echo Opening GitHub...
start "" "https://github.com/orgs/ConsultLink/repositories"
if exist "%LOCALAPPDATA%\GitHubDesktop\GitHubDesktop.exe" (
	call :launchIfNotRunning "GitHubDesktop.exe" "GitHub Desktop" "%LOCALAPPDATA%\GitHubDesktop\GitHubDesktop.exe"
) else (
	call :launchIfNotRunning "GitHubDesktop.exe" "GitHub Desktop" "GitHubDesktop.exe"
)
timeout /t 2 /nobreak >nul
if exist "%LOCALAPPDATA%\Programs\Microsoft VS Code\Code.exe" (
	call :launchIfNotRunning "Code.exe" "Visual Studio Code" "%LOCALAPPDATA%\Programs\Microsoft VS Code\Code.exe" quiet
) else (
	call :launchIfNotRunning "Code.exe" "Visual Studio Code" "code" quiet
)
timeout /t 2 /nobreak >nul
call :launchIfNotRunning "AnyLogic.exe" "AnyLogic" "C:\Program Files\AnyLogic 8.9 Professional\AnyLogic.exe"
timeout /t 2 /nobreak >nul
exit /b

:launchEntertainmentApps
echo Opening YouTube Music...
start "" "C:\Users\ChristopherL\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\NovusTheory\YouTube Music Desktop App.lnk"
exit /b
