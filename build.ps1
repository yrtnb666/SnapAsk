<#
    一键编译 SnapAsk（.NET Framework 4.8 / WinForms，无 NuGet 运行时依赖）
    用法:
        powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
        powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -NoDownload
        powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Csc "C:\path\to\csc.exe"

    编译器查找顺序:
        1. -Csc 参数
        2. tools\.compiler\csc.exe（本目录缓存）
        3. 同级的 EAgentX-新UI版\tools\.compiler\csc.exe（复用已有的 Roslyn 缓存）
        4. Visual Studio / MSBuild 自带的 Roslyn csc.exe
        5. .NET SDK 里的 Roslyn（通过 dotnet exec 调用）
        6. Windows 自带的 %WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe（C# 5）
        7. 从 nuget.org 下载 Microsoft.Net.Compilers.Toolset 并缓存到 tools\.compiler

    说明: 源码保持 C# 5 语法，Windows 自带 csc.exe 也能编译，不联网、不装 SDK 即可构建。
#>

[CmdletBinding()]
param(
    [string]$Csc,
    [switch]$NoDownload,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$srcDir = Join-Path $root "src"
$distDir = Join-Path $root "dist"
$toolDir = Join-Path $root "tools\.compiler"
$exeOut = Join-Path $distDir "SnapAsk.exe"

function Write-Step($text) { Write-Host "==> $text" -ForegroundColor Cyan }
function Write-Ok($text) { Write-Host "    $text" -ForegroundColor Green }

# ---------- 1. 找编译器 ----------
function Find-VsCsc {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $installs = & $vswhere -products * -requires Microsoft.Component.MSBuild -property installationPath 2>$null
        foreach ($i in $installs) {
            $hit = Get-ChildItem -Path (Join-Path $i "MSBuild") -Recurse -Filter csc.exe -ErrorAction SilentlyContinue |
                   Sort-Object FullName -Descending | Select-Object -First 1
            if ($hit) { return $hit.FullName }
        }
    }
    $globs = @(
        "$env:ProgramFiles\Microsoft Visual Studio\*\*\MSBuild\Current\Bin\Roslyn\csc.exe",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\*\*\MSBuild\Current\Bin\Roslyn\csc.exe",
        "${env:ProgramFiles(x86)}\MSBuild\*\Bin\Roslyn\csc.exe",
        "$env:ProgramFiles\MSBuild\*\Bin\Roslyn\csc.exe"
    )
    foreach ($g in $globs) {
        $hit = Get-ChildItem -Path $g -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($hit) { return $hit.FullName }
    }
    return $null
}

function Install-RoslynToolset {
    if ($NoDownload) { throw "没找到 Roslyn 编译器，且指定了 -NoDownload。" }
    $version = "4.11.0"
    $url = "https://api.nuget.org/v3-flatcontainer/microsoft.net.compilers.toolset/$version/microsoft.net.compilers.toolset.$version.nupkg"
    $nupkg = Join-Path $env:TEMP "microsoft.net.compilers.toolset.$version.nupkg"
    $zip = Join-Path $env:TEMP "microsoft.net.compilers.toolset.$version.zip"

    Write-Step "下载 Roslyn 编译器 Microsoft.Net.Compilers.Toolset $version（约 7 MB，只需一次）"
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -Uri $url -OutFile $nupkg -UseBasicParsing
    Copy-Item $nupkg $zip -Force
    if (Test-Path $toolDir) { Remove-Item $toolDir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $toolDir | Out-Null
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory($zip, $toolDir)
    $found = Get-ChildItem -Path $toolDir -Recurse -Filter csc.exe | Select-Object -First 1
    if (-not $found) { throw "下载的编译器包里没有 csc.exe" }
    $flat = Split-Path $found.FullName -Parent
    Get-ChildItem $flat -File | ForEach-Object {
        $target = Join-Path $toolDir $_.Name
        if ($_.FullName -ne $target) { Copy-Item $_.FullName $target -Force }
    }
    return (Join-Path $toolDir "csc.exe")
}

function Find-FrameworkCsc {
    $candidates = @(
        (Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"),
        (Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe")
    )
    foreach ($c in $candidates) { if (Test-Path $c) { return $c } }
    return $null
}

function Find-SdkCsc {
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnet) { return $null }
    $sdkRoot = Join-Path $env:ProgramFiles "dotnet\sdk"
    if (-not (Test-Path $sdkRoot)) { return $null }
    $hit = Get-ChildItem -Path $sdkRoot -Recurse -Filter csc.dll -ErrorAction SilentlyContinue |
           Where-Object { $_.FullName -match 'Roslyn\\bincore\\csc\.dll$' } |
           Sort-Object FullName -Descending | Select-Object -First 1
    if ($hit) { return $hit.FullName }
    return $null
}

$cscExe = $null
$cscPrefix = @()

# 同级项目里已缓存的 Roslyn（避免重复下载）
$sibling = Get-ChildItem -Path (Split-Path $root -Parent) -Directory -ErrorAction SilentlyContinue |
           ForEach-Object { Join-Path $_.FullName "tools\.compiler\csc.exe" } |
           Where-Object { Test-Path $_ } | Select-Object -First 1

if ($Csc) {
    if (-not (Test-Path $Csc)) { throw "指定的编译器不存在: $Csc" }
    $cscExe = $Csc
}
elseif (Test-Path (Join-Path $toolDir "csc.exe")) {
    $cscExe = Join-Path $toolDir "csc.exe"
}
elseif ($sibling) {
    $cscExe = $sibling
}
else {
    $cscExe = Find-VsCsc
    if (-not $cscExe) {
        $dll = Find-SdkCsc
        if ($dll) { $cscPrefix = @("exec", $dll) ; $cscExe = (Get-Command dotnet).Source }
    }
    if (-not $cscExe) { $cscExe = Find-FrameworkCsc }
    if (-not $cscExe) { $cscExe = Install-RoslynToolset }
}

Write-Step "编译器: $cscExe"
if ($cscPrefix.Count -gt 0) { Write-Ok ($cscPrefix -join " ") }

# ---------- 2. 准备输出目录 ----------
if (-not (Test-Path $distDir)) { New-Item -ItemType Directory -Path $distDir | Out-Null }

$fw = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319"
if (-not (Test-Path (Join-Path $fw "System.dll"))) {
    $fw = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319"
}

$refs = @("System.dll", "System.Core.dll", "System.Drawing.dll", "System.Windows.Forms.dll",
          "System.Web.Extensions.dll", "System.Net.Http.dll") |
        ForEach-Object { "/r:" + (Join-Path $fw $_) }

$helper = Join-Path $root "assets\page_helper.js"
if (-not (Test-Path $helper)) { throw "缺少 assets\page_helper.js（注入网页的辅助脚本）" }
$resources = @("/resource:" + $helper + ",page_helper.js")

$files = Get-ChildItem -Path $srcDir -Recurse -Filter *.cs |
         Sort-Object FullName | Select-Object -ExpandProperty FullName
if (-not $files) { throw "src\ 下没有找到 .cs 文件" }

$ico = Join-Path $srcDir "app.ico"

# ---------- 3. 编译 ----------
$cscArgs = @(
    "/nologo",
    "/target:winexe",
    "/platform:anycpu",
    "/codepage:65001",
    "/langversion:5",
    "/define:TRACE",
    "/optimize+",
    "/out:$exeOut"
)
if (Test-Path $ico) { $cscArgs += "/win32icon:$ico" }
$cscArgs += $resources + $refs + $files

if ($Configuration -eq "Debug") {
    $cscArgs = $cscArgs | Where-Object { $_ -ne "/optimize+" }
    $cscArgs += @("/debug:pdbonly", "/define:DEBUG")
}

Write-Step "编译 $($files.Count) 个源文件"
if ($cscPrefix.Count -gt 0) {
    $exe = $cscPrefix[0]
    $argv = @($cscPrefix[1]) + $cscArgs
    & $exe @argv
}
else {
    & $cscExe @cscArgs
}
if ($LASTEXITCODE -ne 0) { throw "编译失败（exit $LASTEXITCODE）" }

# ---------- 4. 结果 ----------
$item = Get-Item $exeOut
$hash = (Get-FileHash $exeOut -Algorithm SHA256).Hash
Write-Ok ("产物: {0}  ({1:N0} 字节, {2})" -f $item.FullName, $item.Length, $Configuration)
Write-Ok "SHA256: $hash"
