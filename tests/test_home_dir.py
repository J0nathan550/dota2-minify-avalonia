import os
import subprocess
import sys

MINIFY_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "Minify"))


def _run(code, env_extra):
    env = {**os.environ, **env_extra}
    return subprocess.run(
        [sys.executable, "-c", code], cwd=MINIFY_DIR, env=env, capture_output=True, text=True, timeout=60
    )


def test_minify_home_moves_user_data(tmp_path):
    home = tmp_path / "minify home"
    result = _run(
        "from core import base; print(base.base_dir); print(base.plugins_dir); print(base.locales_dir)",
        {"MINIFY_HOME": str(home)},
    )
    base_dir, plugins_dir, locales_dir = result.stdout.split("\n")[:3]

    assert base_dir == str(home)
    assert plugins_dir == str(home / "plugins")
    assert home.is_dir()
    # Bundled read-only data still comes from the program itself.
    assert locales_dir == os.path.join(MINIFY_DIR, "locales")


def test_without_minify_home_data_stays_in_the_program_dir():
    env = {k: v for k, v in os.environ.items() if k != "MINIFY_HOME"}
    result = subprocess.run(
        [sys.executable, "-c", "from core import base; print(base.base_dir)"],
        cwd=MINIFY_DIR,
        env=env,
        capture_output=True,
        text=True,
        timeout=60,
    )

    assert result.stdout.strip() == MINIFY_DIR


def test_noninteractive_cli_never_prompts_for_steam(tmp_path):
    # A fresh data home (seeded with mods by the app) has no Steam path; without MINIFY_NONINTERACTIVE this
    # would open a tkinter dialog.
    (tmp_path / "mods").mkdir()
    result = subprocess.run(
        [sys.executable, MINIFY_DIR, "--version"],
        env={**os.environ, "MINIFY_HOME": str(tmp_path), "MINIFY_NONINTERACTIVE": "1", "HOME": str(tmp_path)},
        capture_output=True,
        text=True,
        timeout=60,
    )

    from core import base

    assert result.returncode == 0
    assert result.stdout.strip().splitlines()[-1] == base.VERSION
