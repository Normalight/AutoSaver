"""Save scheduler for AutoSaver.

Manages per-program save timers. Each enabled program gets a timer
that fires at its configured interval, calling the saver to send Ctrl+S.
Signals back to UI the last save timestamp and window count.
"""

import logging
import time
from datetime import datetime

from PySide6.QtCore import QThread, Signal

from core import saver as saver_

logger = logging.getLogger(__name__)


class SaveScheduler(QThread):
    save_done = Signal(str, str, int)  # program_id, timestamp, window_count

    def __init__(self, parent=None):
        super().__init__(parent)
        self._programs: dict[str, dict] = {}
        self._timers: dict[str, float] = {}
        self._running_programs: set[str] = set()
        self._active = True
        self._shutdown = False

    def add_program(self, program: dict) -> None:
        pid = program["id"]
        self._programs[pid] = program
        self._timers[pid] = time.time()

    def remove_program(self, program_id: str) -> None:
        self._programs.pop(program_id, None)
        self._timers.pop(program_id, None)
        self._running_programs.discard(program_id)

    def update_program(self, program: dict) -> None:
        pid = program["id"]
        self._programs[pid] = program
        if pid not in self._timers:
            self._timers[pid] = time.time()

    def set_running(self, program_id: str, running: bool) -> None:
        if running:
            self._running_programs.add(program_id)
            self._timers[program_id] = time.time()
        else:
            self._running_programs.discard(program_id)

    def pause_all(self) -> None:
        self._active = False
        logger.info("All save schedulers paused")

    def resume_all(self) -> None:
        self._active = True
        for pid in self._running_programs:
            self._timers[pid] = time.time()
        logger.info("All save schedulers resumed")

    @property
    def is_paused(self) -> bool:
        return not self._active

    def stop_scheduler(self) -> None:
        self._shutdown = True

    def run(self) -> None:
        while not self._shutdown:
            if self._active:
                now = time.time()
                for pid, prog in list(self._programs.items()):
                    if pid not in self._running_programs:
                        continue
                    interval = prog.get("save_interval_sec", 300)
                    if now - self._timers.get(pid, now) >= interval:
                        self._timers[pid] = now
                        self._do_save(prog)
            time.sleep(1)

    def _do_save(self, prog: dict) -> None:
        exe = prog.get("exe", "")
        try:
            hwnds = saver_.get_windows_by_exe(exe)
            for hwnd in hwnds:
                saver_.send_ctrl_s(hwnd)
            timestamp = datetime.now().strftime("%H:%M:%S")
            logger.info(
                "Saved %s (%s): %d window(s) at %s",
                prog.get("name", prog["id"]),
                exe,
                len(hwnds),
                timestamp,
            )
            self.save_done.emit(prog["id"], timestamp, len(hwnds))
        except Exception:
            logger.exception("Save failed for %s", prog.get("name", prog["id"]))
