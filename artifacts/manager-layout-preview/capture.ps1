$ErrorActionPreference = 'Stop'
$previewFolder = 'D:\vertex ERP\Vertex-ERP\artifacts\manager-layout-preview'
$previewProcess = Start-Process -FilePath 'dotnet.exe' -ArgumentList '"D:\vertex ERP\Vertex-ERP\artifacts\manager-layout-preview\bin\Debug\net8.0\preview.dll"' -WorkingDirectory $previewFolder -WindowStyle Hidden -PassThru -RedirectStandardOutput "$previewFolder\server.log" -RedirectStandardError "$previewFolder\server-error.log"
try {
 $previewReady = $false
 for ($previewAttempt=0; $previewAttempt -lt 12; $previewAttempt++) {
  try { $previewPage = Invoke-WebRequest -Uri 'http://127.0.0.1:5188/preview' -UseBasicParsing -TimeoutSec 5; if ($previewPage.StatusCode -eq 200) { $previewReady = $true; break } } catch { Start-Sleep -Milliseconds 500 }
 }
 if (-not $previewReady) { Get-Content -LiteralPath "$previewFolder\server.log" -Tail 38; throw 'Preview page could not be rendered.' }
 [System.IO.File]::WriteAllText("$previewFolder\rendered.html", $previewPage.Content)
 $previewChrome = Start-Process -FilePath 'C:\Program Files\Google\Chrome\Application\chrome.exe' -ArgumentList @('--headless=new','--disable-gpu','--no-first-run','--no-default-browser-check','--disable-background-networking','--disable-component-update','--disable-sync','--hide-scrollbars','--window-size=1672,1100','--virtual-time-budget=4500','--user-data-dir="D:\vertex ERP\Vertex-ERP\artifacts\manager-layout-preview\chrome-profile"','--screenshot="D:\vertex ERP\Vertex-ERP\artifacts\manager-layout-preview\manager-dashboard.png"','http://127.0.0.1:5188/preview') -WindowStyle Hidden -PassThru
 if (-not $previewChrome.WaitForExit(45000)) { Stop-Process -Id $previewChrome.Id; throw 'Screenshot capture timed out.' }
 Get-Item -LiteralPath "$previewFolder\manager-dashboard.png" | Select-Object FullName,Length
} finally { if (-not $previewProcess.HasExited) { Stop-Process -Id $previewProcess.Id } }
