$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
if (-not (Test-Path -LiteralPath '.venv\Scripts\python.exe')) {
    python -m venv .venv
    if ($LASTEXITCODE -ne 0) { throw '创建 Python 环境失败' }
}
& '.\.venv\Scripts\python.exe' -m pip install --cache-dir .cache\pip -r requirements.txt
if ($LASTEXITCODE -ne 0) { throw '安装依赖失败' }
& '.\build-native.ps1' -Python '.venv\Scripts\python.exe' -Deploy
Write-Host '已生成 XhsBoard.exe 和 XhsCollector.exe，双击看板即可运行。旧构建和数据均保留。'
