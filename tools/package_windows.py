"""Release allowlist: never walk the working folder or include a user profile."""
import argparse
import hashlib
import importlib.metadata
import sys
from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED

parser = argparse.ArgumentParser()
parser.add_argument("--binaries", type=Path, required=True)
parser.add_argument("--output", type=Path, required=True)
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
entries = [(args.binaries / name, name) for name in ("XhsBoard.exe", "XhsCollector.exe", "icon.ico")]
entries += [(root / "启动看板.bat", "启动看板.bat"), (root / "LICENSE", "LICENSE.txt")]
entries += [(root / "docs" / name, name) for name in ("安装与使用.md", "隐私说明.md", "第三方组件.md")]
# Include installed dependency notices without copying any environment paths.
for package in ("pywebview", "pythonnet", "PyInstaller", "comtypes", "clr_loader", "cffi", "pycparser", "proxy_tools", "typing_extensions", "bottle"):
    try:
        distribution = importlib.metadata.distribution(package)
    except importlib.metadata.PackageNotFoundError:
        continue
    for item in distribution.files or []:
        if item.name.lower().startswith(("license", "copying")) and distribution.locate_file(item).is_file():
            entries.append((distribution.locate_file(item), "第三方许可/" + package + "/" + item.name))
python_license = Path(sys.base_prefix) / "LICENSE.txt"
if python_license.is_file():
    entries.append((python_license, "第三方许可/Python/LICENSE.txt"))
if any(not path.is_file() for path, _ in entries):
    raise RuntimeError("A release file is missing")
args.output.parent.mkdir(parents=True, exist_ok=True)
with ZipFile(args.output, "x", ZIP_DEFLATED) as archive:
    for path, name in entries:
        archive.write(path, name)
with ZipFile(args.output) as archive:
    assert set(archive.namelist()) == {name for _, name in entries}
digest = hashlib.sha256(args.output.read_bytes()).hexdigest()
args.output.with_suffix(args.output.suffix + ".sha256").write_text(digest + "  " + args.output.name + "\n", encoding="ascii")
print(f"Checked Windows package: {len(entries)} allowlisted files")
