#!/usr/bin/env bash
set -euo pipefail

research_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
foundation_root="$(cd -- "$research_dir/../../../.." && pwd -P)"
if [[ "${1:-}" == "--output" ]]; then
    [[ $# -ge 2 ]] || { echo '--output requires a new directory.' >&2; exit 2; }
    mkdir -- "$2" # Refuse an existing evidence directory.
    probe_dir="$(cd -- "$2" && pwd -P)"
    shift 2
else
    probe_dir="$(mktemp -d "${TMPDIR:-/tmp}/elsa-streaming-protocol.XXXXXX")"
    probe_dir="$(cd -- "$probe_dir" && pwd -P)"
fi
if [[ "$probe_dir/" == "$foundation_root/"* ]]; then
    rmdir -- "$probe_dir" # Only the empty directory just created by this invocation.
    echo 'The probe project must be outside this checkout; choose an external --output directory.' >&2
    exit 2
fi
printf 'Retained research evidence: %s\n' "$probe_dir"
for flag in "$@"; do
    case "$flag" in
        --drop-terminal-guard) ;;
        *) echo "Unknown probe flag: $flag" >&2; exit 2 ;;
    esac
done
cp "$research_dir/Program.cs" "$probe_dir/Program.cs"
cp "$research_dir/Probe.csproj.template" "$probe_dir/Probe.csproj"
cp "$research_dir/packages.lock.json.template" "$probe_dir/packages.lock.json"
cp "$foundation_root/global.json" "$probe_dir/global.json"
# Stop unrelated machine/project parents from contributing MSBuild or package settings.
for barrier in Directory.Build.props Directory.Build.targets Directory.Packages.props; do
    printf '<Project />\n' > "$probe_dir/$barrier"
done
cat > "$probe_dir/NuGet.Config" <<'XML'
<configuration><packageSources><clear/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources></configuration>
XML
(
    cd -- "$probe_dir"
    dotnet --version > sdk-version.txt
    dotnet restore Probe.csproj --force-evaluate --verbosity quiet > normal-restore.log 2>&1
    cmp packages.lock.json "$research_dir/packages.lock.json.template"
    dotnet restore Probe.csproj --locked-mode --force --verbosity quiet > locked-restore.log 2>&1
    dotnet build Probe.csproj --no-restore --configuration Release --verbosity quiet > build.log 2>&1
)
probe_exit=0
dotnet run --project "$probe_dir/Probe.csproj" --no-build --no-restore --configuration Release -- "$probe_dir" "$@" > "$probe_dir/probe.log" 2>&1 || probe_exit=$?
cat "$probe_dir/probe.log"
if [[ ! -f "$probe_dir/report.json" && $probe_exit -eq 0 ]]; then probe_exit=1; fi
python3 - "$probe_dir" "$research_dir" "$probe_exit" "$@" <<'PY'
import hashlib, json, pathlib, sys
output, source = map(pathlib.Path, sys.argv[1:3])
files = ['Program.cs', 'Probe.csproj.template', 'packages.lock.json.template', 'run.sh']
lock = json.loads((output / 'packages.lock.json').read_text())['dependencies']['net10.0']
report = output / 'report.json'
evidence = {
    'probeExitCode': int(sys.argv[3]), 'controls': sys.argv[4:],
    'sdkVersion': (output / 'sdk-version.txt').read_text().strip(),
    'sourceSha256': {name: hashlib.sha256((source / name).read_bytes()).hexdigest() for name in files},
    'normalRestore': 'Passed', 'lockedRestore': 'Passed', 'build': 'Passed',
    'resolvedPackages': {name: value['resolved'] for name, value in lock.items()},
    'reportPresent': report.is_file(),
    'reportSha256': hashlib.sha256(report.read_bytes()).hexdigest() if report.is_file() else None,
    'boundary': 'Research fixture only; no live model, sockets, Elsa runtime or durability proof.'
}
(output / 'evidence.json').write_text(json.dumps(evidence, indent=2) + '\n')
PY
exit "$probe_exit"
