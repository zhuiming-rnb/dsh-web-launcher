# Uninstall the DSH Web desktop launcher: desktop shortcut, scheduled task,
# and optionally the whole E:\workplace\DSH-Web folder. Reverts the frontend
# icon patch so the stock dsh files are restored.
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File uninstall.ps1 [-RemoveApp]
param([switch]$RemoveApp)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

"== [1/4] desktop shortcut =="
$desktop = [Environment]::GetFolderPath('Desktop')
$lnk = Join-Path $desktop 'DeepSeek Harness Web.lnk'
if (Test-Path $lnk) { Remove-Item $lnk -Force; 'removed shortcut' } else { 'no shortcut' }

"== [2/4] scheduled task =="
$task = Get-ScheduledTask -TaskName 'DSH-Web' -ErrorAction SilentlyContinue
if ($task) { Unregister-ScheduledTask -TaskName 'DSH-Web' -Confirm:$false; 'removed task DSH-Web' } else { 'no task' }

"== [3/4] revert frontend icon patch =="
$dist = Join-Path $env:LOCALAPPDATA 'npm-cache\_npx\1e7f6d9597241db0\node_modules\@deepseek-ai\dsh-web-frontend\dist'
if (Test-Path $dist) {
  # index.html: drop the PNG icon links we added
  $idx = Join-Path $dist 'index.html'
  $html = Get-Content -LiteralPath $idx -Raw
  if ($html -match 'favicon-192\.png') {
    $html = $html -replace "`r?`n\s*<link rel=`"icon`" type=`"image/png`" sizes=`"192x192`" href=`"/favicon-192\.png`" />", ''
    $html = $html -replace "`r?`n\s*<link rel=`"icon`" type=`"image/png`" sizes=`"512x512`" href=`"/favicon-512\.png`" />", ''
    Set-Content -LiteralPath $idx -Value $html -Encoding UTF8 -NoNewline
    'reverted index.html'
  } else { 'index.html untouched' }
  # manifest.webmanifest: drop the PNG icon entries
  $man = Join-Path $dist 'manifest.webmanifest'
  $json = Get-Content -LiteralPath $man -Raw | ConvertFrom-Json
  $kept = @($json.icons | Where-Object { $_.src -notmatch 'favicon-(192|512)\.png' })
  if ($kept.Count -ne $json.icons.Count) {
    $json.icons = $kept
    $json | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $man -Encoding UTF8 -NoNewline
    'reverted manifest.webmanifest'
  } else { 'manifest untouched' }
  # remove copied icon files
  foreach ($f in @('favicon.ico', 'favicon-192.png', 'favicon-512.png')) {
    $p = Join-Path $dist $f
    if (Test-Path $p) { Remove-Item $p -Force }
  }
} else { 'frontend dist not found, skip revert' }

"== [4/4] app folder =="
if ($RemoveApp) {
  Stop-Process -Name 'DSH-Web' -Force -ErrorAction SilentlyContinue
  Start-Sleep -Seconds 2
  Remove-Item $root -Recurse -Force
  "removed $root"
} else {
  "保留目录 $root（如需彻底删除请加 -RemoveApp）"
}
'卸载完成。'
