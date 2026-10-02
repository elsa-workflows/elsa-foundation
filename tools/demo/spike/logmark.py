#!/usr/bin/env python3
"""Spike only: `logmark.py mark LOG` remembers the end of LOG; `logmark.py show LOG [REGEX]` prints what was written to
it since, optionally only the lines matching REGEX (case-insensitive), cut to 300 characters."""
import re
import sys

command, log = sys.argv[1], sys.argv[2]
mark_file = log + ".mark"
if command == "mark":
    with open(log, "rb") as handle:
        handle.seek(0, 2)
        open(mark_file, "w").write(str(handle.tell()))
    sys.exit(0)

start = int(open(mark_file).read())


def since():
    with open(log, "rb") as handle:
        handle.seek(start)
        return handle.read().decode(errors="replace")


pattern = re.compile(sys.argv[3], re.I) if len(sys.argv) > 3 else None
if command == "wait":
    # `logmark.py wait LOG REGEX [SECONDS]`: waits until a line matching REGEX was written since the mark.
    import time
    deadline = time.monotonic() + (float(sys.argv[4]) if len(sys.argv) > 4 else 120)
    while not pattern.search(since()):
        if time.monotonic() > deadline:
            print(f"timed out waiting for /{sys.argv[3]}/ in {log}", file=sys.stderr)
            sys.exit(1)
        time.sleep(0.5)
    sys.exit(0)

text = since()
for line in text.splitlines():
    if line.strip() and (pattern is None or pattern.search(line)):
        print(line[:300])
