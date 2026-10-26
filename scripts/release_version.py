"""Release versions and notes for the desktop app.

Versions are 4-part, like .NET assembly versions: major.minor.build.revision. Every release adds one to the
revision; the last three parts count 0-9 and carry over like an odometer:
1.0.0.1, ..., 1.0.0.9, 1.0.1.0, ..., 1.0.9.9, 1.1.0.0, ..., 1.9.9.9, 2.0.0.0.

    python scripts/release_version.py next            -> next version from the latest v* tag
    python scripts/release_version.py notes [--base]  -> Markdown list of commits since the latest v* tag
"""

import argparse
import re
import subprocess
import sys

FIRST_VERSION = "1.0.0.1"
TAG_PREFIX = "v"
_VERSION = re.compile(r"^v?(\d+)\.(\d+)\.(\d+)\.(\d+)$")


def parse(version: str) -> tuple[int, int, int, int]:
    match = _VERSION.match(version.strip())
    if not match:
        raise ValueError(f"Not a 4-part version: {version!r}")
    major, minor, build, revision = (int(p) for p in match.groups())
    if any(p > 9 for p in (minor, build, revision)):
        raise ValueError(f"Minor, build and revision must be 0-9: {version!r}")
    return major, minor, build, revision


def bump(version: str | None) -> str:
    """The version after `version`, or the first version when there is none."""
    if not version:
        return FIRST_VERSION
    major, minor, build, revision = parse(version)
    counter = (minor * 100 + build * 10 + revision) + 1
    major += counter // 1000
    counter %= 1000
    return f"{major}.{counter // 100}.{counter // 10 % 10}.{counter % 10}"


def latest_tag(tags: list[str]) -> str | None:
    "The highest v* release tag; other tags (e.g. upstream's) are ignored."
    versions = []
    for tag in tags:
        try:
            versions.append((parse(tag), tag))
        except ValueError:
            continue
    return max(versions)[1] if versions else None


def _git(*args: str, cwd: str | None = None) -> str:
    return subprocess.run(["git", *args], capture_output=True, text=True, check=True, cwd=cwd).stdout


def release_notes(previous: str | None, base: str | None = None, head: str = "HEAD", cwd: str | None = None) -> str:
    """Markdown list of commits after `previous` (or after `base` for the first release), newest first."""
    since = previous or base
    revision_range = f"{since}..{head}" if since else head
    log = _git("log", "--no-merges", "--format=%h%x1f%s%x1f%an", revision_range, cwd=cwd)
    lines = []
    for entry in log.splitlines():
        short_hash, subject, author = entry.split("\x1f")
        lines.append(f"- {subject} ({short_hash}, {author})")
    header = f"Changes since {previous}:" if previous else "Changes:"
    return "\n".join([header, "", *lines]) if lines else f"{header}\n\nNo changes."


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)
    sub.add_parser("next", help="print the next release version")
    notes = sub.add_parser("notes", help="print release notes since the latest release")
    notes.add_argument("--base", help="first release only: list commits after this commit")
    args = parser.parse_args(argv)

    previous = latest_tag(_git("tag", "--list", f"{TAG_PREFIX}*").split())
    if args.command == "next":
        print(bump(previous))
    else:
        print(release_notes(previous, args.base))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
