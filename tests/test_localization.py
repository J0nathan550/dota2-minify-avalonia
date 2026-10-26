import json
import os
import subprocess
import sys

ROOT_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))


def get_git_tracked_source_files(root_dir):
    """Backend and desktop app sources; the locale files themselves don't count as usage."""
    result = subprocess.run(
        ["git", "ls-files", "Minify", "desktop"], capture_output=True, text=True, check=True, cwd=root_dir
    )
    files = [f for f in result.stdout.splitlines() if "/locales/" not in f]
    return [os.path.join(root_dir, f) for f in files if f.endswith((".py", ".js", ".json", ".cs", ".axaml"))]


def find_unused_keys(root_dir):
    with open(os.path.join(root_dir, "Minify", "locales", "en.json"), "r", encoding="utf-8") as f:
        keys = list(json.load(f).keys())

    contents = []
    for filepath in get_git_tracked_source_files(root_dir):
        with open(filepath, "r", encoding="utf-8", errors="replace") as f:
            contents.append(f.read())

    def is_used(key):
        # Python: "&key" / "key"; desktop app: Loc["key"] in C#, Loc[key] in XAML.
        patterns = (f"&{key}", f'"{key}"', f"'{key}'", f"Loc[{key}]")
        if "start_text_" in key and any("start_text_{i}_var" in c for c in contents):
            return True
        return any(p in c for c in contents for p in patterns)

    return [key for key in keys if not is_used(key)]


# Translated update-dialog strings from the web UI, reused by the desktop app's updater.
PENDING_UPDATER_KEYS = {
    "button_ignore_version",
    "button_update_now",
    "label_current_version",
    "label_new_version",
    "label_release_notes",
    "modal_update_message",
    "msg_download_complete_launching",
    "status_closing",
    "status_downloading",
    "status_launching_installer",
    "title_update_available",
}


def test_unused_localization_keys():
    unused_keys = [k for k in find_unused_keys(ROOT_DIR) if k not in PENDING_UPDATER_KEYS]
    assert len(unused_keys) == 0, f"Unused localization keys found: {unused_keys}"


def test_get_for_locale():
    sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "../Minify")))
    from core import localization

    en_dict = localization.get_for_locale("EN")
    assert isinstance(en_dict, dict)
    assert len(en_dict) > 0
    assert "close_dota_terminal" in en_dict


if __name__ == "__main__":
    unused = find_unused_keys(ROOT_DIR)
    if unused:
        print("Unused localization keys:")
        for k in sorted(unused):
            print(f"  - {k}")
        sys.exit(1)
    print("All localization keys are used.")
