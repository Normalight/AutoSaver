"""System tray icon and context menu for AutoSaver."""

import logging
from pathlib import Path

from PySide6.QtGui import QAction, QIcon
from PySide6.QtWidgets import QApplication, QMenu, QSystemTrayIcon

logger = logging.getLogger(__name__)


def _get_icon_path() -> str:
    base = Path(__file__).resolve().parent.parent.parent
    return str(base / "assets" / "icon.ico")


class SystemTray(QSystemTrayIcon):
    def __init__(self, parent=None):
        icon_path = _get_icon_path()
        icon = QIcon(icon_path) if Path(icon_path).exists() else QIcon()
        super().__init__(icon, parent)
        self.setToolTip("AutoSaver")

        menu = QMenu()

        self._show_action = QAction("显示主窗口")
        self._show_action.triggered.connect(self._on_show)
        menu.addAction(self._show_action)

        self._pause_action = QAction("暂停所有")
        self._pause_action.triggered.connect(self._on_toggle_pause)
        menu.addAction(self._pause_action)

        menu.addSeparator()

        quit_action = QAction("退出")
        quit_action.triggered.connect(self._on_quit)
        menu.addAction(quit_action)

        self.setContextMenu(menu)
        self.activated.connect(self._on_activated)

        self._paused = False
        self.show_requested = None
        self.pause_toggled = None
        self.quit_requested = None

    def _on_show(self) -> None:
        if self.show_requested:
            self.show_requested()

    def _on_toggle_pause(self) -> None:
        if self.pause_toggled:
            self.pause_toggled()

    def _on_quit(self) -> None:
        if self.quit_requested:
            self.quit_requested()

    def _on_activated(self, reason: QSystemTrayIcon.ActivationReason) -> None:
        if reason == QSystemTrayIcon.DoubleClick:
            self._on_show()

    def set_paused_state(self, paused: bool) -> None:
        self._paused = paused
        self._pause_action.setText("恢复所有" if paused else "暂停所有")
