"""Build release packages of the desktop app.

    uv run python scripts/package.py --version 1.0.0.2

Produces in dist/:
- Linux:   Minify-<version>-linux-x86_64.AppImage
- Windows: Minify-<version>-win-x64.zip

Layout of the app (the AppImage's usr/bin, or the zip's root):
    Minify[.exe]                       desktop app (self-contained, single file)
    backend/minify-backend[.exe]       Python backend (PyInstaller, one folder)
    backend/seed/{mods,plugins,themes} copied into the user's data folder on first run of each version
    LICENSE, NOTICE
"""

import argparse
import os
import platform
import shutil
import stat
import struct
import subprocess
import sys
import urllib.request
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
MINIFY = ROOT / "Minify"
BUILD = ROOT / "build"
DIST = ROOT / "dist"
APPIMAGETOOL_URL = "https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage"
SEED_FOLDERS = ("mods", "plugins", "themes")
SKIP = shutil.ignore_patterns("__pycache__", "*.pyc", "node_modules", "src")


def log(message: str) -> None:
    print(f"[package] {message}", flush=True)


def run(*cmd: str | Path, **kwargs) -> None:
    log(" ".join(str(c) for c in cmd))
    subprocess.run([str(c) for c in cmd], check=True, **kwargs)


def default_runtime() -> str:
    return "win-x64" if sys.platform == "win32" else "linux-x64"


def build_backend() -> Path:
    "PyInstaller build of Minify/ as build/backend/minify-backend/."
    run(
        "uv", "run", "pyinstaller", "--noconfirm", "--clean",
        "--distpath", BUILD / "backend", "--workpath", BUILD / "pyinstaller",
        ROOT / "scripts" / "backend.spec",
    )  # fmt: skip
    return BUILD / "backend" / "minify-backend"


def build_app(version: str, runtime: str) -> Path:
    "Self-contained single-file publish of the desktop app."
    out = BUILD / "app" / runtime
    if out.exists():
        shutil.rmtree(out)
    run(
        "dotnet", "publish", ROOT / "desktop" / "Minify.Desktop" / "Minify.Desktop.csproj",
        "-c", "Release", "-r", runtime, "--self-contained", "true",
        "-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true",
        "-p:InvariantGlobalization=true", "-p:DebugType=none",
        f"-p:Version={version}", "-o", out,
    )  # fmt: skip
    return out


def assemble(version: str, runtime: str, app: Path, backend: Path) -> Path:
    "The app folder as installed: the zip's contents, or the AppImage's usr/bin."
    target = BUILD / "package" / runtime
    if target.exists():
        shutil.rmtree(target)
    exe = "Minify.exe" if runtime.startswith("win") else "Minify"
    target.mkdir(parents=True)
    shutil.copy2(app / exe, target / exe)
    shutil.copytree(backend, target / "backend")
    for folder in SEED_FOLDERS:
        shutil.copytree(MINIFY / folder, target / "backend" / "seed" / folder, ignore=SKIP)
    for name in ("LICENSE", "NOTICE"):
        shutil.copy2(ROOT / name, target / name)
    (target / "VERSION").write_text(version + "\n", encoding="utf-8")
    return target


def extract_icon(png_path: Path) -> None:
    "The 256x256 PNG stored inside favicon.ico."
    data = (MINIFY / "bin" / "images" / "favicon.ico").read_bytes()
    count = struct.unpack("<HHH", data[:6])[2]
    best = None
    for i in range(count):
        width, _, _, _, _, _, size, offset = struct.unpack("<BBBBHHII", data[6 + 16 * i : 22 + 16 * i])
        chunk = data[offset : offset + size]
        if chunk.startswith(b"\x89PNG") and (best is None or (width or 256) > best[0]):
            best = (width or 256, chunk)
    if best is None:
        raise RuntimeError("favicon.ico has no PNG image for the AppImage icon")
    png_path.write_bytes(best[1])


def appimagetool() -> Path:
    tool = BUILD / "appimagetool-x86_64.AppImage"
    if not tool.exists():
        log(f"Downloading {APPIMAGETOOL_URL}")
        BUILD.mkdir(parents=True, exist_ok=True)
        urllib.request.urlretrieve(APPIMAGETOOL_URL, tool)
        tool.chmod(tool.stat().st_mode | stat.S_IEXEC)
    return tool


def make_appimage(version: str, folder: Path) -> Path:
    appdir = BUILD / "Minify.AppDir"
    if appdir.exists():
        shutil.rmtree(appdir)
    shutil.copytree(folder, appdir / "usr" / "bin", symlinks=True)
    apprun = appdir / "AppRun"
    apprun.write_text('#!/bin/sh\nHERE="$(dirname "$(readlink -f "$0")")"\nexec "$HERE/usr/bin/Minify" "$@"\n')
    apprun.chmod(0o755)
    (appdir / "dota2-minify.desktop").write_text(
        "[Desktop Entry]\n"
        "Type=Application\n"
        "Name=Minify\n"
        "Comment=Dota 2 mod patcher and toolkit\n"
        "Exec=Minify\n"
        "Icon=dota2-minify\n"
        "Categories=Game;Utility;\n"
        "Terminal=false\n"
    )
    extract_icon(appdir / "dota2-minify.png")
    shutil.copy2(appdir / "dota2-minify.png", appdir / ".DirIcon")

    DIST.mkdir(exist_ok=True)
    output = DIST / f"Minify-{version}-linux-x86_64.AppImage"
    # appimagetool is itself an AppImage; extract-and-run works without FUSE (CI containers).
    env = {**os.environ, "ARCH": "x86_64", "APPIMAGE_EXTRACT_AND_RUN": "1"}
    run(appimagetool(), "--no-appstream", appdir, output, env=env)
    return output


def make_zip(version: str, folder: Path) -> Path:
    DIST.mkdir(exist_ok=True)
    output = DIST / f"Minify-{version}-win-x64.zip"
    with zipfile.ZipFile(output, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for path in sorted(folder.rglob("*")):
            if path.is_file():
                archive.write(path, path.relative_to(folder).as_posix())
    return output


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--version", required=True, help="4-part app version, e.g. 1.0.0.2")
    parser.add_argument("--runtime", default=default_runtime(), choices=["linux-x64", "win-x64"])
    parser.add_argument("--skip-backend", action="store_true", help="reuse build/backend from a previous run")
    parser.add_argument(
        "--folder-only", action="store_true", help="stop after build/package/<runtime> (no AppImage or zip)"
    )
    args = parser.parse_args(argv)

    if platform.machine().lower() not in ("x86_64", "amd64"):
        log(f"Warning: building x64 packages on {platform.machine()}")

    backend = BUILD / "backend" / "minify-backend" if args.skip_backend else build_backend()
    app = build_app(args.version, args.runtime)
    folder = assemble(args.version, args.runtime, app, backend)
    if args.folder_only:
        log(f"Created {folder}")
        return 0
    output = make_appimage(args.version, folder) if args.runtime == "linux-x64" else make_zip(args.version, folder)
    log(f"Created {output} ({output.stat().st_size / 1024 / 1024:.1f} MB)")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
