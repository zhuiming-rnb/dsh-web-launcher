# Patch the installed dsh web frontend so the app window gets a raster (PNG)
# DeepSeek icon on the taskbar and the PWA becomes installable with real icons.
# Idempotent: safe to re-run after a dsh update. Copies favicon-192/512.png from
# E:\workplace\DSH-Web into the frontend dist and extends index.html +
# manifest.webmanifest with PNG icon references.
$ErrorActionPreference = 'Stop'

$dist = Join-Path $env:LOCALAPPDATA 'npm-cache\_npx\1e7f6d9597241db0\node_modules\@deepseek-ai\dsh-web-frontend\dist'
$web  = 'E:\workplace\DSH-Web'
if (-not (Test-Path -LiteralPath $dist)) { throw "frontend dist not found: $dist" }

# 1. copy raster icons into the served root
Copy-Item -LiteralPath (Join-Path $web 'favicon-192.png') -Destination (Join-Path $dist 'favicon-192.png') -Force
Copy-Item -LiteralPath (Join-Path $web 'favicon-512.png') -Destination (Join-Path $dist 'favicon-512.png') -Force
Copy-Item -LiteralPath (Join-Path $web 'dsh.ico') -Destination (Join-Path $dist 'favicon.ico') -Force
'copied favicon-192.png, favicon-512.png, favicon.ico -> dist'

# 2. index.html: advertise the PNG favicons
$idx = Join-Path $dist 'index.html'
$html = Get-Content -LiteralPath $idx -Raw
if ($html -notmatch 'favicon-192\.png') {
  $needle = '<link rel="icon" type="image/svg+xml" href="/favicon.svg" />'
  if (-not $html.Contains($needle)) { throw "index.html icon link not found" }
  $add = $needle + "`n    <link rel=`"icon`" type=`"image/png`" sizes=`"192x192`" href=`"/favicon-192.png`" />" + "`n    <link rel=`"icon`" type=`"image/png`" sizes=`"512x512`" href=`"/favicon-512.png`" />"
  $html = $html.Replace($needle, $add)
  Set-Content -LiteralPath $idx -Value $html -Encoding UTF8 -NoNewline
  'patched index.html'
} else {
  'index.html already patched'
}

# 3. manifest.webmanifest: add PNG icons so Edge can install the PWA
$man = Join-Path $dist 'manifest.webmanifest'
$json = Get-Content -LiteralPath $man -Raw | ConvertFrom-Json
if (-not ($json.icons | Where-Object { $_.src -eq '/favicon-192.png' })) {
  $json.icons += @(
    @{ src = '/favicon-192.png'; sizes = '192x192'; type = 'image/png'; purpose = 'any' },
    @{ src = '/favicon-512.png'; sizes = '512x512'; type = 'image/png'; purpose = 'any maskable' }
  )
  $json | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $man -Encoding UTF8 -NoNewline
  'patched manifest.webmanifest'
} else {
  'manifest.webmanifest already patched'
}
