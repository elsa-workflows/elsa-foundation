#!/usr/bin/env bash
# Builds Elsa.Workbench and Elsa Studio from source and starts both for the UX QA routine
# (docs/skills/catalog.md#ux-qa-routine). Idempotent: a pair it already started and that is still healthy is reused.
# It never touches a listener it did not start; teardown.sh stops only the process groups recorded here.
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
trap 'echo "FAILED at stage: $stage (logs: $logs)" >&2' ERR

step() { stage="$1"; echo "STAGE $stage"; }

find_studio() {
  local candidate
  for candidate in "${STUDIO_DIR:-}" "$root/../elsa-foundation-studio" "$HOME/elsa-foundation-studio" "/home/user/elsa-foundation-studio"; do
    if [ -n "$candidate" ] && [ -f "$candidate/src/apps/Elsa.Studio.Web/Elsa.Studio.Web.csproj" ]; then
      (cd "$candidate" && pwd)
      return 0
    fi
  done
  echo "Elsa Studio checkout not found; set STUDIO_DIR to an elsa-foundation-studio clone." >&2
  return 1
}

healthy() {
  curl -fsS "$workbench_url/health/ready" 2>/dev/null | grep -q '"ready"' \
    && curl -fsS -o /dev/null "$studio_url/studio-runtime.js" 2>/dev/null
}

wait_for() {
  local label="$1" check="$2" pid="$3" seconds="${4:-240}"
  for _ in $(seq "$seconds"); do
    if eval "$check"; then echo "$label is ready."; return 0; fi
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
  # shellcheck disable=SC1091
  . "$state/env"
  if kill -0 "${WORKBENCH_PID:-0}" 2>/dev/null && kill -0 "${STUDIO_PID:-0}" 2>/dev/null && healthy; then
    echo "Reusing the running pair: Workbench $workbench_url (pid $WORKBENCH_PID), Studio $studio_url (pid $STUDIO_PID)."
    exit 0
  fi
  bash "$here/teardown.sh"
fi

step preflight
studio=$(find_studio)
command -v dotnet >/dev/null || { echo "dotnet is not on PATH." >&2; false; }
command -v node >/dev/null || { echo "node is not on PATH." >&2; false; }
node_major=$(node -p 'process.versions.node.split(".")[0]')
[ "$node_major" -ge 22 ] || { echo "Node 22 or later is required; found $(node --version)." >&2; false; }
if ! command -v pnpm >/dev/null; then
  pnpm_version=$(node -p "require('$studio/package.json').packageManager.split('@')[1]")
  corepack enable --install-directory "$state/bin" pnpm >/dev/null 2>&1 || corepack enable pnpm
  export PATH="$state/bin:$PATH"
  corepack prepare "pnpm@$pnpm_version" --activate >/dev/null
fi
for port in 5095 5089; do
  port_free "$port" || { echo "Port $port is already in use by a process this script did not start; leaving it alone." >&2; false; }
done
echo "Foundation $(git -C "$root" rev-parse --short HEAD), Studio $(git -C "$studio" rev-parse --short HEAD) at $studio"
echo "node $(node --version), pnpm $(cd "$studio" && pnpm --version), dotnet $(dotnet --version)"

step workbench-build
dotnet build "$root/src/apps/Elsa.Workbench/Elsa.Workbench.csproj" -p:RestoreLockedMode=true -v:q -nologo > "$logs/workbench-build.log" 2>&1 \
  || { tail -40 "$logs/workbench-build.log" >&2; false; }

step studio-install
(cd "$studio" && pnpm install --frozen-lockfile) > "$logs/studio-install.log" 2>&1 \
  || { tail -40 "$logs/studio-install.log" >&2; false; }

step studio-build
(cd "$studio" && pnpm build) > "$logs/studio-build.log" 2>&1 \
  || { tail -40 "$logs/studio-build.log" >&2; false; }

step studio-host-build
dotnet build "$studio/src/apps/Elsa.Studio.Web/Elsa.Studio.Web.csproj" -v:q -nologo > "$logs/studio-host-build.log" 2>&1 \
  || { tail -40 "$logs/studio-host-build.log" >&2; false; }

step browser-install
(cd "$here" && npm ci --no-audit --no-fund && npx playwright install chromium) > "$logs/browser-install.log" 2>&1 \
  || { tail -40 "$logs/browser-install.log" >&2; false; }

step workbench-start
dotnet run --no-build --project "$root/src/apps/Elsa.Workbench/Elsa.Workbench.csproj" --launch-profile http \
  > "$logs/workbench.log" 2>&1 &
workbench_pid=$!
wait_for "Workbench" "curl -fsS $workbench_url/health/ready 2>/dev/null | grep -q '\"ready\"'" "$workbench_pid"

step studio-start
Studio__BackendBaseUrl="$workbench_url" dotnet run --no-build --project "$studio/src/apps/Elsa.Studio.Web/Elsa.Studio.Web.csproj" \
  --launch-profile http > "$logs/studio.log" 2>&1 &
studio_pid=$!
wait_for "Studio" "curl -fsS -o /dev/null $studio_url/studio-runtime.js 2>/dev/null" "$studio_pid"

cat > "$state/env" <<EOF
WORKBENCH_URL=$workbench_url
STUDIO_URL=$studio_url
WORKBENCH_PID=$workbench_pid
STUDIO_PID=$studio_pid
FOUNDATION_DIR=$root
STUDIO_DIR=$studio
FOUNDATION_SHA=$(git -C "$root" rev-parse HEAD)
STUDIO_SHA=$(git -C "$studio" rev-parse HEAD)
LOGS=$logs
EOF
stage="done"
echo "Ready: Studio $studio_url, Workbench $workbench_url. State in $state/env; stop with tools/ux-qa/teardown.sh."
