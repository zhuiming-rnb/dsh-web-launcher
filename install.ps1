# One-click install / repair of the DSH Web desktop launcher. Idempotent.
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File install.ps1
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

"== [1/5] build app (icons + exe) =="
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'build.ps1') -NoRestart

"== [2/5] patch web frontend icons =="
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'patch-dsh-frontend.ps1')

"== [3/5] logon scheduled task (server auto-start) =="
$task = Get-ScheduledTask -TaskName 'DSH-Web' -ErrorAction SilentlyContinue
if (-not $task) {
  $action = New-ScheduledTaskAction -Execute 'powershell.exe' `
    -Argument '-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "' + (Join-Path $root 'start-dsh.ps1') + '"'
  $trigger = New-ScheduledTaskTrigger -AtLogOn
  Register-ScheduledTask -TaskName 'DSH-Web' -Action $action -Trigger $trigger `
    -Description 'Start DeepSeek Harness web server (127.0.0.1:3080) at logon, detached from any console' -Force | Out-Null
  'task DSH-Web created'
} else {
  'task DSH-Web already exists'
}

"== [4/5] desktop shortcut =="
$desktop = [Environment]::GetFolderPath('Desktop')
$lnk = Join-Path $desktop 'DeepSeek Harness Web.lnk'
$ws = New-Object -ComObject WScript.Shell
$sc = $ws.CreateShortcut($lnk)
$sc.TargetPath = Join-Path $root 'app\DSH-Web.exe'
$sc.Arguments = ''
$sc.WorkingDirectory = Join-Path $root 'app'
$sc.IconLocation = (Join-Path $root 'dsh-black.ico') + ',0'
$sc.Description = 'DeepSeek Harness 桌面版 - 自动拉起本地服务并打开'
$sc.WindowStyle = 1
$sc.Save()
"shortcut: $lnk"

"== [5/5] done =="
"安装完成。双击桌面「DeepSeek Harness Web」即可使用。"
