import subprocess

import pytest
import release_version as rv


@pytest.mark.parametrize(
    "previous, expected",
    [
        (None, "1.0.0.1"),
        ("1.0.0.1", "1.0.0.2"),
        ("1.0.0.9", "1.0.1.0"),
        ("1.0.1.9", "1.0.2.0"),
        ("1.0.9.9", "1.1.0.0"),
        ("1.9.9.9", "2.0.0.0"),
        ("v2.3.4.5", "2.3.4.6"),
        ("12.9.9.9", "13.0.0.0"),
    ],
)
def test_bump_counts_like_an_odometer(previous, expected):
    assert rv.bump(previous) == expected


def test_every_release_is_one_step_and_strictly_increasing():
    version = None
    seen = []
    for _ in range(1205):
        version = rv.bump(version)
        seen.append(rv.parse(version))
    assert seen == sorted(seen)
    assert len(set(seen)) == len(seen)
    assert version == "2.2.0.5"


@pytest.mark.parametrize("bad", ["1.0.0", "1.0.0.10", "1.10.0.0", "v1.0.0.x", "2rc9"])
def test_rejects_versions_outside_the_scheme(bad):
    with pytest.raises(ValueError):
        rv.parse(bad)


def test_latest_tag_ignores_upstream_tags_and_sorts_numerically():
    tags = ["Minify-v2.0", "v1.0.0.9", "v1.0.1.0", "v1.0.0.10", "2rc9", "v0.9.9.9"]
    assert rv.latest_tag(tags) == "v1.0.1.0"
    assert rv.latest_tag(["Minify-v2.0"]) is None


def _git(repo, *args):
    subprocess.run(["git", *args], cwd=repo, check=True, capture_output=True)


@pytest.fixture
def repo(tmp_path):
    _git(tmp_path, "init", "-q", "-b", "main")
    _git(tmp_path, "config", "user.name", "Tester")
    _git(tmp_path, "config", "user.email", "tester@example.com")
    _git(tmp_path, "config", "commit.gpgsign", "false")

    def commit(message):
        _git(tmp_path, "commit", "-q", "--allow-empty", "-m", message)

    return tmp_path, commit


def test_release_notes_list_commits_since_the_previous_release(repo):
    path, commit = repo
    commit("Upstream work")
    commit("Port settings")
    _git(path, "tag", "v1.0.0.1")
    commit("Add updater")
    commit("Fix AppImage launch")

    notes = rv.release_notes("v1.0.0.1", cwd=str(path))

    lines = notes.splitlines()
    assert lines[0] == "Changes since v1.0.0.1:"
    assert lines[2].startswith("- Fix AppImage launch (") and lines[2].endswith(", Tester)")
    assert lines[3].startswith("- Add updater (")
    assert len(lines) == 4


def test_first_release_notes_start_after_the_base_commit(repo):
    path, commit = repo
    commit("Upstream work")
    base = subprocess.run(["git", "rev-parse", "HEAD"], cwd=path, capture_output=True, text=True).stdout.strip()
    commit("Add Avalonia app")

    notes = rv.release_notes(None, base=base, cwd=str(path))

    assert notes.splitlines()[0] == "Changes:"
    assert "Add Avalonia app" in notes
    assert "Upstream work" not in notes


def test_release_notes_without_new_commits(repo):
    path, commit = repo
    commit("Only commit")
    _git(path, "tag", "v1.0.0.1")

    assert rv.release_notes("v1.0.0.1", cwd=str(path)).endswith("No changes.")
