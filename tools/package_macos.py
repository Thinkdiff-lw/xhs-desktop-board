"""Preserve the executable mode from ditto, add only generic public documents."""
import sys
from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED

build, output = Path(sys.argv[1]), Path(sys.argv[2])
documents = ("安装与使用.md", "macOS说明.md", "隐私说明.md", "LICENSE.txt")
with ZipFile(build / "app.zip") as source, ZipFile(output, "x", ZIP_DEFLATED) as target:
    for item in source.infolist():
        if "data/" in item.filename or "WebKit/" in item.filename:
            raise RuntimeError("Unexpected profile data in app bundle")
        target.writestr(item, source.read(item))
    for name in documents:
        target.write(build / name, name)
print("Checked macOS package: app and public documents only")
