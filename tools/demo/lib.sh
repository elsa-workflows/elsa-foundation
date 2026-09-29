# Shared by the tools/demo scripts: sourced, never run.

demo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
demo_artifacts="$demo_root/artifacts/demo"

# The one place the default Sqlite database is named. The scripts, and the README, mean this file when nothing sets
# ELSA_EF_CONNECTION. Pooling is off so a host and the persistence tool never hold each other's file open.
demo_sqlite_file="$demo_artifacts/notes.db"
demo_sqlite_connection="Data Source=$demo_sqlite_file;Pooling=False"

demo_fail() {
  echo "error: $*" >&2
  exit 1
}

# demo_dir PATH: the directory, created when it is missing, as an absolute path. A relative PATH is relative to the
# repository root, whatever directory the script was started from.
demo_dir() {
  local path="$1"
  [[ "$path" == /* ]] || path="$demo_root/$path"
  mkdir -p "$path" && (cd "$path" && pwd -P)
}

# python3 writes the demo's JSON (a proper encoder, so no value is ever spliced into JSON text) and walks a restore's
# project.assets.json. On macOS /usr/bin/python3 is a stub that only offers to install the Xcode tools, so run it.
demo_require_python() {
  python3 -c 'import json, sys; sys.exit(0 if sys.version_info >= (3, 8) else 1)' >/dev/null 2>&1 ||
    demo_fail "python3 is missing or is not a working Python 3.8 or later. On macOS, /usr/bin/python3 is a stub until the Xcode command line tools are installed: run 'xcode-select --install', or install Python."
}

# demo_quiet LABEL COMMAND...: runs COMMAND with its output held back. When it fails, the output is printed, so a build or
# restore error is never lost, and the script exits with the command's status.
demo_quiet() {
  local label="$1" log status=0
  shift
  log="$(mktemp)"
  "$@" >"$log" 2>&1 || status=$?
  if [[ "$status" -ne 0 ]]; then
    echo "error: $label failed (exit $status). Its output:" >&2
    cat "$log" >&2
    rm -f "$log"
    exit "$status"
  fi
  rm -f "$log"
}
