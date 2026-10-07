from PyInstaller.utils.hooks import collect_data_files

a = Analysis(
    ['app.py'], pathex=[], binaries=[],
    datas=[('web', 'web'), ('icon.ico', '.')] + collect_data_files('webview', subdir='js'),
    hiddenimports=['pystray._win32', 'comtypes', 'webview.platforms.winforms', 'webview.platforms.edgechromium'],
    hookspath=[], runtime_hooks=[], excludes=['tkinter'], noarchive=False,
)
pyz = PYZ(a.pure)
exe = EXE(pyz, a.scripts, a.binaries, a.datas, [], name='XhsBoard',
          debug=False, bootloader_ignore_signals=False, strip=False,
          upx=False, console=False, icon='icon.ico')
