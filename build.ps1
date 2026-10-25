param(
    [string]$Output = "PortalAutoLogin.exe"
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
$refDir = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'

$csc = 'C:\Program Files\Microsoft Visual Studio\18\Professional\MSBuild\Current\Bin\Roslyn\csc.exe'
if (-not (Test-Path $csc)) {
    $vswhere = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vswhere) {
        $vsPath = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
        if ($vsPath) { $csc = Join-Path $vsPath 'MSBuild\Current\Bin\Roslyn\csc.exe' }
    }
}
if (-not (Test-Path $csc)) {
    throw "找不到 Roslyn 编译器（csc.exe）。请确认已安装 Visual Studio 或 .NET Framework 4.8 开发工具。"
}
if (-not (Test-Path $refDir)) {
    throw "找不到 .NET Framework 4.8 引用程序集目录：$refDir"
}

$versionFile = Join-Path $root 'VERSION'
if (Test-Path $versionFile) {
    $fileVersion = (Get-Content $versionFile -Raw).Trim()
    $asmText = Get-Content (Join-Path $root 'src\AssemblyInfo.cs') -Raw
    $match = [regex]::Match($asmText, 'AssemblyFileVersion\("([^"]+)"\)')
    if (-not $match.Success) { throw '无法从 src\AssemblyInfo.cs 读取 AssemblyFileVersion' }
    $asmVersion = ([version]$match.Groups[1].Value).ToString(3)
    if ($asmVersion -ne $fileVersion) {
        throw "版本号不一致：VERSION=$fileVersion，AssemblyInfo=$asmVersion。请同步修改后再编译。"
    }
}

$refs = @(
    'System.dll',
    'System.Core.dll',
    'System.Drawing.dll',
    'System.Windows.Forms.dll',
    'System.Web.dll',
    'System.Web.Extensions.dll',
    'System.Security.dll',
    'Microsoft.CSharp.dll'
) | ForEach-Object { '/r:"' + (Join-Path $refDir $_) + '"' }

$sources = Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { '"' + $_.FullName + '"' }
$out = Join-Path $root $Output
$icon = Join-Path $root 'assets\PortalAutoLogin.ico'

$cscArgs = @(
    '/nologo',
    '/target:winexe',
    '/platform:anycpu',
    '/optimize+',
    '/debug-',
    '/langversion:latest',
    '/utf8output',
    ('/out:"' + $out + '"'),
    ('/win32icon:"' + $icon + '"')
) + $refs + $sources

& $csc @cscArgs
if ($LASTEXITCODE -ne 0) { throw "编译失败，退出码 $LASTEXITCODE" }

$file = Get-Item $out
$version = (Get-Item $out).VersionInfo.FileVersion
Write-Host ("编译成功：{0}  ({1:N0} 字节, 版本 {2})" -f $file.FullName, $file.Length, $version) -ForegroundColor Green
