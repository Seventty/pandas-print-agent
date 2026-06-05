$ErrorActionPreference = "Stop"
& (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "publish-app.ps1") -Runtime "win-x64"
