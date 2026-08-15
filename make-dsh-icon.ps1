# Build a multi-size .ico for the DeepSeek Harness desktop shortcut.
# Source: the dsh web frontend favicon.svg (single <path> with M/C/Z commands).
# Renders the vector path at 16/20/24/32/40/48/64/128/256 px with System.Drawing,
# packs a proper ICO (BMP DIB entries below 256, PNG entry at 256), and also
# writes favicon-192.png / favicon-512.png (PWA icons) next to the .ico.
param(
  [string]$Fill     = "#4D6BFE",   # DeepSeek brand blue; visible on dark & light taskbars
  [string]$OutPath  = "E:\workplace\DSH-Web\dsh.ico"
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$fillColor = [System.Drawing.ColorTranslator]::FromHtml($Fill)
$fillBrush = New-Object System.Drawing.SolidBrush($fillColor)

$SvgPath = Join-Path $env:LOCALAPPDATA 'npm-cache\_npx\1e7f6d9597241db0\node_modules\@deepseek-ai\dsh-web-frontend\dist\favicon.svg'
if (-not (Test-Path -LiteralPath $SvgPath)) { throw "favicon not found: $SvgPath" }

# --- 1. extract the path d attribute ---
$svg = Get-Content -LiteralPath $SvgPath -Raw
$m = [regex]::Match($svg, '<path[^>]*\sd="([^"]+)"')
if (-not $m.Success) { throw "no <path d=...> found in $SvgPath" }
$d = $m.Groups[1].Value

# --- 2. tokenize letters / numbers ---
$tokens = [System.Collections.Generic.List[string]]::new()
foreach ($tok in [regex]::Matches($d, '[A-Za-z]|[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?')) {
  $tokens.Add($tok.Value)
}

# --- 3. build the GraphicsPath (M / C / Z with implicit repeats) ---
$path = [System.Drawing.Drawing2D.GraphicsPath]::new()
$i = 0
$curX = 0.0; $curY = 0.0; $startX = 0.0; $startY = 0.0
$cmd = ''
$nextNum = {
  $v = [double]$tokens[$script:i]
  $script:i++
  return $v
}
function Read-Cubic {
  $c1x = & $nextNum; $c1y = & $nextNum
  $c2x = & $nextNum; $c2y = & $nextNum
  $ex  = & $nextNum; $ey  = & $nextNum
  $path.AddBezier($curX, $curY, $c1x, $c1y, $c2x, $c2y, $ex, $ey) | Out-Null
  $script:curX = $ex; $script:curY = $ey
}
while ($i -lt $tokens.Count) {
  $t = $tokens[$i]
  if ($t -match '^[A-Za-z]$') { $cmd = $t; $i++ }
  switch ($cmd) {
    'M' {
      $startX = & $nextNum; $startY = & $nextNum
      $curX = $startX; $curY = $startY
      $path.StartFigure() | Out-Null
    }
    'C' { Read-Cubic }
    'Z' {
      $path.CloseFigure() | Out-Null
      $curX = $startX; $curY = $startY
    }
    default { throw "unhandled command: $cmd" }
  }
  # implicit repeats: consecutive number groups reuse the current command
  while ($i -lt $tokens.Count -and $tokens[$i] -notmatch '^[A-Za-z]$') {
    if ($cmd -eq 'C') { Read-Cubic }
    elseif ($cmd -eq 'M') { $curX = & $nextNum; $curY = & $nextNum }
    else { break }
  }
}

# --- 4. render sizes ---
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$blobs = @{}
foreach ($s in $sizes) {
  $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
  $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
  $g.Clear([System.Drawing.Color]::Transparent)
  $g.ScaleTransform($s / 50.0, $s / 50.0)
  $g.FillPath($fillBrush, $path)
  $g.Dispose()

  if ($s -eq 256) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $blobs[$s] = $ms.ToArray()
    $ms.Dispose()
  } else {
    $rect = New-Object System.Drawing.Rectangle(0, 0, $s, $s)
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $stride = $data.Stride
    $raw = New-Object byte[] ($stride * $s)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $raw, 0, $raw.Length)
    $bmp.UnlockBits($data)

    # XOR: BGRA rows bottom-up
    $xor = New-Object byte[] ($s * $s * 4)
    for ($row = 0; $row -lt $s; $row++) {
      $src = ($s - 1 - $row) * $stride
      $dst = $row * $s * 4
      [Array]::Copy($raw, $src, $xor, $dst, $s * 4)
    }
    # AND mask: all zeros (alpha respected)
    $andRow = [int][math]::Ceiling($s / 32.0) * 4
    $and = New-Object byte[] ($andRow * $s)

    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    $bw.Write([int32]40)            # biSize
    $bw.Write([int32]$s)            # biWidth
    $bw.Write([int32]($s * 2))      # biHeight (XOR + AND)
    $bw.Write([int16]1)             # biPlanes
    $bw.Write([int16]32)            # biBitCount
    $bw.Write([int32]0)             # biCompression
    $bw.Write([int32]($s * $s * 4)) # biSizeImage
    $bw.Write([int64]0)             # biXPels/biYPels
    $bw.Write([int32]0)             # biClrUsed
    $bw.Write([int32]0)             # biClrImportant
    $bw.Write($xor)
    $bw.Write($and)
    $bw.Flush()
    $blobs[$s] = $ms.ToArray()
    $bw.Dispose(); $ms.Dispose()
  }
  $bmp.Dispose()
}

# --- 5. assemble the .ico ---
$fs = [System.IO.File]::Open($OutPath, [System.IO.FileMode]::Create)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([int16]0)                     # reserved
$bw.Write([int16]1)                     # type: icon
$bw.Write([int16]$sizes.Count)          # count
$offset = 6 + 16 * $sizes.Count
foreach ($s in $sizes) {
  $w = if ($s -ge 256) { 0 } else { $s }
  $h = if ($s -ge 256) { 0 } else { $s }
  $bw.Write([byte]$w)                   # width
  $bw.Write([byte]$h)                   # height
  $bw.Write([byte]0)                    # color count
  $bw.Write([byte]0)                    # reserved
  $bw.Write([int16]1)                   # planes
  $bw.Write([int16]32)                  # bit count
  $bw.Write([int32]$blobs[$s].Length)   # size
  $bw.Write([int32]$offset)             # offset
  $offset += $blobs[$s].Length
}
foreach ($s in $sizes) { $bw.Write($blobs[$s]) }
$bw.Flush(); $bw.Dispose()
"wrote $OutPath ($($sizes.Count) sizes, $((Get-Item $OutPath).Length) bytes)"

# --- 6. PWA PNG icons (192 / 512) next to the .ico ---
$dir = Split-Path $OutPath -Parent
foreach ($size in @(192, 512)) {
  $png = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($png)
  $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
  $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
  $g.Clear([System.Drawing.Color]::Transparent)
  $g.ScaleTransform($size / 50.0, $size / 50.0)
  $g.FillPath($fillBrush, $path)
  $g.Dispose()
  $file = Join-Path $dir "favicon-$size.png"
  $png.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
  $png.Dispose()
  "wrote $file"
}
$fillBrush.Dispose()
