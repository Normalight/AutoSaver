"""Window enumeration and keystroke injection for AutoSaver.

Uses pywin32 to find visible top-level windows belonging to a target process
and send Ctrl+S via PostMessage (no focus stealing).
"""

import logging

import psutil
import win32con
import win32gui
import win32process

logger = logging.getLogger(__name__)


def get_windows_by_exe(exe_name: str) -> list[int]:
    """Return HWNDs of all visible top-level windows for the given exe name."""
    exe_lower = exe_name.lower()
    pids = set()
    for proc in psutil.process_iter(["pid", "name"]):
        try:
            if proc.info["name"] and proc.info["name"].lower() == exe_lower:
                pids.add(proc.info["pid"])
        except (psutil.NoSuchProcess, psutil.AccessDenied):
            continue

    if not pids:
        return []

    hwnds = []

    def callback(hwnd: int, _):
        if not win32gui.IsWindowVisible(hwnd):
            return True
        if not win32gui.IsWindowEnabled(hwnd):
            return True
        _, found_pid = win32process.GetWindowThreadProcessId(hwnd)
        if found_pid in pids:
            hwnds.append(hwnd)
        return True

    win32gui.EnumWindows(callback, None)
    return hwnds


def send_ctrl_s(hwnd: int) -> bool:
    """Send Ctrl+S to the target window via PostMessage. Returns True on success."""
    try:
        win32gui.PostMessage(hwnd, win32con.WM_KEYDOWN, win32con.VK_CONTROL, 0)
        win32gui.PostMessage(hwnd, win32con.WM_KEYDOWN, ord("S"), 0)
        win32gui.PostMessage(hwnd, win32con.WM_KEYUP, ord("S"), 0)
        win32gui.PostMessage(hwnd, win32con.WM_KEYUP, win32con.VK_CONTROL, 0)
        return True
    except Exception:
        logger.exception("Failed to send Ctrl+S to hwnd %d", hwnd)
        return False


def get_window_title(hwnd: int) -> str:
    """Get window title for logging/display purposes."""
    try:
        return win32gui.GetWindowText(hwnd)
    except Exception:
        return ""
