param([string]$Source = 'native_card\Program.cs', [switch]$Baseline)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath (Split-Path -Parent $PSScriptRoot)
$qaTag = 'visibility-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,6)
$qaDirectory = Join-Path $PWD ('.cache\' + $qaTag)
New-Item -ItemType Directory -Path $qaDirectory -Force | Out-Null
$frameworkPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$qaBinary = Join-Path $qaDirectory 'VisibilityTest.exe'
$compilerArguments = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/main:XhsNative.VisibilityTests', "/out:$qaBinary", "/reference:$frameworkPath\WPF\PresentationFramework.dll", "/reference:$frameworkPath\WPF\PresentationCore.dll", "/reference:$frameworkPath\WPF\WindowsBase.dll", '/reference:System.Xaml.dll', '/reference:System.Web.Extensions.dll', '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', $Source, 'tests\native_visibility.cs')
if ($Baseline) { $compilerArguments += '/define:BASELINE' }
& (Join-Path $frameworkPath 'csc.exe') @compilerArguments
if ($LASTEXITCODE -ne 0) { throw 'Visibility harness compilation failed' }
$results = @()
foreach ($initial in @('show','hide')) {
    foreach ($final in @('show','hide')) {
        $caseDirectory = Join-Path $qaDirectory ($initial + '-to-' + $final)
        $caseProcess = Start-Process -FilePath $qaBinary -ArgumentList @(('"' + $caseDirectory + '"'), $initial, $final) -WindowStyle Hidden -PassThru
        if (-not $caseProcess.WaitForExit(15000)) {
            Stop-Process -Id $caseProcess.Id
            throw 'Owned visibility test process timed out'
        }
        $result = Get-Content -LiteralPath (Join-Path $caseDirectory 'visibility-qa.json') -Raw | ConvertFrom-Json
        $results += $result
        $result | ConvertTo-Json -Compress
    }
}
if (@($results | Where-Object { -not $_.ok }).Count -gt 0) { throw 'Visibility regression failed' }
Write-Output 'PASS: startup restoration, tray toggle, close-to-hide and session ending in four visibility combinations'
