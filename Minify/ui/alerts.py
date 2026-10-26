from core import localization, output
from ui import bridge


def alert(text_or_id, *args, msg_type="info", title=None, **kwargs):
    """Display a user-facing alert dialog box / notification in the UI with & localization support."""
    text = text_or_id
    if isinstance(text_or_id, str) and text_or_id.startswith("&"):
        text = localization.localization_dict.get(text_or_id.replace("&", ""), text_or_id)

    if args and isinstance(text, str):
        text = text.format(*args)

    title = title or ("Error" if msg_type == "error" else "Warning" if msg_type == "warning" else "Notice")

    # Always in the terminal/log; as a dialog too when the desktop app is attached.
    output.add_text(text_or_id, *args, msg_type=msg_type, **kwargs)

    if bridge.active:
        bridge.active.request("alert", text=text, title=title, msg_type=msg_type)
