# Desktop-shortcut target: make sure the local DSH web server is up (starting
# it detached if needed), then open http://127.0.0.1:3080/ in the default
# browser. Runs hidden; a small popup reports failure instead of a console.
param([switch]$NoOpen)

$ErrorActionPreference = 'SilentlyContinue'

$port = 3080
$url  = "http://127.0.0.1:$port/"
$log  = "E:\workplace\DSH-Web\dsh-web.log"

$listener = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue
if (-not $listener) {
  & "E:\workplace\DSH-Web\start-dsh.ps1" *>> $log
}

# Wait up to 120s for the server to come up.
$deadline = (Get-Date).AddSeconds(120)
while ((Get-Date) -lt $deadline) {
  $listener = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue
  if ($listener) { break }
  Start-Sleep -Seconds 1
}

if ($listener) {
  if (-not $NoOpen) {
    # Preferred: the native DeepSeek Harness app (WinForms + WebView2) with the
    # whale icon embedded in the exe. Fallback: Edge app mode.
    $exe = "E:\workplace\DSH-Web\app\DSH-Web.exe"
    if (Test-Path -LiteralPath $exe) {
      Start-Process -FilePath $exe
    } else {
      $edge = "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
      if (Test-Path -LiteralPath $edge) {
        $profile = Join-Path $env:LOCALAPPDATA 'DSH-Web-Edge'
        Start-Process -FilePath $edge -ArgumentList "--user-data-dir=`"$profile`" --app=$url"
      } else {
        Start-Process $url
      }
    }
    "opened $url at $(Get-Date -Format o)" | Out-File -FilePath $log -Append
  }
  exit 0
}

(New-Object -ComObject WScript.Shell).Popup(
  "DeepSeek Harness Web could not start.`n`nCheck: E:\workplace\DSH-Web\dsh-web.log",
  0, "DeepSeek Harness", 48)
exit 1
