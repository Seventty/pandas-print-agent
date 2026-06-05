$ErrorActionPreference = "Stop"
& (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "publish-app.ps1") -Runtime "linux-x64"
