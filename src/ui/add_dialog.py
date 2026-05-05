"""Add/Edit program dialog for AutoSaver."""

import uuid

import psutil
from PySide6.QtWidgets import (
    QCheckBox,
    QComboBox,
    QDialog,
    QDialogButtonBox,
    QFormLayout,
    QLineEdit,
    QPushButton,
    QSpinBox,
    QVBoxLayout,
)

EXE_BLACKLIST = {
    "system idle process", "system", "svchost.exe", "csrss.exe",
    "smss.exe", "wininit.exe", "services.exe", "lsass.exe",
    "winlogon.exe", "explorer.exe", "taskmgr.exe", "autosaver.exe",
}


def _get_running_exes() -> list[str]:
    exes: set[str] = set()
    for proc in psutil.process_iter(["name"]):
        try:
            name = proc.info["name"]
            if name and name.lower() not in EXE_BLACKLIST:
                exes.add(name)
        except (psutil.NoSuchProcess, psutil.AccessDenied):
            continue
    return sorted(exes, key=str.lower)


class AddEditDialog(QDialog):
    def __init__(self, parent=None, program: dict | None = None):
        super().__init__(parent)
        self._program = program
        self._editing = program is not None
        self.setWindowTitle("编辑程序" if self._editing else "添加程序")
        self.setMinimumWidth(380)
        self._setup_ui()
        if program:
            self._load_program(program)

    def _setup_ui(self) -> None:
        layout = QVBoxLayout(self)
        form = QFormLayout()

        self._name_edit = QLineEdit()
        self._name_edit.setPlaceholderText("例如: SAI2")
        form.addRow("显示名称:", self._name_edit)

        exe_layout = QVBoxLayout()
        self._exe_edit = QLineEdit()
        self._exe_edit.setPlaceholderText("例如: sai2.exe")
        exe_layout.addWidget(self._exe_edit)

        picker_layout = QVBoxLayout()
        self._process_picker = QComboBox()
        self._process_picker.addItem("-- 从运行中选择 --")
        self._process_picker.addItems(_get_running_exes())
        self._process_picker.currentTextChanged.connect(self._on_process_picked)
        picker_layout.addWidget(self._process_picker)

        refresh_btn = QPushButton("刷新进程列表")
        refresh_btn.clicked.connect(self._refresh_processes)
        picker_layout.addWidget(refresh_btn)

        exe_layout.addLayout(picker_layout)
        form.addRow("进程名:", exe_layout)

        self._interval_spin = QSpinBox()
        self._interval_spin.setRange(1, 3600)
        self._interval_spin.setValue(5)
        self._interval_spin.setSuffix(" 分钟")
        form.addRow("保存间隔:", self._interval_spin)

        self._enabled_check = QCheckBox("启用")
        self._enabled_check.setChecked(True)
        form.addRow("", self._enabled_check)

        layout.addLayout(form)

        buttons = QDialogButtonBox(QDialogButtonBox.Ok | QDialogButtonBox.Cancel)
        buttons.accepted.connect(self.accept)
        buttons.rejected.connect(self.reject)
        layout.addWidget(buttons)

    def _load_program(self, prog: dict) -> None:
        self._name_edit.setText(prog.get("name", ""))
        self._exe_edit.setText(prog.get("exe", ""))
        self._interval_spin.setValue(prog.get("save_interval_sec", 300) // 60)
        self._enabled_check.setChecked(prog.get("enabled", True))

    def _on_process_picked(self, text: str) -> None:
        if text and text != "-- 从运行中选择 --":
            self._exe_edit.setText(text)

    def _refresh_processes(self) -> None:
        current = self._process_picker.currentText()
        self._process_picker.clear()
        self._process_picker.addItem("-- 从运行中选择 --")
        self._process_picker.addItems(_get_running_exes())
        idx = self._process_picker.findText(current)
        if idx >= 0:
            self._process_picker.setCurrentIndex(idx)

    def get_program(self) -> dict:
        if self._program:
            prog = self._program.copy()
        else:
            prog = {"id": str(uuid.uuid4())}
        prog["name"] = self._name_edit.text().strip()
        prog["exe"] = self._exe_edit.text().strip()
        prog["save_interval_sec"] = self._interval_spin.value() * 60
        prog["enabled"] = self._enabled_check.isChecked()
        return prog
