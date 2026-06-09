param(
  [Parameter(Mandatory = $true)]
  [ValidateSet("win-x64", "linux-x64", "osx-x64", "osx-arm64")]
  [string]$Runtime,

  [switch]$Velopack,
  [switch]$Msi,
  [switch]$NoDelta,
  [Alias("Version")]
  [string]$PackageVersion,
  [string]$Channel
)

$ErrorActionPreference = "Stop"

function Get-Setting {
  param(
    [string]$Value,
    [string]$EnvName,
    [string]$DefaultValue
  )

  if (-not [string]::IsNullOrWhiteSpace($Value)) {
    return $Value
  }

  $envValue = [Environment]::GetEnvironmentVariable($EnvName)
  if (-not [string]::IsNullOrWhiteSpace($envValue)) {
    return $envValue
  }

  return $DefaultValue
}

function Normalize-PackageVersion {
  param([string]$Version)

  if ([string]::IsNullOrWhiteSpace($Version)) {
    return ""
  }

  return ($Version.Trim() -replace "^[vV]", "")
}

function Assert-VelopackOutputVersion {
  param(
    [string]$ReleaseOutput,
    [string]$PackageId,
    [string]$PackageVersion,
    [string]$Channel
  )

  $releaseManifest = Join-Path $ReleaseOutput "releases.$Channel.json"
  $assetsManifest = Join-Path $ReleaseOutput "assets.$Channel.json"
  $expectedFullPackage = "$PackageId-$PackageVersion-$Channel-full.nupkg"

  if (-not (Test-Path $releaseManifest)) {
    throw "No se encontro $releaseManifest. Velopack no genero el manifiesto de releases esperado."
  }

  if (-not (Test-Path $assetsManifest)) {
    throw "No se encontro $assetsManifest. Velopack no genero el manifiesto de assets esperado."
  }

  if (-not (Test-Path (Join-Path $ReleaseOutput $expectedFullPackage))) {
    throw "No se encontro $expectedFullPackage en $ReleaseOutput. No subas este release: el paquete generado no corresponde a la version $PackageVersion."
  }

  $releaseJson = Get-Content $releaseManifest -Raw | ConvertFrom-Json
  $releaseAssets = @($releaseJson.Assets)
  $expectedReleaseAsset = $releaseAssets | Where-Object {
    $_.PackageId -eq $PackageId -and
    $_.Version -eq $PackageVersion -and
    $_.Type -eq "Full" -and
    $_.FileName -eq $expectedFullPackage
  } | Select-Object -First 1

  if ($null -eq $expectedReleaseAsset) {
    throw "El manifiesto releases.$Channel.json no apunta al paquete full $expectedFullPackage. No subas este release."
  }

  $assetsJson = @(Get-Content $assetsManifest -Raw | ConvertFrom-Json)
  $expectedPublishedAsset = $assetsJson | Where-Object {
    $_.Type -eq "Full" -and $_.RelativeFileName -eq $expectedFullPackage
  } | Select-Object -First 1

  if ($null -eq $expectedPublishedAsset) {
    throw "El manifiesto assets.$Channel.json no publicaria $expectedFullPackage. No subas este release."
  }
}

function Get-VpkCommandPath {
  $command = Get-Command vpk -ErrorAction SilentlyContinue
  if ($null -ne $command) {
    return $command.Source
  }

  $isWindowsHost = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)
  $toolName = if ($isWindowsHost) { "vpk.exe" } else { "vpk" }
  $globalToolPath = Join-Path (Join-Path $HOME ".dotnet") (Join-Path "tools" $toolName)
  if (Test-Path $globalToolPath) {
    return $globalToolPath
  }

  throw "No se encontro la CLI de Velopack. Instala con: dotnet tool install -g vpk"
}

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
  -p:DebugType=None `
  -p:DebugSymbols=false `
  --output $tempOutput

Copy-Item -Path (Join-Path $tempOutput "*") -Destination $publishOutput -Recurse -Force
Get-ChildItem -Path $publishOutput -Recurse -Filter "*.pdb" -File -ErrorAction SilentlyContinue | Remove-Item -Force

if (Test-Path $settingsBackup) {
  Copy-Item -Path $settingsBackup -Destination $existingSettings -Force
  Remove-Item -Path $settingsBackup -Force
}

$releaseChannel = Get-Setting $Channel "PANDAS_UPDATE_CHANNEL" "stable"

if ($Velopack) {
  $vpk = Get-VpkCommandPath

  if ($Msi -and $Runtime -ne "win-x64") {
    throw "El flag -Msi solo aplica para win-x64."
  }

  $packVersion = Normalize-PackageVersion (Get-Setting $PackageVersion "PANDAS_VELOPACK_PACK_VERSION" "")
  if ([string]::IsNullOrWhiteSpace($packVersion)) {
    throw "Define -Version o PANDAS_VELOPACK_PACK_VERSION antes de empaquetar con Velopack."
  }

  $packId = Get-Setting "" "PANDAS_VELOPACK_PACK_ID" "com.pandas.printagent"
  $packTitle = Get-Setting "" "PANDAS_VELOPACK_PACK_TITLE" "PANDAS Print Agent"
  $packChannel = $releaseChannel
  $outputRoot = Get-Setting "" "PANDAS_VELOPACK_OUTPUT_DIR" (Join-Path $rootDir "releases")
  if (-not [System.IO.Path]::IsPathRooted($outputRoot)) {
    $outputRoot = Join-Path $rootDir $outputRoot
  }
  $releaseOutput = Join-Path $outputRoot $Runtime
  $mainExe = if ($Runtime -eq "win-x64") { "Pandas.PrintAgent.App.exe" } else { "Pandas.PrintAgent.App" }

  New-Item -ItemType Directory -Force $releaseOutput | Out-Null

  $packArgs = @(
    "pack",
    "--packId", $packId,
    "--packVersion", $packVersion,
    "--packDir", $publishOutput,
    "--mainExe", $mainExe,
    "--packTitle", $packTitle,
    "--outputDir", $releaseOutput
  )

  if (-not [string]::IsNullOrWhiteSpace($packChannel)) {
    $packArgs += @("--channel", $packChannel)
  }

  if ($Msi) {
    $packArgs += "--msi"
  }

  if ($NoDelta) {
    $packArgs += @("--delta", "None")
  }

  & $vpk @packArgs
  if ($LASTEXITCODE -ne 0) {
    throw "Velopack fallo con codigo $LASTEXITCODE."
  }

  Assert-VelopackOutputVersion $releaseOutput $packId $packVersion $packChannel

  Write-Host "Velopack generado en: $(Resolve-Path $releaseOutput)"
  Write-Host "Version validada: $packId $packVersion ($packChannel)"
}

Write-Host ""
Write-Host "GUI publicada en: $(Resolve-Path $publishOutput)"
Write-Host "El token del backend se configura desde la app y se guarda en el almacen seguro del sistema."
