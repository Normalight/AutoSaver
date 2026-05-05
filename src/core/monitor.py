"""Process monitor thread for AutoSaver.

Polls running processes at configurable intervals and emits Qt signals
when a target program's running status changes.
"""

import logging
import time

import psutil
from PySide6.QtCore import QThread, Signal

logger = logging.getLogger(__name__)


class ProcessMonitor(QThread):
    status_changed = Signal(str, bool)  # program_id, running

    def __init__(self, parent=None):
        super().__init__(parent)
        self._programs: list[dict] = []
        self._interval = 3
        self._running = False
        self._prev_state: dict[str, bool] = {}

    def configure(self, programs: list[dict], interval_sec: int) -> None:
        self._programs = programs
        self._interval = interval_sec

    def stop_monitor(self) -> None:
        self._running = False

    def run(self) -> None:
        self._running = True
        while self._running:
            self._poll()
            time.sleep(self._interval)

    def _poll(self) -> None:
        try:
            running_exes: set[str] = set()
            for proc in psutil.process_iter(["name"]):
                try:
                    name = proc.info["name"]
                    if name:
                        running_exes.add(name.lower())
                except (psutil.NoSuchProcess, psutil.AccessDenied):
                    continue
        except Exception:
            logger.exception("Process enumeration failed")
            return

        for prog in self._programs:
            if not prog.get("enabled", True):
                continue
            pid = prog["id"]
            exe = prog.get("exe", "").lower()
            is_running = exe in running_exes
            prev = self._prev_state.get(pid)
            if prev != is_running:
                self._prev_state[pid] = is_running
                logger.info(
                    "Program %s (%s) status: %s",
                    prog.get("name", pid),
                    prog.get("exe", ""),
                    "running" if is_running else "stopped",
                )
                self.status_changed.emit(pid, is_running)
