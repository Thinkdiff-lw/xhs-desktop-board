param([string]$Python = '.venv\Scripts\python.exe', [switch]$Deploy)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$nativeTag = 'native-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,6)
$nativeOutput = Join-Path $PSScriptRoot ('dist\' + $nativeTag)
New-Item -ItemType Directory -Path $nativeOutput -Force | Out-Null
$frameworkPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $frameworkPath 'csc.exe'
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /win32manifest:native_card\app.manifest /win32icon:icon.ico "/out:$nativeOutput\XhsBoard.exe" "/reference:$frameworkPath\WPF\PresentationFramework.dll" "/reference:$frameworkPath\WPF\PresentationCore.dll" "/reference:$frameworkPath\WPF\WindowsBase.dll" /reference:System.Xaml.dll /reference:System.Web.Extensions.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll native_card\Program.cs
if ($LASTEXITCODE -ne 0) { throw '原生窗口编译失败' }
& $Python -m PyInstaller XhsCollector.spec --distpath $nativeOutput --workpath "build\$nativeTag" --noconfirm
if ($LASTEXITCODE -ne 0) { throw '同步程序打包失败' }
Copy-Item -LiteralPath icon.ico -Destination $nativeOutput
if ($Deploy) {
    Copy-Item -LiteralPath "$nativeOutput\XhsCollector.exe" -Destination '.\XhsCollector.exe' -Force
    Copy-Item -LiteralPath "$nativeOutput\XhsBoard.exe" -Destination '.\XhsBoard.exe' -Force
}
Write-Output ('Built native card and collector at ' + $nativeOutput)
