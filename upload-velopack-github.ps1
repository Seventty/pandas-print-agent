param(
  [Parameter(Mandatory = $true)]
  [Alias("PackageVersion")]
  [string]$Version,

  [ValidateSet("win-x64", "linux-x64", "osx-x64", "osx-arm64")]
  [string]$Runtime = "win-x64",

  [string]$Channel,
  [string]$RepoUrl = "https://github.com/Seventty/pandas-print-agent",
  [string]$Token,
  [string]$OutputDir,
  [switch]$DryRun,
  [switch]$NoMerge,
  [switch]$Publish
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
    [string]$PackageVersion,
    [string]$Channel
  )

  $releaseManifest = Join-Path $ReleaseOutput "releases.$Channel.json"
  $assetsManifest = Join-Path $ReleaseOutput "assets.$Channel.json"

  if (-not (Test-Path $releaseManifest)) {
    throw "No se encontro $releaseManifest. Ejecuta publish con -Velopack -Version $PackageVersion antes de subir."
  }

  if (-not (Test-Path $assetsManifest)) {
    throw "No se encontro $assetsManifest. Ejecuta publish con -Velopack -Version $PackageVersion antes de subir."
  }

  $releaseJson = Get-Content $releaseManifest -Raw | ConvertFrom-Json
  $releaseAssets = @($releaseJson.Assets)
  $fullAsset = $releaseAssets | Where-Object {
    $_.Version -eq $PackageVersion -and $_.Type -eq "Full"
  } | Select-Object -First 1

  if ($null -eq $fullAsset) {
    throw "El manifiesto local no contiene un paquete full version $PackageVersion. No se subira un tag que no coincida con los assets."
  }

  if (-not (Test-Path (Join-Path $ReleaseOutput $fullAsset.FileName))) {
    throw "El paquete $($fullAsset.FileName) aparece en el manifiesto, pero no existe en $ReleaseOutput."
  }

  $assetsJson = @(Get-Content $assetsManifest -Raw | ConvertFrom-Json)
  $publishedFull = $assetsJson | Where-Object {
    $_.Type -eq "Full" -and $_.RelativeFileName -eq $fullAsset.FileName
  } | Select-Object -First 1

  if ($null -eq $publishedFull) {
    throw "assets.$Channel.json no publicaria $($fullAsset.FileName). Regenera el paquete antes de subir."
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
$packageVersion = Normalize-PackageVersion $Version
$releaseChannel = Get-Setting $Channel "PANDAS_UPDATE_CHANNEL" "stable"
$outputRoot = Get-Setting $OutputDir "PANDAS_VELOPACK_OUTPUT_DIR" (Join-Path $rootDir "releases")

if ([string]::IsNullOrWhiteSpace($packageVersion)) {
  throw "Define -Version con la version del paquete Velopack."
}

if (-not [System.IO.Path]::IsPathRooted($outputRoot)) {
  $outputRoot = Join-Path $rootDir $outputRoot
}

$releaseOutput = Join-Path $outputRoot $Runtime
if (-not (Test-Path $releaseOutput)) {
  throw "No existe $releaseOutput. Ejecuta publish con -Velopack antes de subir."
}

Assert-VelopackOutputVersion $releaseOutput $packageVersion $releaseChannel

if ($DryRun) {
  Write-Host "Validacion OK: $packageVersion ($releaseChannel) en $(Resolve-Path $releaseOutput)"
  return
}

$releaseToken = Get-Setting $Token "VPK_GITHUB_TOKEN" ""
if ([string]::IsNullOrWhiteSpace($releaseToken)) {
  throw "Define -Token o VPK_GITHUB_TOKEN para publicar en GitHub Releases."
}

$vpk = Get-VpkCommandPath

$tag = "v$packageVersion"
$uploadArgs = @(
  "upload",
  "github",
  "--outputDir", $releaseOutput,
  "--channel", $releaseChannel,
  "--repoUrl", $RepoUrl,
  "--token", $releaseToken,
  "--tag", $tag
)

if ($Publish) {
  $uploadArgs += @("--publish", "true")
}

if (-not $NoMerge) {
  $uploadArgs += @("--merge", "true")
}

Write-Host "Subiendo Velopack $packageVersion ($releaseChannel) a $RepoUrl como $tag..."
& $vpk @uploadArgs
if ($LASTEXITCODE -ne 0) {
  throw "Velopack upload fallo con codigo $LASTEXITCODE."
}

Write-Host "Release publicado: $tag"
