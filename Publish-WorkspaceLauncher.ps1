[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$RuntimeIdentifier = 'win-x64'
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = $PSScriptRoot
$projectPath = Join-Path $repositoryRoot 'WorkspaceLauncher\WorkspaceLauncher.csproj'
$artifactsPath = Join-Path $repositoryRoot 'artifacts'
$publishPath = Join-Path $artifactsPath "WorkspaceLauncher-$RuntimeIdentifier"
$archivePath = Join-Path $artifactsPath "WorkspaceLauncher-$RuntimeIdentifier.zip"

if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw "Workspace Launcher project was not found: $projectPath"
}

New-Item -Path $artifactsPath -ItemType Directory -Force | Out-Null
if (Test-Path -LiteralPath $publishPath) {
    Remove-Item -LiteralPath $publishPath -Recurse -Force
}

dotnet publish $projectPath `
    --configuration Release `
    --runtime $RuntimeIdentifier `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    --output $publishPath
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$executablePath = Join-Path $publishPath 'WorkspaceLauncher.exe'
$helperPath = Join-Path $publishPath 'workspace_desktop.ps1'
if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $helperPath -PathType Leaf)) {
    throw "The publish output is incomplete: expected WorkspaceLauncher.exe and workspace_desktop.ps1 in $publishPath"
}

Copy-Item -LiteralPath (Join-Path $repositoryRoot 'Install-WorkspaceLauncher.ps1') -Destination $publishPath

if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}
Compress-Archive -Path (Join-Path $publishPath '*') -DestinationPath $archivePath -CompressionLevel Optimal

Write-Output "Published self-contained app: $publishPath"
Write-Output "Created release archive: $archivePath"
