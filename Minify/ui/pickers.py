"""File dialogs, shown by the desktop app. Without it (plain CLI) there is nothing to pick with."""

import os

from ui import bridge


def pick_file(title="Select File", file_types=(), initial_dir=None):
    """Open a dialog to select a single file."""
    if not bridge.active:
        return None
    return bridge.active.request(
        "pick_file", title=title, file_types=list(file_types), initial_dir=initial_dir or os.getcwd()
    )


def pick_files(title="Select Files", file_types=(), initial_dir=None):
    """Open a dialog to select multiple files."""
    if not bridge.active:
        return []
    return (
        bridge.active.request(
            "pick_files", title=title, file_types=list(file_types), initial_dir=initial_dir or os.getcwd()
        )
        or []
    )


def pick_folder(title="Select Folder", initial_dir=None):
    """Open a dialog to select a directory/folder."""
    if not bridge.active:
        return None
    return bridge.active.request("pick_folder", title=title, initial_dir=initial_dir or os.getcwd())


def pick_save_file(title="Save File", default_filename="", file_types=(), initial_dir=None):
    """Open a dialog to choose a file save location."""
    if not bridge.active:
        return None
    return bridge.active.request(
        "pick_save_file",
        title=title,
        default_filename=default_filename,
        file_types=list(file_types),
        initial_dir=initial_dir or os.getcwd(),
    )
