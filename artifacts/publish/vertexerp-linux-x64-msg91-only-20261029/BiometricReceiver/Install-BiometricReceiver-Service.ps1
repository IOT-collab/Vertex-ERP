param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDirectory
)

$ErrorActionPreference = "Stop"
$serviceName = "VertexERPBiometricReceiver"
$executablePath = Join-Path $PublishDirectory "BiometricReceiver.exe"

if (-not (Test-Path -LiteralPath $executablePath)) {
    throw "BiometricReceiver.exe was not found in: $PublishDirectory"
}

$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing) {
    if ($existing.Status -ne "Stopped") { Stop-Service -Name $serviceName -Force }
    & sc.exe delete $serviceName | Out-Null
    Start-Sleep -Seconds 2
}

& sc.exe create $serviceName binPath= ('"' + $executablePath + '"') start= auto DisplayName= "Vertex ERP Biometric Receiver" | Out-Null
& sc.exe failure $serviceName reset= 86400 actions= restart/5000/restart/10000/restart/30000 | Out-Null
Start-Service -Name $serviceName
Write-Host "Installed and started $serviceName. Test http://localhost:8082/health"
