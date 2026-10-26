# Minify Desktop (Avalonia)

Native frontend for Minify. It replaces the pywebview + Svelte UI. The Python code stays as a **backend process** with no GUI libraries in it, so Linux users no longer need WebKitGTK, PyGObject or tkinter.

```
┌──────────────────────────┐   stdin:  {"id":1,"method":"get_mods"}          ┌───────────────────────────┐
│ Minify.Desktop (C#)      │ ────────────────────────────────────────────▶   │ Minify rpc (Python)       │
│ Avalonia, X11 / Wayland  │   stdout: {"id":1,"result":[...]}               │ ui/bridge.py -> ui.Api    │
│ native dialogs           │ ◀────────────────────────────────────────────   │ patcher, mods, plugins    │
└──────────────────────────┘   stdout: {"event":"onLogReceived",...}         └───────────────────────────┘
                               stdout: {"id":"b1","method":"pick_file"}  (backend asks, frontend answers)
```

- The protocol is defined in [`Minify/ui/bridge.py`](../Minify/ui/bridge.py). Every public method of `ui.Api` can be called. Events have the same names as the old `window.on*` callbacks.
- Mod scripts and plugins keep calling `ui.pick_file`, `ui.alert`, etc. In `rpc` mode those calls go to the frontend, which shows native dialogs.
- While in `rpc` mode the backend never prompts on its own. If Steam/Dota isn't found, the app asks for the folder, calls `set_steam_root`, and restarts the backend.
- When the app closes, the backend's stdin closes and it exits, even if the app crashed.

## Running from source

Prerequisites: .NET 10 SDK, plus `uv` for the Python side (see [development docs](../docs/wiki/development.md)).

```sh
uv sync                     # once, from the repo root
dotnet run --project desktop/Minify.Desktop
```

The app finds the backend in this order:

1. `MINIFY_BACKEND`: a frozen backend executable, or a `Minify/` source folder.
2. `backend/Minify[.exe]` next to the app (release layout).
3. The repository's `Minify/` folder, run with `uv run` (falls back to `python3`).

To try only the backend: `printf '{"id":1,"method":"get_mods"}\n' | uv run python Minify rpc`

## Status

Tests: `dotnet test desktop/Minify.Desktop.slnx` (the real-backend test needs `uv sync`).

Done: mods grid with previews, search and refresh; mod details (notes, preview, file trees, code); toggling mods; patch/uninstall (with "remove everything"); live terminal output; download notifications; settings for the app, mods and plugins (every control type, reset, live language and theme switching); CSS themes from `themes/` mapped onto the native UI; About and credits; the Steam-not-found flow; Workshop Tools download/extract prompts; legacy mods import; the D2PFX browser (categories, search, install/update/remove/enable, previews); announcements (from this repository's `docs/announcements.json`); native file/folder/alert dialogs for mod scripts.

Not ported yet from the Svelte UI: the community plugin tab. The app updater is being replaced by a native one.
