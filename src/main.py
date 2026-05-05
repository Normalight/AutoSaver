"""AutoSaver entry point.

Sets up logging, QApplication, system tray, main window, and wires
ProcessMonitor + SaveScheduler threads to the UI via Qt signals.
"""

import logging
import sys
from logging.handlers import RotatingFileHandler

from PySide6.QtWidgets import QApplication

from core.config import get_config_path, load_config, save_config
from core.monitor import ProcessMonitor
from core.scheduler import SaveScheduler
from ui.main_window import MainWindow
from ui.tray import SystemTray


def setup_logging() -> None:
    log_path = get_config_path().parent / "autosaver.log"
    handler = RotatingFileHandler(
        str(log_path), maxBytes=1_000_000, backupCount=1, encoding="utf-8"
    )
    handler.setFormatter(
        logging.Formatter(
            "%(asctime)s [%(levelname)s] %(name)s: %(message)s",
            datefmt="%Y-%m-%d %H:%M:%S",
        )
    )
    root = logging.getLogger()
    root.setLevel(logging.INFO)
    root.addHandler(handler)


class App:
    def __init__(self):
        self._config = load_config()
        self._monitor = ProcessMonitor()
        self._scheduler = SaveScheduler()
        self._window = MainWindow(self._config)
        self._tray = SystemTray()
        self._tray.show()

        self._wire_signals()
        self._start_threads()

    def _wire_signals(self) -> None:
        self._tray.show_requested = self._show_window
        self._tray.pause_toggled = self._toggle_pause
        self._tray.quit_requested = self._quit

        self._monitor.status_changed.connect(self._on_status_changed)
        self._scheduler.save_done.connect(self._on_save_done)

        self._window.program_added.connect(self._on_program_added)
        self._window.program_edited.connect(self._on_program_edited)
        self._window.program_deleted.connect(self._on_program_deleted)
        self._window.pause_toggled.connect(self._on_pause_toggled)

    def _start_threads(self) -> None:
        self._monitor.configure(
            self._config.get("programs", []),
            self._config.get("global", {}).get("check_interval_sec", 3),
        )
        for prog in self._config.get("programs", []):
            if prog.get("enabled", True):
                self._scheduler.add_program(prog)
        self._monitor.start()
        self._scheduler.start()

    def _show_window(self) -> None:
        self._window.show()
        self._window.raise_()
        self._window.activateWindow()

    def _toggle_pause(self) -> None:
        if self._scheduler.is_paused:
            self._scheduler.resume_all()
            self._tray.set_paused_state(False)
            self._window.set_paused_state(False)
        else:
            self._scheduler.pause_all()
            self._tray.set_paused_state(True)
            self._window.set_paused_state(True)

    def _quit(self) -> None:
        self._scheduler.stop_scheduler()
        self._monitor.stop_monitor()
        self._scheduler.wait(3000)
        self._monitor.wait(3000)
        QApplication.quit()

    def _on_status_changed(self, program_id: str, running: bool) -> None:
        self._window.update_program_status(program_id, running)
        self._scheduler.set_running(program_id, running)

    def _on_save_done(self, program_id: str, timestamp: str, window_count: int) -> None:
        self._window.update_last_save(program_id, timestamp, window_count)

    def _on_program_added(self, prog: dict) -> None:
        self._scheduler.add_program(prog)
        self._save_config()

    def _on_program_edited(self, prog: dict) -> None:
        self._scheduler.update_program(prog)
        self._save_config()

    def _on_program_deleted(self, program_id: str) -> None:
        self._scheduler.remove_program(program_id)
        self._save_config()

    def _on_pause_toggled(self, paused: bool) -> None:
        if paused:
            self._scheduler.pause_all()
            self._tray.set_paused_state(True)
        else:
            self._scheduler.resume_all()
            self._tray.set_paused_state(False)

    def _save_config(self) -> None:
        save_config(self._config)


def main() -> None:
    setup_logging()
    logging.info("AutoSaver starting")

    app = QApplication(sys.argv)
    app.setQuitOnLastWindowClosed(False)

    auto_saver = App()

    try:
        sys.exit(app.exec())
    finally:
        save_config(auto_saver._config)
        logging.info("AutoSaver exiting")


if __name__ == "__main__":
    main()
