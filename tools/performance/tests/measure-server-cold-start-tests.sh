#!/usr/bin/env bash

set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
subject="$repository_root/tools/performance/measure-server-cold-start.sh"

if [[ ! -f "$subject" ]]; then
  printf 'FAIL: cold-start measurement CLI is not implemented: %s (expected RED for T014).\n' "$subject" >&2
  exit 1
fi

temporary_directory="$(mktemp -d)"
occupied_port_pid=""
cleanup() {
  if [[ -n "$occupied_port_pid" ]]; then
    kill "$occupied_port_pid" 2>/dev/null || true
    wait "$occupied_port_pid" 2>/dev/null || true
  fi
  rm -rf "$temporary_directory"
}
trap cleanup EXIT

failures=0
last_output="$temporary_directory/last-output"

run_case() {
  local name="$1"
  local expected_status="$2"
  local expected_pattern="$3"
  shift 3

  local actual_status
  set +e
  bash "$subject" "$@" >"$last_output" 2>&1
  actual_status=$?
  set -e

  if [[ "$actual_status" -ne "$expected_status" ]]; then
    printf 'FAIL: %s returned %s; expected %s.\n' "$name" "$actual_status" "$expected_status" >&2
    sed 's/^/  | /' "$last_output" >&2
    failures=$((failures + 1))
    return
  fi

  if ! grep -Eiq -- "$expected_pattern" "$last_output"; then
    printf 'FAIL: %s output did not match /%s/.\n' "$name" "$expected_pattern" >&2
    sed 's/^/  | /' "$last_output" >&2
    failures=$((failures + 1))
    return
  fi

  printf 'PASS: %s\n' "$name"
}

free_port() {
  python3 - <<'PY'
import socket

with socket.socket() as sock:
    sock.bind(("127.0.0.1", 0))
    print(sock.getsockname()[1])
PY
}

server_dll="$temporary_directory/Elsa.Server.dll"
content_root="$temporary_directory/content-root"
baseline_dir="$temporary_directory/baseline"
fake_bin="$temporary_directory/bin"
fake_server="$temporary_directory/fake-server.py"
mkdir -p "$content_root" "$baseline_dir" "$fake_bin"
: >"$server_dll"
printf '{}\n' >"$content_root/appsettings.json"
printf '{}\n' >"$content_root/shells.json"
sqlite3 "$baseline_dir/elsa-groundwork-runtime.db" 'VACUUM;'

cat >"$fake_server" <<'PY'
import http.server
import os


class Handler(http.server.BaseHTTPRequestHandler):
    def do_GET(self):
        if self.path == "/health/ready":
            body = b'{"status":"ready","shell":"default","generation":1,"durationMs":1}'
            content_type = "application/json"
            status = 200
        elif self.path == "/workflows/http/hello-world":
            body = b"Hello World!"
            content_type = "text/plain"
            status = 200
        else:
            body = b'{"status":"live"}'
            content_type = "application/json"
            status = 200

        self.send_response(status)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, *_args):
        pass


server = http.server.ThreadingHTTPServer(("127.0.0.1", int(os.environ["FAKE_SERVER_PORT"])), Handler)
server.serve_forever()
PY

cat >"$fake_bin/dotnet" <<'SH'
#!/usr/bin/env bash
set -euo pipefail
if [[ "${1:-}" == "--version" ]]; then
  printf '10.0.100-test\n'
  exit 0
fi
if [[ "${1:-}" == "--list-runtimes" ]]; then
  printf 'Microsoft.NETCore.App 10.0.0-test [/tmp/fake-dotnet]\n'
  exit 0
fi
exec python3 "$FAKE_SERVER_SCRIPT"
SH
chmod +x "$fake_bin/dotnet"

common_args=(
  --server-dll "$server_dll"
  --content-root "$content_root"
  --baseline-dir "$baseline_dir"
  --base-url "http://127.0.0.1:$(free_port)"
  --readiness-path /health/ready
  --workflow-path /workflows/http/hello-world
  --expected-status 200
  --expected-body 'Hello World!'
  --boots 1
)

run_case "help" 0 'Usage:|--server-dll' --help
run_case "unknown argument" 2 'Unknown argument.*--definitely-unknown' --definitely-unknown
run_case "zero boots" 2 '--boots.*positive|positive.*--boots' "${common_args[@]:0:${#common_args[@]}-2}" --boots 0
run_case "non-numeric boots" 2 '--boots.*positive|positive.*--boots' "${common_args[@]:0:${#common_args[@]}-2}" --boots nope

run_case "missing server DLL" 2 'Server DLL.*not found|not found.*server' \
  --server-dll "$temporary_directory/missing-server.dll" \
  --content-root "$content_root" --baseline-dir "$baseline_dir" \
  --base-url "http://127.0.0.1:$(free_port)" --readiness-path /health/ready \
  --workflow-path /workflows/http/hello-world --expected-status 200 --expected-body 'Hello World!' --boots 1

run_case "missing content root" 2 'Content root.*not found|not found.*content' \
  --server-dll "$server_dll" --content-root "$temporary_directory/missing-content" \
  --baseline-dir "$baseline_dir" --base-url "http://127.0.0.1:$(free_port)" \
  --readiness-path /health/ready --workflow-path /workflows/http/hello-world \
  --expected-status 200 --expected-body 'Hello World!' --boots 1

run_case "missing baseline directory" 2 'Baseline.*not found|not found.*baseline' \
  --server-dll "$server_dll" --content-root "$content_root" \
  --baseline-dir "$temporary_directory/missing-baseline" --base-url "http://127.0.0.1:$(free_port)" \
  --readiness-path /health/ready --workflow-path /workflows/http/hello-world \
  --expected-status 200 --expected-body 'Hello World!' --boots 1

occupied_port="$(free_port)"
occupied_ready="$temporary_directory/occupied-ready"
python3 - "$occupied_port" "$occupied_ready" <<'PY' &
import pathlib
import socket
import sys
import time

sock = socket.socket()
sock.bind(("127.0.0.1", int(sys.argv[1])))
sock.listen()
pathlib.Path(sys.argv[2]).touch()
try:
    while True:
        time.sleep(1)
finally:
    sock.close()
PY
occupied_port_pid=$!
for _ in {1..100}; do
  [[ -f "$occupied_ready" ]] && break
  sleep 0.01
done
if [[ ! -f "$occupied_ready" ]]; then
  printf 'FAIL: occupied-port fixture did not start.\n' >&2
  exit 1
fi

run_case "occupied port" 2 'port.*(occupied|in use|unavailable)|address already in use' \
  --server-dll "$server_dll" --content-root "$content_root" --baseline-dir "$baseline_dir" \
  --base-url "http://127.0.0.1:$occupied_port" --readiness-path /health/ready \
  --workflow-path /workflows/http/hello-world --expected-status 200 --expected-body 'Hello World!' --boots 1
kill "$occupied_port_pid" 2>/dev/null || true
wait "$occupied_port_pid" 2>/dev/null || true
occupied_port_pid=""

export PATH="$fake_bin:$PATH"
export FAKE_SERVER_SCRIPT="$fake_server"
export FAKE_SERVER_PORT="$(free_port)"
run_case "ready budget failure" 1 'p95.*exceeds.*budget|budget.*failed' \
  --server-dll "$server_dll" --content-root "$content_root" --baseline-dir "$baseline_dir" \
  --base-url "http://127.0.0.1:$FAKE_SERVER_PORT" --readiness-path /health/ready \
  --workflow-path /workflows/http/hello-world --expected-status 200 --expected-body 'Hello World!' \
  --boots 1 --enforce-ready-p95-ms 0 \
  --output-json "$temporary_directory/result.json" \
  --output-markdown "$temporary_directory/result.md"

if ((failures > 0)); then
  printf 'FAILED: %s cold-start CLI contract case(s) failed.\n' "$failures" >&2
  exit 1
fi

printf 'PASS: all cold-start CLI contract cases passed.\n'
