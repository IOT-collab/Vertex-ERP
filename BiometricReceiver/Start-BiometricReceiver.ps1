$ErrorActionPreference = "Stop"
$workspacePath = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$receiverPath = Join-Path $workspacePath "services\BiometricReceiver\BiometricReceiver.exe"
$receiverDirectory = Split-Path -Parent $receiverPath
$logDirectory = Join-Path $receiverDirectory "logs"
$logPath = Join-Path $logDirectory "receiver.log"

if (-not (Test-Path -LiteralPath $receiverPath)) {
    throw "Published biometric receiver was not found at $receiverPath"
}

if (Get-NetTCPConnection -LocalPort 8082 -State Listen -ErrorAction SilentlyContinue) {
    exit 0
}

New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
Set-Location -LiteralPath $receiverDirectory
$env:ASPNETCORE_ENVIRONMENT = "Development"
"[$(Get-Date -Format o)] Starting Vertex ERP biometric receiver." | Add-Content -LiteralPath $logPath
& $receiverPath *>> $logPath
