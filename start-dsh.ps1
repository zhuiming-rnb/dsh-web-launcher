# Start the local DeepSeek Harness web server (port 3080) fully detached from
# any console, so closing a terminal can never take it down. Safe to run at
# every logon and from the desktop shortcut (idempotent: no-op if already up).
$ErrorActionPreference = 'SilentlyContinue'

$log  = "E:\workplace\DSH-Web\dsh-web.log"
$port = 3080

$already = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue
if ($already) {
  "port $port already listening (pid $($already[0].OwningProcess)) at $(Get-Date -Format o)" | Out-File -FilePath $log -Append -Encoding utf8
  exit 0
}

$node   = "D:\Program Files\nodejs\node.exe"
$dshBin = Join-Path $env:LOCALAPPDATA 'npm-cache\_npx\1e7f6d9597241db0\node_modules\@deepseek-ai\dsh\lib\bin.js'
if (-not (Test-Path -LiteralPath $node))  { "node missing: $node" | Out-File -FilePath $log -Append -Encoding utf8; exit 1 }
if (-not (Test-Path -LiteralPath $dshBin)) { "dsh bin missing: $dshBin" | Out-File -FilePath $log -Append -Encoding utf8; exit 1 }

# WMI Create => parent is WmiPrvSE, no console window, survives everything.
$cmdLine = "`"$node`" `"$dshBin`" web"
$p = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{ CommandLine = $cmdLine }
"started dsh pid $($p.ProcessId) at $(Get-Date -Format o)" | Out-File -FilePath $log -Append -Encoding utf8

# Health check: give the server up to 90s to bind.
$deadline = (Get-Date).AddSeconds(90)
$ok = $false
while ((Get-Date) -lt $deadline) {
  Start-Sleep -Seconds 2
  $l = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue
  if ($l) { $ok = $true; break }
  if (-not (Get-Process -Id $p.ProcessId -ErrorAction SilentlyContinue)) { break }
}
if ($ok) {
  $pid2 = (Get-NetTCPConnection -LocalPort $port -State Listen)[0].OwningProcess
  "health: port $port LISTENING (pid $pid2) at $(Get-Date -Format o)" | Out-File -FilePath $log -Append -Encoding utf8
  exit 0
}
"health: NOT listening within 90s at $(Get-Date -Format o)" | Out-File -FilePath $log -Append -Encoding utf8
exit 1
