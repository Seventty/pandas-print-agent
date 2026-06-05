$ErrorActionPreference = "Stop"

$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$tempOutput = "C:\tmp\pandas-print-agent-win-x64"
$publishOutput = Join-Path $projectDir "publish\win-x64"

New-Item -ItemType Directory -Force $tempOutput, $publishOutput | Out-Null
$existingSettings = Join-Path $publishOutput "appsettings.json"
$settingsBackup = Join-Path $env:TEMP "pandas-print-agent-appsettings.backup.json"
$existingExe = Join-Path $publishOutput "Pandas.PrintAgent.exe"

if (Test-Path $existingExe) {
  try {
    $lockCheck = [System.IO.File]::Open($existingExe, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
    $lockCheck.Close()
  } catch {
    throw "No se puede publicar porque Pandas.PrintAgent.exe esta abierto. Cierra la ventana del agente y vuelve a ejecutar publish-win-x64.ps1."
  }
}

if (Test-Path $existingSettings) {
  Copy-Item -Path $existingSettings -Destination $settingsBackup -Force
}

dotnet publish .\Pandas.PrintAgent.csproj `
  --configuration Release `
  --runtime win-x64 `
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
Write-Host "Agente publicado en: $(Resolve-Path $publishOutput)"
Write-Host "Edita appsettings.json y ejecuta 'Start PANDAS Print Agent.bat' o Pandas.PrintAgent.exe."
