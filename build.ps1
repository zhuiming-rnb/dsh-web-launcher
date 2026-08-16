# Rebuild the native app (icons + DSH-Web.exe) from source.
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 [-Fill "#000000"] [-NoRestart]
param(
  [string]$Fill = "#000000",
  [switch]$NoRestart
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$out  = Join-Path $root 'app'

"== [1/4] icons =="
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'make-dsh-icon.ps1') -Fill $Fill
Copy-Item (Join-Path $root 'dsh.ico') (Join-Path $root 'dsh-black.ico') -Force

"== [2/4] webview2 sdk (auto-download if missing) =="
$coreDll = Join-Path $root 'webview2-sdk\pkg\lib\net462\Microsoft.Web.WebView2.Core.dll'
if (-not (Test-Path $coreDll)) {
  $ver = (Get-Content (Join-Path $root 'wv2-version.txt') -ErrorAction SilentlyContinue | Select-Object -First 1)
  if (-not $ver) {
    $ver = (Invoke-RestMethod 'https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/index.json' -TimeoutSec 30).versions |
      Where-Object { $_ -notmatch 'alpha|beta|preview|rc|prerelease' } | Select-Object -Last 1
    Set-Content (Join-Path $root 'wv2-version.txt') $ver
  }
  "downloading Microsoft.Web.WebView2 $ver ..."
  New-Item -ItemType Directory -Force -Path (Join-Path $root 'webview2-sdk') | Out-Null
  Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/$ver/microsoft.web.webview2.$ver.nupkg" `
    -OutFile (Join-Path $root 'webview2-sdk\webview2.nupkg') -TimeoutSec 180
  Add-Type -AssemblyName System.IO.Compression.FileSystem
  [System.IO.Compression.ZipFile]::ExtractToDirectory(
    (Join-Path $root 'webview2-sdk\webview2.nupkg'),
    (Join-Path $root 'webview2-sdk\pkg'))
}

"== [3/4] compile =="
Stop-Process -Name 'DSH-Web' -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
$sdk = Join-Path $root 'webview2-sdk\pkg'
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 `
  /out:"$out\DSH-Web.exe" `
  /win32icon:"$root\dsh.ico" `
  /win32manifest:"$out\app.manifest" `
  /reference:"$sdk\lib\net462\Microsoft.Web.WebView2.Core.dll" `
  /reference:"$sdk\lib\net462\Microsoft.Web.WebView2.WinForms.dll" `
  /reference:System.Windows.Forms.dll `
  /reference:System.Drawing.dll `
  "$out\DSH-Web.cs"
if ($LASTEXITCODE -ne 0) { throw "csc failed ($LASTEXITCODE)" }

"== [4/4] deploy runtime =="
Copy-Item "$sdk\lib\net462\Microsoft.Web.WebView2.Core.dll" $out -Force
Copy-Item "$sdk\lib\net462\Microsoft.Web.WebView2.WinForms.dll" $out -Force
Copy-Item "$sdk\runtimes\win-x64\native\WebView2Loader.dll" $out -Force

if (-not $NoRestart) {
  Start-Process "$out\DSH-Web.exe"
  "DSH-Web.exe rebuilt and relaunched."
} else {
  "DSH-Web.exe rebuilt. (not launched)"
}
