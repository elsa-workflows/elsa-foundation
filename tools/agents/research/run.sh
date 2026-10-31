#!/usr/bin/env bash
set -euo pipefail

# Keep the research dependency graph outside Foundation's central package pins.
research_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
probe_dir="$(mktemp -d "${TMPDIR:-/tmp}/elsa-agent-framework.XXXXXX")"
probe_dir="$(cd -- "$probe_dir" && pwd -P)"
trap 'rm -rf -- "$probe_dir"' EXIT
if [[ "${1:-}" == "--foundation-compatibility" ]]; then
    foundation_root="$(cd -- "$research_dir/../../.." && pwd -P)"
    cp "$research_dir/Compatibility.cs" "$probe_dir/Program.cs"
    cp "$research_dir/compatibility.packages.lock.json" "$probe_dir/packages.lock.json"
    python3 - "$research_dir/Compatibility.csproj.template" "$foundation_root" "$probe_dir/Probe.csproj" <<'PY'
import pathlib, sys
from xml.sax.saxutils import escape
template, root, output = sys.argv[1:]
pathlib.Path(output).write_text(pathlib.Path(template).read_text().replace('__FOUNDATION_ROOT__', escape(root, {'"': '&quot;'})))
PY
    dotnet restore "$probe_dir/Probe.csproj" --locked-mode --verbosity quiet
    dotnet run --no-restore --project "$probe_dir/Probe.csproj"
    exit
fi
cp "$research_dir/Program.cs" "$probe_dir/Program.cs"
cp "$research_dir/Probe.csproj.template" "$probe_dir/Probe.csproj"
cp "$research_dir/packages.lock.json" "$probe_dir/packages.lock.json"
dotnet restore "$probe_dir/Probe.csproj" --locked-mode --verbosity quiet
dotnet run --no-restore --project "$probe_dir/Probe.csproj" -- --write-session "$probe_dir/session.json" "$@"
dotnet run --no-build --no-restore --project "$probe_dir/Probe.csproj" -- --restore-only "$probe_dir/session.json"
