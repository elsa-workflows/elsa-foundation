# shellcheck shell=bash disable=SC2034
# Shared by the tools/demo scripts: sourced, never run. (SC2034: the variables are used by the scripts that source it.)

demo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
demo_artifacts="$demo_root/artifacts/demo"

# The one place the default Sqlite database is named. The scripts, and the README, mean this file when nothing sets
# ELSA_EF_CONNECTION. Pooling is off so a host and the persistence tool never hold each other's file open.
demo_sqlite_file="$demo_artifacts/notes.db"
demo_sqlite_connection="Data Source=$demo_sqlite_file;Pooling=False"

# Where prepack.sh stages the two releases (staging/1 and staging/2) for publish.sh, where run-host.sh records the process id of
# each host it starts (pids/NAME.pid) for reset.sh, the resolve-only closure feed, and the folder the runbook tees each host's
# output into (logs/NAME.log).
demo_staging="$demo_artifacts/staging"
demo_pids="$demo_artifacts/pids"
demo_closure="$demo_artifacts/closure"
demo_logs="$demo_artifacts/logs"

# The PostgreSQL container of the two-host demo. DEMO_PG_CONTAINER renames it, for a machine where the name is taken.
demo_pg_container="${DEMO_PG_CONTAINER:-elsa-demo-pg}"

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

# demo_need_value "$@": in the branch of an option that takes a value, stops with a reason when that value is missing.
demo_need_value() {
  [[ $# -ge 2 ]] || demo_fail "$1 needs a value (see --help)."
}

# demo_require COMMAND...: every command must be on the PATH, or the script stops naming the first one that is not, with the way
# to install it.
demo_require() {
  local command
  for command in "$@"; do
    command -v "$command" >/dev/null 2>&1 || demo_fail "$command is not installed or not on the PATH. On macOS: brew install $command (Docker: install Docker Desktop)."
  done
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

# demo_release_version RELEASE: the package version of a release, 1 -> 1.0.0 and 2 -> 1.1.0 (see the Notes project file).
demo_release_version() {
  case "$1" in
    1) echo "1.0.0" ;;
    2) echo "1.1.0" ;;
    *) demo_fail "There is no release '$1'; the releases are 1 and 2." ;;
  esac
}

# demo_staged_package RELEASE: the path where prepack.sh stages that release's package (which need not exist yet).
demo_staged_package() {
  local version
  version="$(demo_release_version "$1")" || exit 1
  echo "$demo_staging/$1/Elsa.Samples.Nuplane.Notes.$version.nupkg"
}

# demo_port_in_use PORT: succeeds when something already listens on 127.0.0.1:PORT.
demo_port_in_use() {
  (exec 3<>"/dev/tcp/127.0.0.1/$1") 2>/dev/null
}

# demo_host_pid NAME: the process id recorded for host NAME when that process is still a demo host, and nothing otherwise. A
# recorded id alone is not enough: the number may since have been given to another process, which nothing here may ever signal.
# A demo host is the host dll started by run-host.sh, whose content root is a folder under artifacts/demo/hosts.
demo_host_pid() {
  local file="$demo_pids/$1.pid" pid command
  [[ -f "$file" ]] || return 0
  pid="$(<"$file")"
  [[ "$pid" =~ ^[0-9]+$ ]] || return 0
  command="$(ps -ww -p "$pid" -o command= 2>/dev/null || true)"
  if [[ "$command" == *"Elsa.Foundation.Host.dll"* && "$command" == *"--contentRoot $demo_artifacts/hosts/"* ]]; then
    echo "$pid"
  fi
}
