param(
  [Parameter(Mandatory = $true)]
  [ValidateSet("win-x64", "linux-x64", "osx-x64", "osx-arm64")]
  [string]$Runtime
)

$ErrorActionPreference = "Stop"

$rootDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectDir = Join-Path $rootDir "Pandas.PrintAgent.App"
$projectFile = Join-Path $projectDir "Pandas.PrintAgent.App.csproj"
$tempRoot = if ($IsWindows) { "C:\tmp" } else { [System.IO.Path]::GetTempPath() }
$tempOutput = Join-Path $tempRoot "pandas-print-agent-app-$Runtime"
$publishOutput = Join-Path $projectDir "publish\$Runtime"
$existingSettings = Join-Path $publishOutput "appsettings.json"
$settingsBackup = Join-Path ([System.IO.Path]::GetTempPath()) "pandas-print-agent-appsettings-$Runtime.backup.json"

New-Item -ItemType Directory -Force $tempOutput, $publishOutput | Out-Null

$windowsExe = Join-Path $publishOutput "Pandas.PrintAgent.App.exe"
if ($Runtime -eq "win-x64" -and (Test-Path $windowsExe)) {
  try {
    $lockCheck = [System.IO.File]::Open($windowsExe, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
    $lockCheck.Close()
  } catch {
    throw "No se puede publicar porque Pandas.PrintAgent.App.exe esta abierto. Cierra la app de bandeja y vuelve a ejecutar el publish."
  }
}

if (Test-Path $existingSettings) {
  Copy-Item -Path $existingSettings -Destination $settingsBackup -Force
}

dotnet publish $projectFile `
  --configuration Release `
  --runtime $Runtime `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  --output $tempOutput

Copy-Item -Path (Join-Path $tempOutput "*") -Destination $publishOutput -Recurse -Force

if (Test-Path $settingsBackup) {
  Copy-Item -Path $settingsBackup -Destination $existingSettings -Force
  Remove-Item -Path $settingsBackup -Force
}

Write-Host ""
Write-Host "GUI publicada en: $(Resolve-Path $publishOutput)"
Write-Host "El token se configura desde la app y se guarda en el almacen seguro del sistema."
