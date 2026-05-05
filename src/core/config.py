"""Configuration management for AutoSaver.

Reads/writes config.json next to the executable. First run auto-creates defaults.
Atomic writes via temp file + rename to prevent corruption.
"""

import json
import logging
import os
import sys
import uuid
from pathlib import Path
from tempfile import NamedTemporaryFile

logger = logging.getLogger(__name__)

DEFAULT_CONFIG = {
    "global": {
        "start_with_windows": False,
        "check_interval_sec": 3,
        "minimize_to_tray_on_close": True,
    },
    "programs": [],
}


def get_config_path() -> Path:
    if getattr(sys, "frozen", False):
        base = Path(sys.executable).parent
    else:
        base = Path(__file__).resolve().parent.parent.parent
    return base / "config.json"


def _deep_merge(default: dict, override: dict) -> dict:
    result = default.copy()
    for key, value in override.items():
        if key in result and isinstance(result[key], dict) and isinstance(value, dict):
            result[key] = _deep_merge(result[key], value)
        else:
            result[key] = value
    return result


def load_config(path: Path | None = None) -> dict:
    if path is None:
        path = get_config_path()

    if not path.exists():
        logger.info("config.json not found, creating with defaults")
        save_config(path, DEFAULT_CONFIG)
        return DEFAULT_CONFIG

    try:
        with open(path, "r", encoding="utf-8") as f:
            data = json.load(f)
        merged = _deep_merge(DEFAULT_CONFIG, data)
        for prog in merged.get("programs", []):
            if "id" not in prog:
                prog["id"] = str(uuid.uuid4())
        return merged
    except (json.JSONDecodeError, OSError) as e:
        logger.error("Failed to parse config.json: %s, using defaults", e)
        return DEFAULT_CONFIG


def save_config(data: dict, path: Path | None = None) -> None:
    if path is None:
        path = get_config_path()

    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = NamedTemporaryFile(
        mode="w", encoding="utf-8", delete=False, dir=path.parent, suffix=".tmp"
    )
    try:
        json.dump(data, tmp, indent=2, ensure_ascii=False)
        tmp.flush()
        os.fsync(tmp.fileno())
        tmp.close()
        os.replace(tmp.name, str(path))
    except Exception:
        tmp.close()
        try:
            os.unlink(tmp.name)
        except OSError:
            pass
        raise
