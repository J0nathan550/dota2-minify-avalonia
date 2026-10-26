# -*- mode: python ; coding: utf-8 -*-
# PyInstaller spec for the headless backend that the desktop app runs (`minify-backend rpc`).
# Build with: uv run pyinstaller --noconfirm scripts/backend.spec
import os

MINIFY = os.path.abspath(os.path.join(SPECPATH, "..", "Minify"))

a = Analysis(
    [os.path.join(MINIFY, "__main__.py")],
    pathex=[MINIFY],
    # Read-only data inside the bundle; mods, plugins and themes are seeded into the user's data folder instead.
    datas=[
        (os.path.join(MINIFY, "bin"), "bin"),
        (os.path.join(MINIFY, "locales"), "locales"),
    ],
    # Plugins and mod scripts are loaded at runtime, so their imports are invisible to the analysis.
    hiddenimports=[
        "concurrent.futures",
        "core.plugin_sdk",
        "defusedxml.ElementTree",
        "gzip",
        "hashlib",
        "hmac",
        "io",
        "struct",
        "tempfile",
        "urllib.parse",
        "zipfile",
    ],
    # No GUI toolkit: the interface is the desktop app.
    excludes=["plugins", "tkinter", "webview"],
)

pyz = PYZ(a.pure)

exe = EXE(
    pyz,
    a.scripts,
    [],
    exclude_binaries=True,
    name="minify-backend",
    console=True,
    icon=[os.path.join(MINIFY, "bin", "images", "favicon.ico")],
)

coll = COLLECT(exe, a.binaries, a.datas, name="minify-backend")
