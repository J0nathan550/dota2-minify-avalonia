"""End-to-end self-update test of real release packages (run by the release pipeline).

    python scripts/e2e_update.py --old dist/Minify-1.0.0.0-<platform> --old-version 1.0.0.0 \
                                 --new dist/Minify-1.0.0.7-<platform> --new-version 1.0.0.7

Installs the old package in a temporary folder, uses it once (so it has user data), serves the new package from
a local feed shaped like GitHub's "latest release" API, runs `--update-now`, and checks that:
- the installed app now reports the new version,
- user data survived the update,
- the backend still works through the updated app (CLI forwarding, bundled mods),
- a second update check finds nothing newer.
"""

import argparse
import hashlib
import http.server
import json
import os
import shutil
import stat
import subprocess
import sys
import tempfile
import threading
import time
import zipfile
from functools import partial
from pathlib import Path

UP_TO_DATE = 3


def log(message: str) -> None:
    print(f"[e2e] {message}", flush=True)


class Install:
    "An installed copy of a release package and how to run it."

    def __init__(self, package: Path, root: Path):
        self.root = root
        root.mkdir(parents=True, exist_ok=True)
        self.env = {**os.environ, "MINIFY_NONINTERACTIVE": "1"}
        if package.name.endswith(".AppImage"):
            self.path = root / "Minify.AppImage"
            shutil.copy2(package, self.path)
            self.path.chmod(self.path.stat().st_mode | stat.S_IEXEC)
            # No FUSE in CI containers; user data goes to an isolated XDG folder. APPIMAGE is what the runtime
            # sets to the AppImage's own path; set here too in case extract-and-run mode doesn't.
            self.env["APPIMAGE_EXTRACT_AND_RUN"] = "1"
            self.env["APPIMAGE"] = str(self.path)
            self.env["XDG_DATA_HOME"] = str(root / "xdg")
            self.data_home = root / "xdg" / "dota2-minify"
        else:
            app = root / "Minify"
            with zipfile.ZipFile(package) as archive:
                archive.extractall(app)
            self.path = app / "Minify.exe"
            self.data_home = app / "data"

    def run(self, *args: str, timeout: int = 300) -> subprocess.CompletedProcess:
        result = subprocess.run(
            [str(self.path), *args], env=self.env, capture_output=True, text=True, timeout=timeout, cwd=self.root
        )
        log(f"$ Minify {' '.join(args)} -> {result.returncode}")
        for line in (result.stdout + result.stderr).strip().splitlines()[-8:]:
            log(f"    {line}")
        return result

    def app_version(self) -> str:
        return self.run("--app-version").stdout.strip()


def serve(directory: Path) -> tuple[http.server.ThreadingHTTPServer, str]:
    handler = partial(http.server.SimpleHTTPRequestHandler, directory=str(directory))
    server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), handler)
    threading.Thread(target=server.serve_forever, daemon=True).start()
    return server, f"http://127.0.0.1:{server.server_port}"


def publish(feed_dir: Path, package: Path, version: str, base_url: str) -> str:
    "Copies the package next to a latest.json like GitHub's API returns, and returns the feed URL."
    feed_dir.mkdir(parents=True, exist_ok=True)
    shutil.copy2(package, feed_dir / package.name)
    digest = hashlib.sha256((feed_dir / package.name).read_bytes()).hexdigest()
    release = {
        "tag_name": f"v{version}",
        "name": f"Minify {version}",
        "body": "- End-to-end update test",
        "html_url": f"{base_url}/release",
        "assets": [
            {
                "name": package.name,
                "browser_download_url": f"{base_url}/{package.name}",
                "size": package.stat().st_size,
                "digest": f"sha256:{digest}",
            }
        ],
    }
    (feed_dir / "latest.json").write_text(json.dumps(release), encoding="utf-8")
    return f"{base_url}/latest.json"


def check(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)
    log(f"ok: {message}")


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--old", type=Path, required=True)
    parser.add_argument("--old-version", required=True)
    parser.add_argument("--new", type=Path, required=True)
    parser.add_argument("--new-version", required=True)
    args = parser.parse_args(argv)

    root = Path(tempfile.mkdtemp(prefix="minify-e2e-"))
    server = None
    try:
        install = Install(args.old, root / "install")
        check(install.app_version() == args.old_version, f"old app reports {args.old_version}")

        # Use it once: seeds the data folder; then leave something of the user's in it.
        check(install.run("mods", "--json").returncode == 0, "old app lists mods")
        marker = install.data_home / "config" / "e2e-user-file.txt"
        marker.parent.mkdir(parents=True, exist_ok=True)
        marker.write_text("keep me", encoding="utf-8")

        server, base_url = serve(root / "feed")
        feed = publish(root / "feed", args.new, args.new_version, base_url)

        result = install.run("--update-now", "--no-restart", "--feed", feed)
        check(result.returncode == 0, "--update-now succeeded")

        # On Windows the new version replaces the folder after the old process exits.
        deadline = time.monotonic() + 120
        version = install.app_version()
        while version != args.new_version and time.monotonic() < deadline:
            time.sleep(2)
            version = install.app_version()
        check(version == args.new_version, f"installed app now reports {args.new_version}")

        check(marker.read_text(encoding="utf-8") == "keep me", "user data survived the update")
        backend = install.run("--version")
        check(backend.returncode == 0 and backend.stdout.strip() != "", "backend runs through the updated app")
        mods = install.run("mods", "--json")
        check(mods.returncode == 0 and "Dark Terrain" in mods.stdout, "updated app lists the bundled mods")
        again = install.run("--update-now", "--no-restart", "--feed", feed)
        check(again.returncode == UP_TO_DATE, "a second check finds nothing newer")
        log("passed")
        return 0
    except (AssertionError, subprocess.TimeoutExpired) as error:
        log(f"FAILED: {error}")
        return 1
    finally:
        if server:
            server.shutdown()
        shutil.rmtree(root, ignore_errors=True)


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
