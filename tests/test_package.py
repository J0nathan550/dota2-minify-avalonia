import struct
import zipfile

import package


def test_appimage_icon_is_the_256px_png_from_the_favicon(tmp_path):
    icon = tmp_path / "icon.png"

    package.extract_icon(icon)

    data = icon.read_bytes()
    assert data.startswith(b"\x89PNG\r\n\x1a\n")
    assert struct.unpack(">II", data[16:24]) == (256, 256)


def test_windows_zip_has_the_app_at_its_root(tmp_path, monkeypatch):
    # The updater extracts the zip and expects Minify.exe at the top.
    folder = tmp_path / "Minify"
    (folder / "backend" / "seed" / "mods" / "Dark Terrain").mkdir(parents=True)
    (folder / "Minify.exe").write_bytes(b"app")
    (folder / "backend" / "minify-backend.exe").write_bytes(b"backend")
    (folder / "backend" / "seed" / "mods" / "Dark Terrain" / "manifest.json").write_text("{}")
    monkeypatch.setattr(package, "DIST", tmp_path / "dist")

    output = package.make_zip("1.0.0.2", folder)

    assert output.name == "Minify-1.0.0.2-win-x64.zip"
    with zipfile.ZipFile(output) as archive:
        assert sorted(archive.namelist()) == [
            "Minify.exe",
            "backend/minify-backend.exe",
            "backend/seed/mods/Dark Terrain/manifest.json",
        ]


def test_seed_copies_skip_build_leftovers():
    ignored = package.SKIP("Minify/plugins/d2pfx", ["__pycache__", "src", "api.py", "x.pyc"])
    assert ignored == {"__pycache__", "src", "x.pyc"}
