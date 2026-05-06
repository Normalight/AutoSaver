"""Main window for AutoSaver — program list, status, and controls."""

import logging
import sys

import winreg
from PySide6.QtCore import Qt, Signal
from PySide6.QtWidgets import (
    QCheckBox,
    QHBoxLayout,
    QHeaderView,
    QLabel,
    QMainWindow,
    QPushButton,
    QStatusBar,
    QTableWidget,
    QTableWidgetItem,
    QVBoxLayout,
    QWidget,
)

from ui.add_dialog import AddEditDialog

logger = logging.getLogger(__name__)

REG_PATH = r"Software\Microsoft\Windows\CurrentVersion\Run"
REG_KEY = "AutoSaver"


class MainWindow(QMainWindow):
    program_added = Signal(dict)
    program_edited = Signal(dict)
    program_deleted = Signal(str)
    pause_toggled = Signal(bool)

    def __init__(self, config: dict, parent=None):
        super().__init__(parent)
        self._config = config
        self._paused = False
        self._last_save_info: dict[str, tuple[str, int]] = {}
        self.setWindowTitle("AutoSaver")
        self.setFixedWidth(520)
        self.setMinimumHeight(300)
        self._setup_ui()
        self._refresh_table()

    def _setup_ui(self) -> None:
        central = QWidget()
        self.setCentralWidget(central)
        layout = QVBoxLayout(central)

        self._table = QTableWidget(0, 4)
        self._table.setHorizontalHeaderLabels(["程序名", "状态", "间隔", "操作"])
        header = self._table.horizontalHeader()
        header.setSectionResizeMode(0, QHeaderView.Stretch)
        header.setSectionResizeMode(1, QHeaderView.Fixed)
        header.setSectionResizeMode(2, QHeaderView.Fixed)
        header.setSectionResizeMode(3, QHeaderView.Fixed)
        self._table.setColumnWidth(1, 100)
        self._table.setColumnWidth(2, 80)
        self._table.setColumnWidth(3, 140)
        self._table.setSelectionBehavior(QTableWidget.SelectRows)
        self._table.verticalHeader().setVisible(False)
        layout.addWidget(self._table)

        btn_layout = QHBoxLayout()
        self._add_btn = QPushButton("+ 添加程序")
        self._add_btn.clicked.connect(self._on_add)
        btn_layout.addWidget(self._add_btn)
        btn_layout.addStretch()
        self._pause_btn = QPushButton("全部暂停")
        self._pause_btn.clicked.connect(self._on_toggle_pause)
        btn_layout.addWidget(self._pause_btn)
        layout.addLayout(btn_layout)

        status = QStatusBar()
        self._startup_check = QCheckBox("开机自启")
        self._startup_check.setChecked(self._get_startup_registry())
        self._startup_check.stateChanged.connect(self._on_startup_changed)
        status.addPermanentWidget(self._startup_check)
        self._status_label = QLabel("")
        status.addWidget(self._status_label, 1)
        self.setStatusBar(status)

    def _refresh_table(self) -> None:
        programs = self._config.get("programs", [])
        self._table.setRowCount(len(programs))
        for row, prog in enumerate(programs):
            name_item = QTableWidgetItem(prog.get("name", ""))
            name_item.setData(Qt.UserRole, prog["id"])
            self._table.setItem(row, 0, name_item)

            status_text = self._get_status_text(prog["id"])
            self._table.setItem(row, 1, QTableWidgetItem(status_text))

            interval_min = prog.get("save_interval_sec", 300) // 60
            self._table.setItem(row, 2, QTableWidgetItem(f"{interval_min} 分钟"))

            action_widget = QWidget()
            action_layout = QHBoxLayout(action_widget)
            action_layout.setContentsMargins(4, 2, 4, 2)
            edit_btn = QPushButton("编辑")
            del_btn = QPushButton("删除")
            edit_btn.clicked.connect(lambda checked, p=prog: self._on_edit(p))
            del_btn.clicked.connect(lambda checked, pid=prog["id"]: self._on_delete(pid))
            action_layout.addWidget(edit_btn)
            action_layout.addWidget(del_btn)
            self._table.setCellWidget(row, 3, action_widget)

        self._update_status_bar()

    def _get_status_text(self, program_id: str) -> str:
        return "○ 未检测到"

    def _on_add(self) -> None:
        dlg = AddEditDialog(self)
        if dlg.exec():
            prog = dlg.get_program()
            self._config.setdefault("programs", []).append(prog)
            self._refresh_table()
            self.program_added.emit(prog)

    def _on_edit(self, prog: dict) -> None:
        dlg = AddEditDialog(self, prog)
        if dlg.exec():
            updated = dlg.get_program()
            programs = self._config["programs"]
            for i, p in enumerate(programs):
                if p["id"] == updated["id"]:
                    programs[i] = updated
                    break
            self._refresh_table()
            self.program_edited.emit(updated)

    def _on_delete(self, program_id: str) -> None:
        self._config["programs"] = [
            p for p in self._config.get("programs", []) if p["id"] != program_id
        ]
        self._last_save_info = {
            k: v for k, v in self._last_save_info.items()
            if k != self._find_program_name(program_id)
        }
        self._refresh_table()
        self.program_deleted.emit(program_id)

    def _find_program_name(self, program_id: str) -> str:
        for prog in self._config.get("programs", []):
            if prog["id"] == program_id:
                return prog.get("name", "")
        return ""

    def _on_toggle_pause(self) -> None:
        self._paused = not self._paused
        self._pause_btn.setText("全部恢复" if self._paused else "全部暂停")
        self.pause_toggled.emit(self._paused)

    def _on_startup_changed(self, state: int) -> None:
        enabled = state == Qt.Checked.value
        try:
            key = winreg.OpenKey(
                winreg.HKEY_CURRENT_USER, REG_PATH, 0, winreg.KEY_SET_VALUE
            )
            if enabled:
                exe_path = sys.executable
                winreg.SetValueEx(key, REG_KEY, 0, winreg.REG_SZ, exe_path)
            else:
                try:
                    winreg.DeleteValue(key, REG_KEY)
                except FileNotFoundError:
                    pass
            winreg.CloseKey(key)
        except OSError:
            logger.exception("Failed to update startup registry")
            self._startup_check.setChecked(not enabled)

    def _get_startup_registry(self) -> bool:
        try:
            key = winreg.OpenKey(
                winreg.HKEY_CURRENT_USER, REG_PATH, 0, winreg.KEY_READ
            )
            try:
                winreg.QueryValueEx(key, REG_KEY)
                winreg.CloseKey(key)
                return True
            except FileNotFoundError:
                winreg.CloseKey(key)
                return False
        except OSError:
            return False

    def update_program_status(self, program_id: str, running: bool) -> None:
        for row in range(self._table.rowCount()):
            item = self._table.item(row, 0)
            if item and item.data(Qt.UserRole) == program_id:
                self._table.item(row, 1).setText(
                    "● 运行中" if running else "○ 未检测到"
                )
                break

    def update_last_save(self, program_id: str, timestamp: str, window_count: int) -> None:
        for prog in self._config.get("programs", []):
            if prog["id"] == program_id:
                self._last_save_info[prog["name"]] = (timestamp, window_count)
                break
        self._update_status_bar()

    def _update_status_bar(self) -> None:
        if self._last_save_info:
            parts = []
            for name, (ts, count) in self._last_save_info.items():
                parts.append(f"{name} {ts} ({count}窗口)")
            self._status_label.setText("上次保存: " + ", ".join(parts))
        else:
            self._status_label.setText("")

    def get_config(self) -> dict:
        return self._config

    def set_paused_state(self, paused: bool) -> None:
        self._paused = paused
        self._pause_btn.setText("全部恢复" if paused else "全部暂停")

    def closeEvent(self, event) -> None:
        if self._config.get("global", {}).get("minimize_to_tray_on_close", True):
            event.ignore()
            self.hide()
        else:
            event.accept()
