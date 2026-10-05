[CmdletBinding()]
param(
    [ValidateSet('win-x64')]
    [string]$RuntimeIdentifier = 'win-x64'
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = $PSScriptRoot
$projectPath = Join-Path $repositoryRoot 'WorkspaceLauncher\WorkspaceLauncher.csproj'
$artifactsPath = Join-Path $repositoryRoot 'artifacts'
$publishPath = Join-Path $artifactsPath "WorkspaceLauncher-$RuntimeIdentifier"
$installerPath = Join-Path $repositoryRoot 'WorkspaceLauncher.iss'
$compiler = @(
    (Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1),
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
) | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) } | Select-Object -First 1

if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw "Workspace Launcher project was not found: $projectPath"
}
if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
    throw "Inno Setup definition was not found: $installerPath"
}
if (-not $compiler) {
    throw 'Inno Setup 6 is required to build the installer. Install it from https://jrsoftware.org/isdl.php, then rerun this script.'
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

$compilerArguments = @(
    '/Qp',
    "/DPublishDir=$publishPath",
    "/O$artifactsPath",
    "/FWorkspaceLauncher-Setup-$RuntimeIdentifier",
    $installerPath
)
& $compiler @compilerArguments
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE."
}

Write-Output "Published self-contained app: $publishPath"
Write-Output "Created Windows installer: $(Join-Path $artifactsPath "WorkspaceLauncher-Setup-$RuntimeIdentifier.exe")"
