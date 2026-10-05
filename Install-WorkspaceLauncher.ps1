[CmdletBinding()]
param(
    [string]$SourcePath = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'

$sourceDirectory = (Resolve-Path -LiteralPath $SourcePath).Path
$executablePath = Join-Path $sourceDirectory 'WorkspaceLauncher.exe'
$helperPath = Join-Path $sourceDirectory 'workspace_desktop.ps1'
if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $helperPath -PathType Leaf)) {
    throw "The source folder is not a complete Workspace Launcher release: $sourceDirectory"
}

$runningApps = @(Get-Process -Name 'WorkspaceLauncher' -ErrorAction SilentlyContinue)
if ($runningApps.Count -gt 0) {
    throw 'Close Workspace Launcher before installing or updating it.'
}

$installDirectory = Join-Path $env:LOCALAPPDATA 'Programs\WorkspaceLauncher'
$programsDirectory = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
$shortcutPath = Join-Path $programsDirectory 'Workspace Launcher.lnk'
New-Item -Path $installDirectory -ItemType Directory -Force | Out-Null
New-Item -Path $programsDirectory -ItemType Directory -Force | Out-Null

Get-ChildItem -LiteralPath $sourceDirectory -Force |
    Where-Object { $_.Name -ne 'Install-WorkspaceLauncher.ps1' } |
    ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $installDirectory -Recurse -Force
    }

$installedExecutable = Join-Path $installDirectory 'WorkspaceLauncher.exe'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $installedExecutable
$shortcut.WorkingDirectory = $installDirectory
$shortcut.IconLocation = "$installedExecutable,0"
$shortcut.Description = 'Configure and launch Windows workspaces'
$shortcut.Save()

Write-Output "Installed Workspace Launcher to $installDirectory"
Write-Output "Created Start Menu shortcut: $shortcutPath"
Write-Output 'Your saved workspace settings remain in %LOCALAPPDATA%\WorkspaceLauncher.'
