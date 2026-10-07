@echo off
cd /d "%~dp0"
if exist "XhsBoard.exe" (
    start "" "XhsBoard.exe"
) else (
    if exist ".venv\Scripts\pythonw.exe" (
        start "" ".venv\Scripts\pythonw.exe" "app.py"
    ) else (
        echo Please build XhsBoard.exe or install requirements.txt first.
        pause
    )
)
