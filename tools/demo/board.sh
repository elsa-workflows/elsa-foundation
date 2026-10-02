#!/usr/bin/env bash
# The package board, for the screen: one row per demo host for the Notes module, showing the three places a release passes through
# (the host's folder feed, what the host has installed, what it is serving), so the audience sees a package land, install and switch
# instead of only the 409 and 200 that follow. Read-only: it lists a folder, reads the host's own Nuplane record and sends GET
# requests, and it never calls /reload or /reconcile.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

usage() {
  cat <<'USAGE'
Usage: bash tools/demo/board.sh HOST... [--watch]

HOST is solo, a or b. For each one the board prints a row for Elsa.Samples.Nuplane.Notes:

  in the feed   the Notes versions present as .nupkg files in artifacts/demo/hosts/HOST/feed, or - for none
  installed     the version Nuplane recorded as active in artifacts/demo/hosts/HOST/.nuplane/store-state.json, or - when
                the host has not written it yet
  serving       what GET /demo/notes/with-tags says: 404 is 1.0.0, 409 is 1.1.0 with the reason its feature is dormant,
                200 is 1.1.0 with its tags live, and no answer is down. A release that is installed but not yet switched to
                says so underneath

  --watch       clear and redraw every second until Ctrl-C, with the time, and mark a cell that just changed (bold and
                yellow; NO_COLOR keeps it bold and reversed). Nothing is drawn with escape codes unless stdout is a terminal

Ports: DEMO_PORT_SOLO (5101), DEMO_PORT_A (5201), DEMO_PORT_B (5202), the variables tools/demo/rehearse.sh uses.
The frame is at most 50 columns wide and prints no absolute path.
USAGE
}

hosts=()
watch=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    solo|a|b) hosts+=("$1"); shift ;;
    --watch) watch=1; shift ;;
    -h|--help) usage; exit 0 ;;
    *) demo_fail "unknown argument '$1' (see --help)." ;;
  esac
done
[[ ${#hosts[@]} -gt 0 ]] || demo_fail "Give at least one host, solo, a or b: bash tools/demo/board.sh solo (see --help)."

demo_require_python
cd "$demo_root"

export DEMO_BOARD_PORTS="solo=${DEMO_PORT_SOLO:-5101},a=${DEMO_PORT_A:-5201},b=${DEMO_PORT_B:-5202}"
python3 - "$watch" "${hosts[@]}" <<'PY'
import concurrent.futures, datetime, json, os, re, sys, textwrap, time, urllib.error, urllib.request

watch = sys.argv[1] == "1"
hosts = sys.argv[2:]
ports = dict(item.split("=") for item in os.environ["DEMO_BOARD_PORTS"].split(","))
module = "Elsa.Samples.Nuplane.Notes"
hold = 6  # seconds a changed cell stays marked, so the audience has time to see it
reasons = {"schema-version-not-finalized": "tags dormant: not every host reads 2.0.0 yet"}


def version_key(version):
    return [int(part) for part in version.split("-")[0].split(".") if part.isdigit()], version


def in_feed(host):
    pattern = re.compile("^" + re.escape(module) + r"\.(\d+\.\d+\.\d+[^/]*)\.nupkg$")
    feed = f"artifacts/demo/hosts/{host}/feed"
    found = [m.group(1) for name in (os.listdir(feed) if os.path.isdir(feed) else []) if (m := pattern.match(name))]
    return sorted(found, key=version_key)


def installed(host, previous):
    for _ in range(5):
        try:
            with open(f"artifacts/demo/hosts/{host}/.nuplane/store-state.json") as state:
                active = json.load(state).get("activeVersionById") or {}
            return next((version for key, version in active.items() if key.lower() == module.lower()), "-")
        except FileNotFoundError:
            return "-"
        except ValueError:
            time.sleep(0.1)  # the host is writing the file this very moment
    return previous


def serving(host):
    """(version, note) from GET /demo/notes/with-tags: 404 before release 1.1.0 is switched to, 409 or 200 after."""
    try:
        with urllib.request.urlopen(f"http://127.0.0.1:{ports[host]}/demo/notes/with-tags", timeout=2) as response:
            code, body = response.status, b""
    except urllib.error.HTTPError as error:
        code, body = error.code, error.read()
    except (urllib.error.URLError, OSError):
        return "down", ""
    if code == 404:
        return "1.0.0", ""
    if code == 200:
        return "1.1.0", "tags live"
    if code == 409:
        try:
            refusal = json.loads(body).get("code", "")
        except ValueError:
            refusal = ""
        return "1.1.0", reasons.get(refusal) or f"tags dormant: {refusal or 'refused'}"
    return f"HTTP {code}", "the host answers, but the module is not serving"


def read(host, previous):
    version, note = serving(host)
    feed = in_feed(host)
    now_installed = installed(host, previous.get((host, "installed"), "-"))
    if version == "1.0.0" and now_installed == "1.1.0":
        note = "1.1.0 installed, not switched"
    elif version == "1.0.0" and feed and feed[-1] != now_installed and now_installed != "-":
        note = f"{feed[-1]} in the feed, not installed yet"
    return {"feed": ", ".join(feed) or "-", "installed": now_installed, "serving": version}, note


def frame(rows, notes, changed, color):
    heads = {"feed": "in the feed", "installed": "installed", "serving": "serving"}
    widths = {col: max(len(heads[col]), *(len(rows[h][col]) for h in hosts)) for col in heads}
    mark = "\x1b[1;33m" if not os.environ.get("NO_COLOR") else "\x1b[1;7m"
    lines = [f"{module}   {datetime.datetime.now():%H:%M:%S}", ""]
    lines.append(("host  " + "  ".join(heads[col].ljust(widths[col]) for col in heads)).rstrip())
    for host in hosts:
        cells = []
        for col in heads:
            cell = rows[host][col].ljust(widths[col])
            cells.append(f"{mark}{cell}\x1b[0m" if color and (host, col) in changed else cell)
        lines.append((host.ljust(4) + "  " + "  ".join(cells)).rstrip())
        lines += textwrap.wrap(notes[host], 50, initial_indent="  - ", subsequent_indent="    ") if notes[host] else []
    return lines


def snapshot(previous):
    with concurrent.futures.ThreadPoolExecutor(len(hosts)) as pool:
        results = list(pool.map(lambda host: read(host, previous), hosts))
    rows = {host: row for host, (row, _) in zip(hosts, results)}
    notes = {host: note for host, (_, note) in zip(hosts, results)}
    return rows, notes


if not watch:
    print("\n".join(frame(*snapshot({}), set(), False)))
    sys.exit(0)

tty = sys.stdout.isatty()
previous, changed_at, first = {}, {}, True
if tty:
    sys.stdout.write("\x1b[2J")
try:
    while True:
        started = time.monotonic()
        rows, notes = snapshot(previous)
        now = time.monotonic()
        current = {(host, col): value for host in hosts for col, value in rows[host].items()}
        if not first:
            changed_at.update({key: now for key, value in current.items() if previous.get(key) != value})
        changed = {key for key, at in changed_at.items() if now - at < hold}
        previous, first = current, False
        lines = frame(rows, notes, changed, tty)
        if tty:
            sys.stdout.write("\x1b[H" + "".join(line + "\x1b[K\n" for line in lines) + "\x1b[J")
        else:
            sys.stdout.write("\n".join(lines) + "\n\n")
        sys.stdout.flush()
        time.sleep(max(0.0, 1 - (time.monotonic() - started)))
except KeyboardInterrupt:
    print()
PY
