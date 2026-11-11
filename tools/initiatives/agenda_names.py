#!/usr/bin/env python3
"""Make the coordinator's local, named copy of a committed meeting agenda.

A committed agenda (docs/initiatives/<n>/<date>-meeting-agenda.md) is name-free: positions are numbered and the
initiative board maps them to people ("Positions in the agenda: **1** = @login, ..."). Before the meeting the
coordinator wants names in front of them. This script reads that mapping from the board, replaces the numbered
positions with names, and writes `<agenda>.named.md` next to the agenda. That file is git-ignored: it is for the
coordinator's preview (for example the VS Code Markdown preview) and is never committed.

Usage:
  agenda_names.py <agenda.md> --initiative <n> [--name login=Display ...] [--repo owner/name]
  agenda_names.py --self-test
"""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from pathlib import Path

MAPPING = re.compile(r"\*\*(?P<n>\d+)\*\*\s*=\s*@(?P<login>[A-Za-z0-9-]+)")


def mapping_from_board(body: str) -> dict[str, str]:
    """Position number -> login, from the board's 'Positions in the agenda' sentence."""
    start = body.find("Positions in the agenda")
    if start == -1:
        return {}
    line = body[start:].split("\n", 1)[0]
    return {m["n"]: m["login"] for m in MAPPING.finditer(line)}


def name_agenda(text: str, people: dict[str, str]) -> str:
    """Replace numbered positions with names: table rows '| 1 |' and phrases 'position 1' / 'Position 1'."""
    for number, name in people.items():
        text = re.sub(rf"^\| {number} \|", f"| {name} |", text, flags=re.MULTILINE)
        text = re.sub(rf"\b[Pp]osition {number}\b(?!\d)", name, text)
    text = text.replace("| Position | Summary | Notes |", "| Person | Position | Notes |")
    return text.replace("Positions are numbered; the board maps them to people.", "Local copy with names; never commit it.")


def gh_json(*args: str) -> dict:
    return json.loads(subprocess.run(["gh", *args], capture_output=True, text=True, check=True).stdout)


def self_test() -> int:
    board = "Spoken anchors: ... Positions in the agenda: **1** = @alice, **2** = @bob-x. Final round now"
    people = mapping_from_board(board)
    assert people == {"1": "alice", "2": "bob-x"}, people
    agenda = ("| Position | Summary | Notes |\n|---|---|---|\n| 1 | Accepts. | x |\n| 2 | Differs. | y |\n"
              "| 12 | untouched |\nAccepted position 1. (b) Position 2, plus\nposition 12 stays\n")
    named = name_agenda(agenda, {"1": "Alice", "2": "Bob"})
    assert "| Alice | Accepts." in named and "| Bob | Differs." in named, named
    assert "Accepted Alice." in named and "(b) Bob, plus" in named, named
    assert "| 12 | untouched" in named and "position 12 stays" in named, named
    assert "| Person | Position | Notes |" in named, named
    print("agenda_names self-test: pass")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("agenda", nargs="?")
    parser.add_argument("--initiative", type=int)
    parser.add_argument("--repo", default="elsa-workflows/elsa-foundation")
    parser.add_argument("--name", action="append", default=[], help="login=Display name override")
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        return self_test()
    if not args.agenda or not args.initiative:
        parser.error("an agenda path and --initiative are required")

    board = gh_json("issue", "view", str(args.initiative), "--repo", args.repo, "--json", "body")["body"]
    logins = mapping_from_board(board)
    if not logins:
        print(f"agenda_names: board #{args.initiative} has no 'Positions in the agenda' mapping", file=sys.stderr)
        return 2
    overrides = dict(item.split("=", 1) for item in args.name)
    people = {}
    for number, login in logins.items():
        display = overrides.get(login) or gh_json("api", f"users/{login}").get("name") or login
        people[number] = display

    source = Path(args.agenda)
    target = source.with_name(source.stem + ".named.md")
    target.write_text(name_agenda(source.read_text(encoding="utf-8"), people), encoding="utf-8")
    print(f"agenda_names: wrote {target} ({', '.join(f'{n} = {p}' for n, p in sorted(people.items()))})")
    return 0


if __name__ == "__main__":
    sys.exit(main())
