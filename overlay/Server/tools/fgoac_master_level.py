"""Max Master Level for one local account: raises the Master's EXP to the top of the game's level table.

  python fgoac_master_level.py --aime-id 1 --json

Writes one JSON object to stdout: {"ok": true, "action": "master", "changed": 0 or 1, "backup": path}, or
{"ok": false, "error": code, "message": text}. Loads and saves the profile through Cloud23333's
fgo_account.py beside this file, whichever version is installed.
"""

import argparse
from copy import deepcopy
from datetime import datetime, timezone
import json
from pathlib import Path
import re
import sys

TOOLS_DIR = Path(__file__).resolve().parent
# The packaged Python uses an isolated ._pth and omits the script directory.
if str(TOOLS_DIR) not in sys.path:
    sys.path.insert(0, str(TOOLS_DIR))
SERVER_DIR = TOOLS_DIR.parent
MASTER_EXP_TABLE_PATH = SERVER_DIR / "data" / "fgo-master" / "master_exp_table" / "arms_mst_master_exp_table.bin"


def load_master_level_requirements() -> list:
    """Per-level EXP costs from the cabinet's cumulative upper thresholds; empty when the table cannot be read."""
    try:
        lines = MASTER_EXP_TABLE_PATH.read_text(encoding="utf-8-sig", errors="replace").splitlines()
    except OSError:
        return []
    rows = {}
    for line in lines:
        match = re.match(r"^master_exp_table\.(\d+)\.([^.]+)=(.*)$", line.strip())
        if match is not None:
            rows.setdefault(int(match.group(1)), {})[match.group(2)] = match.group(3)
    entries = []
    for row in rows.values():
        try:
            entries.append((int(row["lv"]), int(row["exp"])))
        except (KeyError, TypeError, ValueError):
            return []
    entries.sort()
    requirements = []
    previous = 0
    for index, (level, upper) in enumerate(entries, start=1):
        if level != index or upper <= previous:
            return []
        requirements.append(upper - previous)
        previous = upper
    return requirements


def max_master_exp(profile: dict, requirements: list) -> int:
    """Sets mstr_exp to the top of the table; 1 when it changed, 0 when it was there already. Never lowers it."""
    total = sum(max(1, int(value)) for value in requirements)
    try:
        current = int(profile.get("mstr_exp", 0))
    except (TypeError, ValueError):
        current = 0
    if current >= total:
        return 0
    profile["mstr_exp"] = total
    return 1


def run(aime_id: int) -> dict:
    import fgo_account as tool

    if tool.server_running():
        return {"ok": False, "error": "server_running", "message": "Stop the local server, then try again"}
    requirements = load_master_level_requirements()
    if not requirements:
        return {"ok": False, "error": "master_table_missing", "message": "The Master level table could not be read from the game data, so nothing was changed. Check that the game files are complete"}
    servlet = tool.build_servlet()
    _key, original = tool.find_profile_by_aime_id(servlet._profiles, aime_id)
    if original is None:
        return {"ok": False, "error": "account_not_found", "message": "The selected account was not found"}
    profile = deepcopy(original)
    try:
        changed = max_master_exp(profile, requirements)
        profile.setdefault("local_admin_actions", {})["master"] = {
            "at": datetime.now(timezone.utc).isoformat(timespec="seconds"),
            "changed": changed,
        }
        backup = tool.backup_file(tool.PROFILES_PATH)
        servlet._save_profiles(profile)
    except Exception as exc:
        return {"ok": False, "error": "upgrade_failed", "message": str(exc)}
    return {"ok": True, "action": "master", "changed": changed, "backup": backup}


def main() -> int:
    for stream in (sys.stdout, sys.stderr):
        reconfigure = getattr(stream, "reconfigure", None)
        if callable(reconfigure):
            reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description="Max Master Level for one local account")
    parser.add_argument("--aime-id", type=int, required=True)
    parser.add_argument("--json", action="store_true")
    args = parser.parse_args()
    result = run(args.aime_id)
    sys.stdout.write(json.dumps(result, ensure_ascii=False) + "\n")
    sys.stdout.flush()
    return 0 if result.get("ok") else 1


if __name__ == "__main__":
    sys.exit(main())
