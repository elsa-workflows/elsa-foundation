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

server_output_dir="$temporary_directory/server-output"
server_dll="$server_output_dir/Elsa.Server.dll"
content_root="$temporary_directory/content-root"
baseline_dir="$temporary_directory/baseline"
fake_bin="$temporary_directory/bin"
fake_server="$temporary_directory/fake-server.py"
mkdir -p "$server_output_dir" "$content_root" "$baseline_dir" "$fake_bin"
: >"$server_dll"
printf '{}\n' >"$content_root/appsettings.json"
printf '{}\n' >"$content_root/shells.json"
sqlite3 "$baseline_dir/elsa-groundwork-runtime.db" 'VACUUM;'

cat >"$fake_server" <<'PY'
import http.server
import os
import subprocess
import sys


if os.environ.get("FAKE_STUBBORN_CHILD_PID_FILE"):
    subprocess.Popen([
        sys.executable,
        "-c",
        "import os,pathlib,signal,sys,time; "
        "signal.signal(signal.SIGTERM, signal.SIG_IGN); "
        "pathlib.Path(sys.argv[1]).write_text(str(os.getpid())); "
        "time.sleep(300)",
        os.environ["FAKE_STUBBORN_CHILD_PID_FILE"],
    ])


class Handler(http.server.BaseHTTPRequestHandler):
    def do_GET(self):
        if self.path == "/health/ready":
            body = os.environ.get(
                "FAKE_READY_BODY",
                '{"status":"ready","shell":"default","generation":1,"durationMs":1}').encode()
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
run_case "invalid shutdown timeout" 2 '--shutdown-timeout-seconds.*positive|positive.*--shutdown-timeout-seconds' \
  "${common_args[@]}" --shutdown-timeout-seconds 0

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

run_case "artifacts overlap baseline" 2 '(overlap|must not overlap)' \
  --server-dll "$server_dll" --content-root "$content_root" --baseline-dir "$baseline_dir" \
  --artifacts-dir "$baseline_dir/results" --base-url "http://127.0.0.1:$(free_port)" \
  --readiness-path /health/ready --workflow-path /workflows/http/hello-world \
  --expected-status 200 --expected-body 'Hello World!' --boots 1
if [[ -e "$baseline_dir/results" ]]; then
  printf 'FAIL: overlap validation mutated the frozen baseline.\n' >&2
  failures=$((failures + 1))
fi

existing_artifacts="$temporary_directory/existing-artifacts"
mkdir "$existing_artifacts"
run_case "existing artifacts directory" 2 'already exists|choose a new path' \
  --server-dll "$server_dll" --content-root "$content_root" --baseline-dir "$baseline_dir" \
  --artifacts-dir "$existing_artifacts" --base-url "http://127.0.0.1:$(free_port)" \
  --readiness-path /health/ready --workflow-path /workflows/http/hello-world \
  --expected-status 200 --expected-body 'Hello World!' --boots 1

export PATH="$fake_bin:$PATH"
export FAKE_SERVER_SCRIPT="$fake_server"

hash_a_artifacts="$temporary_directory/hash-a-artifacts"
hash_b_artifacts="$temporary_directory/hash-b-artifacts"
export FAKE_SERVER_PORT="$(free_port)"
run_case "successful report provenance" 0 'Cold-start report' \
  --server-dll "$server_dll" --content-root "$content_root" --baseline-dir "$baseline_dir" \
  --artifacts-dir "$hash_a_artifacts" --base-url "http://127.0.0.1:$FAKE_SERVER_PORT" \
  --readiness-path /health/ready --workflow-path /workflows/http/hello-world \
  --expected-status 200 --expected-body 'Hello World!' --boots 1 \
  --startup-timeout-seconds 7 --shutdown-timeout-seconds 3
printf 'sibling-output-v2\n' >"$server_output_dir/Sibling.Output.dll"
export FAKE_SERVER_PORT="$(free_port)"
run_case "output closure hash changes with sibling" 0 'Cold-start report' \
  --server-dll "$server_dll" --content-root "$content_root" --baseline-dir "$baseline_dir" \
  --artifacts-dir "$hash_b_artifacts" --base-url "http://127.0.0.1:$FAKE_SERVER_PORT" \
  --readiness-path /health/ready --workflow-path /workflows/http/hello-world \
  --expected-status 200 --expected-body 'Hello World!' --boots 1 \
  --startup-timeout-seconds 7 --shutdown-timeout-seconds 3
if ! python3 - "$hash_a_artifacts/report.json" "$hash_b_artifacts/report.json" <<'PY'
import json, pathlib, sys
first, second = (json.loads(pathlib.Path(path).read_text()) for path in sys.argv[1:])
assert first["provenance"]["serverOutputSha256"] != second["provenance"]["serverOutputSha256"]
assert second["request"]["startupTimeoutSeconds"] == 7
assert second["request"]["shutdownTimeoutSeconds"] == 3
PY
then
  printf 'FAIL: successful report did not preserve output-closure hash and timeout provenance.\n' >&2
  failures=$((failures + 1))
fi

launch_marker="$temporary_directory/pre-session.marker"
launch_output="$temporary_directory/pre-session.output"
pre_session_failures="$failures"
export _ELSA_PERF_TEST_PRE_SESSION_DELAY_SECONDS=10
export _ELSA_PERF_TEST_PRE_SESSION_MARKER="$launch_marker"
export FAKE_SERVER_PORT="$(free_port)"
set +e
bash "$subject" \
  --server-dll "$server_dll" --content-root "$content_root" --baseline-dir "$baseline_dir" \
  --artifacts-dir "$temporary_directory/pre-session-artifacts" \
  --base-url "http://127.0.0.1:$FAKE_SERVER_PORT" --readiness-path /health/ready \
  --workflow-path /workflows/http/hello-world --expected-status 200 --expected-body 'Hello World!' --boots 1 \
  --startup-timeout-seconds 1 --shutdown-timeout-seconds 1 >"$launch_output" 2>&1 &
launch_harness_pid=$!
set -e
for _ in {1..200}; do
  [[ -f "$launch_marker" ]] && break
  sleep 0.01
done
if [[ ! -f "$launch_marker" ]]; then
  printf 'FAIL: pre-session launch fixture did not publish its PID.\n' >&2
  kill -KILL "$launch_harness_pid" 2>/dev/null || true
  wait "$launch_harness_pid" 2>/dev/null || true
  failures=$((failures + 1))
else
  launch_child_pid="$(cat "$launch_marker")"
  kill -TERM "$launch_harness_pid" 2>/dev/null || true
  for _ in {1..300}; do
    kill -0 "$launch_harness_pid" 2>/dev/null || break
    sleep 0.01
  done
  if kill -0 "$launch_harness_pid" 2>/dev/null; then
    printf 'FAIL: benchmark did not terminate during the pre-session launch window.\n' >&2
    kill -KILL "$launch_harness_pid" 2>/dev/null || true
    failures=$((failures + 1))
  fi
  set +e
  wait "$launch_harness_pid"
  launch_status=$?
  set -e
  if [[ "$launch_status" -ne 143 ]]; then
    printf 'FAIL: pre-session SIGTERM returned %s; expected 143.\n' "$launch_status" >&2
    failures=$((failures + 1))
  fi
  if kill -0 "$launch_child_pid" 2>/dev/null; then
    printf 'FAIL: benchmark leaked pre-session child PID %s.\n' "$launch_child_pid" >&2
    kill -KILL "$launch_child_pid" 2>/dev/null || true
    failures=$((failures + 1))
  fi
fi
unset _ELSA_PERF_TEST_PRE_SESSION_DELAY_SECONDS _ELSA_PERF_TEST_PRE_SESSION_MARKER
if [[ "$failures" -eq "$pre_session_failures" ]]; then
  printf 'PASS: pre-session SIGTERM is bounded and leak-free\n'
fi

export FAKE_SERVER_PORT="$(free_port)"
run_case "ready budget failure" 1 'p95.*exceeds.*budget|budget.*failed' \
  --server-dll "$server_dll" --content-root "$content_root" --baseline-dir "$baseline_dir" \
  --base-url "http://127.0.0.1:$FAKE_SERVER_PORT" --readiness-path /health/ready \
  --workflow-path /workflows/http/hello-world --expected-status 200 --expected-body 'Hello World!' \
  --boots 1 --enforce-ready-p95-ms 0 \
  --output-json "$temporary_directory/result.json" \
  --output-markdown "$temporary_directory/result.md"

export FAKE_READY_BODY='{"status":"starting","shell":"default","code":"shell_activation_pending"}'
export FAKE_SERVER_PORT="$(free_port)"
run_case "malformed readiness body" 1 'malformed or unexpected readiness' \
  --server-dll "$server_dll" --content-root "$content_root" --baseline-dir "$baseline_dir" \
  --base-url "http://127.0.0.1:$FAKE_SERVER_PORT" --readiness-path /health/ready \
  --workflow-path /workflows/http/hello-world --expected-status 200 --expected-body 'Hello World!' --boots 1 \
  --output-json "$temporary_directory/readiness-failure.json" \
  --output-markdown "$temporary_directory/readiness-failure.md"
unset FAKE_READY_BODY

stubborn_child_pid_file="$temporary_directory/stubborn-child.pid"
export FAKE_STUBBORN_CHILD_PID_FILE="$stubborn_child_pid_file"
export FAKE_SERVER_PORT="$(free_port)"
run_case "stubborn descendant is killed" 1 'process group.*did not shut down cleanly|shutdown_failed' \
  --server-dll "$server_dll" --content-root "$content_root" --baseline-dir "$baseline_dir" \
  --base-url "http://127.0.0.1:$FAKE_SERVER_PORT" --readiness-path /health/ready \
  --workflow-path /workflows/http/hello-world --expected-status 200 --expected-body 'Hello World!' --boots 1 \
  --shutdown-timeout-seconds 1 \
  --output-json "$temporary_directory/stubborn-child-failure.json" \
  --output-markdown "$temporary_directory/stubborn-child-failure.md"
unset FAKE_STUBBORN_CHILD_PID_FILE
if [[ ! -f "$stubborn_child_pid_file" ]]; then
  printf 'FAIL: stubborn descendant fixture did not publish its PID.\n' >&2
  failures=$((failures + 1))
else
  stubborn_child_pid="$(cat "$stubborn_child_pid_file")"
  for _ in {1..100}; do
    kill -0 "$stubborn_child_pid" 2>/dev/null || break
    sleep 0.01
  done
  if kill -0 "$stubborn_child_pid" 2>/dev/null; then
    printf 'FAIL: benchmark leaked stubborn descendant PID %s.\n' "$stubborn_child_pid" >&2
    kill -KILL "$stubborn_child_pid" 2>/dev/null || true
    failures=$((failures + 1))
  fi
fi

export FAKE_SERVER_PORT="$(free_port)"
run_case "trailing newline body mismatch" 1 'workflow validation failed' \
  --server-dll "$server_dll" --content-root "$content_root" --baseline-dir "$baseline_dir" \
  --base-url "http://127.0.0.1:$FAKE_SERVER_PORT" --readiness-path /health/ready \
  --workflow-path /workflows/http/hello-world --expected-status 200 --expected-body $'Hello World!\n' --boots 1 \
  --output-json "$temporary_directory/failure.json" --output-markdown "$temporary_directory/failure.md"
if ! python3 - "$temporary_directory/failure.json" <<'PY'
import json, pathlib, sys
report = json.loads(pathlib.Path(sys.argv[1]).read_text())
sample = report["boots"][0]
assert sample["status"] == "workflow_validation_failed"
assert pathlib.Path(sample["logPath"]).is_file()
assert "serverOutputSha256" in report["provenance"]
assert report["request"]["startupTimeoutSeconds"] == 120
assert report["request"]["shutdownTimeoutSeconds"] == 30
PY
then
  printf 'FAIL: forced failure report did not retain the stable status and log path.\n' >&2
  failures=$((failures + 1))
fi

if ((failures > 0)); then
  printf 'FAILED: %s cold-start CLI contract case(s) failed.\n' "$failures" >&2
  exit 1
fi

printf 'PASS: all cold-start CLI contract cases passed.\n'
