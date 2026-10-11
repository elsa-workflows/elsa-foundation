#!/usr/bin/env bash
# Builds Elsa.Workbench and Elsa Studio from source and starts both for the UX QA routine
# (docs/skills/catalog.md#ux-qa-routine). Idempotent: a pair it already started and that is still healthy is reused.
# It never touches a listener it did not start; teardown.sh stops only the process groups recorded here. Each host's
# PID is recorded as soon as it launches, and any failure stops what this invocation started.
# .state/env is data (KEY=value lines), read with sed and never sourced, so a checkout path cannot run as shell code.
#
#   STUDIO_DIR=/path/to/elsa-foundation-studio bash tools/ux-qa/setup.sh
#
# Prints `STAGE <name>` before each step, so a failure names the step that broke.
set -euo pipefail
set -m # each background host gets its own process group, so teardown can stop dotnet run and its child together

here=$(cd "$(dirname "$0")" && pwd)
root=$(cd "$here/../.." && pwd)
state="$here/.state"
logs="$state/logs"
workbench_url="http://localhost:5095"
studio_url="http://localhost:5089"
stage="preflight"

on_error() {
  local status=$?
  trap - ERR
  echo "FAILED at stage: $stage (logs: $logs)" >&2
  bash "$here/teardown.sh" >&2 || true
  exit "$status"
}
trap on_error ERR

step() { stage="$1"; echo "STAGE $stage"; }
state_get() { [ -f "$state/env" ] && sed -n "s/^$1=//p" "$state/env" | tail -1; }
record() { printf '%s=%s\n' "$1" "$2" >> "$state/env"; }

find_studio() {
  local candidate
  for candidate in "${STUDIO_DIR:-}" "$root/../elsa-foundation-studio" "$HOME/elsa-foundation-studio" "/home/user/elsa-foundation-studio"; do
    if [ -n "$candidate" ] && [ -f "$candidate/Essentials/apps/Elsa.Studio.Web/Elsa.Studio.Web.csproj" ]; then
      (cd "$candidate" && pwd)
      return 0
    fi
  done
  echo "Elsa Studio checkout not found; set STUDIO_DIR to an elsa-foundation-studio clone." >&2
  return 1
}

workbench_ready() { curl -fsS "$workbench_url/health/ready" 2>/dev/null | grep -q '"ready"'; }
studio_ready() { curl -fsS -o /dev/null "$studio_url/studio-runtime.js" 2>/dev/null; }

wait_for() {
  local label="$1" check="$2" pid="$3" seconds="${4:-240}"
  for _ in $(seq "$seconds"); do
    if "$check"; then echo "$label is ready."; return 0; fi
    if ! kill -0 "$pid" 2>/dev/null; then echo "$label exited during startup; see $logs." >&2; return 1; fi
    sleep 1
  done
  echo "$label was not ready after ${seconds}s; see $logs." >&2
  return 1
}

port_free() {
  ! (exec 3<>"/dev/tcp/127.0.0.1/$1") 2>/dev/null
}

mkdir -p "$logs"

if [ -f "$state/env" ]; then
  workbench_pid=$(state_get WORKBENCH_PID) studio_pid=$(state_get STUDIO_PID)
  if [ "$(state_get READY)" = "1" ] && kill -0 "${workbench_pid:-0}" 2>/dev/null && kill -0 "${studio_pid:-0}" 2>/dev/null \
     && workbench_ready && studio_ready; then
    echo "Reusing the running pair: Workbench $workbench_url (pid $workbench_pid), Studio $studio_url (pid $studio_pid)."
    exit 0
  fi
  bash "$here/teardown.sh"
fi

step preflight
studio=$(find_studio)
if ! command -v dotnet >/dev/null; then
  # A cloud session with several repositories starts outside them, so the repository's SessionStart hook never ran.
  CLAUDE_CODE_REMOTE=true CLAUDE_PROJECT_DIR="$root" CLAUDE_ENV_FILE="$state/dotnet.env" bash "$root/.claude/hooks/session-start.sh"
  export DOTNET_ROOT="$HOME/.dotnet" PATH="$HOME/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
  command -v dotnet >/dev/null || { echo "dotnet is not on PATH and could not be installed." >&2; false; }
fi
command -v node >/dev/null || { echo "node is not on PATH." >&2; false; }
node_major=$(node -p 'process.versions.node.split(".")[0]')
[ "$node_major" -ge 22 ] || { echo "Node 22 or later is required; found $(node --version)." >&2; false; }
if ! command -v pnpm >/dev/null; then
  pnpm_version=$(node -p 'require(process.argv[1]).packageManager.split("@")[1]' "$studio/package.json")
  corepack enable --install-directory "$state/bin" pnpm >/dev/null 2>&1 || corepack enable pnpm
  export PATH="$state/bin:$PATH"
  corepack prepare "pnpm@$pnpm_version" --activate >/dev/null
fi
for port in 5095 5089; do
  port_free "$port" || { echo "Port $port is already in use by a process this script did not start; leaving it alone." >&2; false; }
done
echo "Foundation $(git -C "$root" rev-parse --short HEAD), Studio $(git -C "$studio" rev-parse --short HEAD) at $studio"
echo "node $(node --version), pnpm $(cd "$studio" && pnpm --version), dotnet $(dotnet --version)"

{
  echo "WORKBENCH_URL=$workbench_url"
  echo "STUDIO_URL=$studio_url"
  echo "FOUNDATION_DIR=$root"
  echo "STUDIO_DIR=$studio"
  echo "FOUNDATION_SHA=$(git -C "$root" rev-parse HEAD)"
  echo "STUDIO_SHA=$(git -C "$studio" rev-parse HEAD)"
  echo "LOGS=$logs"
} > "$state/env"

step workbench-build
dotnet build "$root/Essentials/apps/Elsa.Workbench/Elsa.Workbench.csproj" -p:RestoreLockedMode=true -v:q -nologo > "$logs/workbench-build.log" 2>&1 \
  || { tail -40 "$logs/workbench-build.log" >&2; false; }

step studio-install
(cd "$studio" && pnpm install --frozen-lockfile) > "$logs/studio-install.log" 2>&1 \
  || { tail -40 "$logs/studio-install.log" >&2; false; }

step studio-build
(cd "$studio" && pnpm build) > "$logs/studio-build.log" 2>&1 \
  || { tail -40 "$logs/studio-build.log" >&2; false; }

step studio-host-build
dotnet build "$studio/Essentials/apps/Elsa.Studio.Web/Elsa.Studio.Web.csproj" -v:q -nologo > "$logs/studio-host-build.log" 2>&1 \
  || { tail -40 "$logs/studio-host-build.log" >&2; false; }

step browser-install
(cd "$here" && npm ci --no-audit --no-fund && npx playwright install chromium) > "$logs/browser-install.log" 2>&1 \
  || { tail -40 "$logs/browser-install.log" >&2; false; }

step workbench-start
# The Workbench writes its Nuplane lock file into the project on startup; teardown removes it when this run created it.
lock_file="$root/Essentials/apps/Elsa.Workbench/nuplane.lock.json"
[ -e "$lock_file" ] || record GENERATED_LOCK_FILE "$lock_file"
dotnet run --no-build --project "$root/Essentials/apps/Elsa.Workbench/Elsa.Workbench.csproj" --launch-profile http \
  > "$logs/workbench.log" 2>&1 &
workbench_pid=$!
record WORKBENCH_PID "$workbench_pid"
wait_for "Workbench" workbench_ready "$workbench_pid"

step studio-start
Studio__BackendBaseUrl="$workbench_url" dotnet run --no-build --project "$studio/Essentials/apps/Elsa.Studio.Web/Elsa.Studio.Web.csproj" \
  --launch-profile http > "$logs/studio.log" 2>&1 &
studio_pid=$!
record STUDIO_PID "$studio_pid"
wait_for "Studio" studio_ready "$studio_pid"

record READY 1
stage="done"
echo "Ready: Studio $studio_url, Workbench $workbench_url. State in $state/env; stop with tools/ux-qa/teardown.sh."
