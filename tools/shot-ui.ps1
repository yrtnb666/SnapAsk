Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;

public class Win {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

  public static List<IntPtr> OfProcess(int pid) {
    List<IntPtr> list = new List<IntPtr>();
    EnumWindows(delegate(IntPtr h, IntPtr l) {
      int p; GetWindowThreadProcessId(h, out p);
      if (p == pid && IsWindowVisible(h)) list.Add(h);
      return true;
    }, IntPtr.Zero);
    return list;
  }

  public static string Capture(IntPtr h, string path) {
    RECT r;
    if (!GetWindowRect(h, out r)) return "no-rect";
    int w = r.Right - r.Left, t = r.Bottom - r.Top;
    if (w < 40 || t < 40) return "too-small " + w + "x" + t;
    Bitmap bmp = new Bitmap(w, t);
    Graphics g = Graphics.FromImage(bmp);
    IntPtr hdc = g.GetHdc();
    bool ok = PrintWindow(h, hdc, 2);
    g.ReleaseHdc(hdc);
    g.Dispose();
    bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    bmp.Dispose();
    return (ok ? "ok " : "print-failed ") + w + "x" + t;
  }
}
"@

$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root "dist\SnapAsk.exe"
$docs = Join-Path $root "docs"
$info = Join-Path $root "ui-info.txt"

[Win]::SetProcessDPIAware() | Out-Null

Get-Process SnapAsk -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 1200
[System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point(300, 200)

$proc = Start-Process -FilePath $exe -ArgumentList "--uidemo" -PassThru
Start-Sleep -Seconds 8

$lines = @()
$windows = [Win]::OfProcess($proc.Id)
$captures = @()
foreach ($h in $windows) {
    $cls = New-Object System.Text.StringBuilder 256
    [Win]::GetClassName($h, $cls, 256) | Out-Null
    $tmp = Join-Path $docs ("tmp-" + $captures.Count + ".png")
    $res = [Win]::Capture($h, $tmp)
    $lines += "$($cls.ToString()) -> $res"
    $captures += $tmp
}

# 按窗口大小命名：大的是主窗口，小的是答案卡片
$sizes = @()
foreach ($f in $captures) {
    $img = [System.Drawing.Image]::FromFile($f)
    $sizes += ($img.Width * $img.Height)
    $img.Dispose()
}
$idx = 0
$main = -1; $answer = -1
for ($i = 0; $i -lt $sizes.Count; $i++) {
    if ($main -lt 0 -or $sizes[$i] -gt $sizes[$main]) { $main = $i }
}
for ($i = 0; $i -lt $sizes.Count; $i++) {
    if ($i -ne $main -and ($answer -lt 0 -or $sizes[$i] -gt $sizes[$answer])) { $answer = $i }
}
if ($main -ge 0) { Move-Item $captures[$main] (Join-Path $docs "ui-window.png") -Force }
if ($answer -ge 0) { Move-Item $captures[$answer] (Join-Path $docs "ui-answer.png") -Force }
for ($i = 0; $i -lt $captures.Count; $i++) {
    if (($i -ne $main) -and ($i -ne $answer) -and (Test-Path $captures[$i])) { Remove-Item $captures[$i] -Force }
}
[System.IO.File]::WriteAllLines($info, $lines, (New-Object System.Text.UTF8Encoding($false)))

Get-Process SnapAsk -ErrorAction SilentlyContinue | Stop-Process -Force
