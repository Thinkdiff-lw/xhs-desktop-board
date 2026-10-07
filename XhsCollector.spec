from PyInstaller.utils.hooks import collect_data_files
a = Analysis(['collector_worker.py'], pathex=[], binaries=[],
    datas=collect_data_files('webview', subdir='js'),
    hiddenimports=['webview.platforms.winforms', 'webview.platforms.edgechromium'],
    hookspath=[], runtime_hooks=[], excludes=['tkinter', 'numpy', 'pystray', 'PIL'], noarchive=False)
pyz = PYZ(a.pure)
exe = EXE(pyz, a.scripts, a.binaries, a.datas, [], name='XhsCollector',
    debug=False, bootloader_ignore_signals=False, strip=False, upx=False, console=False, icon='icon.ico')
