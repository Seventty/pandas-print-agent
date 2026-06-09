param(
  [switch]$Velopack,
  [switch]$NoDelta,
  [Alias("PackageVersion")]
  [string]$Version,
  [string]$Channel
)

$ErrorActionPreference = "Stop"

$publishScript = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "publish-app.ps1"
$publishArgs = @{
  Runtime = "linux-x64"
}

if ($Velopack) {
  $publishArgs.Velopack = $true
}

if ($NoDelta) {
  $publishArgs.NoDelta = $true
}

if (-not [string]::IsNullOrWhiteSpace($Version)) {
  $publishArgs.PackageVersion = $Version
}

if (-not [string]::IsNullOrWhiteSpace($Channel)) {
  $publishArgs.Channel = $Channel
}

& $publishScript @publishArgs
