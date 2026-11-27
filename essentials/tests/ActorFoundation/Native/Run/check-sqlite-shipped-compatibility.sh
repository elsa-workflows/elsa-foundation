#!/usr/bin/env bash
set -euo pipefail

runner=essentials/tests/ActorFoundation/Native/Run
output=bin/Release/net10.0/Kernel.Native.Run.dll
repo=$(git -C "$(dirname "$0")" -c core.fsmonitor=false rev-parse --show-toplevel)
prior_revision=92c54ac8797af9d087a8046c28aa5e1ac5ff8a4b
floor_revision=fad467643e13087ea77707fb41ee5c667ba4479b
candidate_revision=$(git -C "$repo" -c core.fsmonitor=false rev-parse HEAD)
logs=$(mktemp -d "${1:-${TMPDIR:-/tmp}}/kernel-sqlite-compatibility.XXXXXXXX")
scratch=$(mktemp -d)
owner=
cleanup() {
    if [[ -n "$owner" ]]; then
        kill -TERM "$owner" 2>/dev/null || true
        wait "$owner" 2>/dev/null || true
    fi
    rm -rf -- "$scratch"
}
trap cleanup EXIT
echo "logs $logs"

build_driver() {
    local name=$1 revision=$2 source="$scratch/$1"
    mkdir "$source"
    git -C "$repo" -c core.fsmonitor=false archive "$revision" | tar -xf - -C "$source"
    if [[ "$name" != candidate ]]; then
        # Only the two fixture files are overlaid. The shipping provider is compiled from its pinned archive.
        cp "$scratch/candidate/$runner/SqliteShippedCompatibilityProcess.cs" "$source/$runner/"
        cat > "$source/$runner/Program.cs" <<'CS'
using Kernel.Native.Run;
return await SqliteShippedCompatibilityProcess.RunAsync(args.Skip(1).ToArray());
CS
    fi
    (cd "$source" && dotnet build "$runner" -c Release -p:RestoreLockedMode=true -p:SourceRevisionId="$revision" -maxcpucount:2) \
        > "$logs/$name-build.log" 2>&1
    local assemblies="$source/$runner/bin/Release/net10.0"
    local sqlite_hash store_hash
    sqlite_hash=$(git -c core.fsmonitor=false hash-object "$assemblies/Kernel.Foundation.Sqlite.dll")
    store_hash=$(git -c core.fsmonitor=false hash-object "$assemblies/Kernel.EntityFramework.dll")
    printf '%s %s sqlite-provider %s ef-store %s\n' "$name" "$revision" "$sqlite_hash" "$store_hash"
}

{
    build_driver candidate "$candidate_revision"
    build_driver prior "$prior_revision"
    build_driver floor "$floor_revision"
} | tee "$logs/build-provenance.txt"

prior="$scratch/prior/$runner/$output"
candidate="$scratch/candidate/$runner/$output"
floor="$scratch/floor/$runner/$output"
[[ $(git -c core.fsmonitor=false hash-object "$(dirname "$prior")/Kernel.EntityFramework.dll") != \
   "$(git -c core.fsmonitor=false hash-object "$(dirname "$candidate")/Kernel.EntityFramework.dll")" ]]

wait_line() {
    local pattern=$1 log=$2
    for ((attempt = 0; attempt < 300; attempt++)); do
        if grep -qE "$pattern" "$log"; then return; fi
        kill -0 "$owner" 2>/dev/null || break
        sleep 0.05
    done
    echo "Missing $pattern in $log" >&2
    return 1
}

probe() {
    local driver=$1 role=$2 file=$3
    dotnet "$driver" sqlite-shipped-compatibility "$role" "$lane" "$file" "$fence"
}

start_owner() {
    local driver=$1 file=$2 log=$3
    (exec dotnet "$driver" sqlite-shipped-compatibility seed "$lane" "$file" "$fence") > "$log" 2>&1 &
    owner=$!
}

expect_refusal() {
    local expected_code=$1 message=$2 driver=$3 role=$4 file=$5 log=$6 code=0
    probe "$driver" "$role" "$file" > "$log" 2>&1 || code=$?
    if [[ "$code" == "$expected_code" ]] && grep -Fq "$message" "$log"; then return; fi
    echo "Expected refusal $expected_code, got $code. See $log." >&2
    return 1
}

fingerprint() {
    probe "$candidate" snapshot "$1" | grep -E '^[A-F0-9]{64}$'
}

prior_refusal='Stored schema=3, message protocol=27, journal codec=1. This build reads schema=3, message protocol=26, journal codec=1.'
candidate_refusal='Stored schema=3, message protocol=26, journal codec=1. This build reads schema=3, message protocol=27, journal codec=1.'
for fence in checked in-batch; do
    lane="$scratch/data-$fence"
    out="$logs/$fence"
    mkdir "$lane" "$out"
    touch "$lane/.sqlite-compatibility-rehearsal"

    start_owner "$prior" prior "$out/prior-owner.log"
    wait_line '^ready prior epoch 1$' "$out/prior-owner.log"
    grep -Fxq 'installed 1=3/26/1 2=2/9/0' "$out/prior-owner.log"
    expect_refusal 9 'refused host lock install' "$candidate" install prior "$out/candidate-live-install-refused.log"
    touch "$lane/continue-prior"
    wait_line '^continued prior epoch 1$' "$out/prior-owner.log"
    grep -Fxq 'live snapshot unchanged except lease timestamps' "$out/prior-owner.log"
    touch "$lane/stop-prior"
    wait "$owner"
    owner=
    grep -Fxq 'stopped prior epoch 1' "$out/prior-owner.log"

    fingerprint prior > "$out/prior-before-closed-probes.sha256"
    for role in install cold-owner; do
        expect_refusal 8 "$candidate_refusal" "$candidate" "$role" prior "$out/candidate-prior-$role-refused.log"
        fingerprint prior > "$out/prior-after-$role.sha256"
        cmp "$out/prior-before-closed-probes.sha256" "$out/prior-after-$role.sha256"
    done
    probe "$prior" projected-list prior > "$out/prior-list-compatible.log"
    probe "$prior" journal prior > "$out/prior-journal-compatible.log"
    probe "$prior" sweeper prior > "$out/prior-sweep-compatible.log"
    grep -Eq '^swept [1-9][0-9]*$' "$out/prior-sweep-compatible.log"

    start_owner "$candidate" candidate "$out/candidate-owner.log"
    wait_line '^ready candidate epoch 1$' "$out/candidate-owner.log"
    grep -Fxq 'installed 1=3/27/1 2=2/10/0' "$out/candidate-owner.log"
    expect_refusal 9 'refused host lock cold-owner' "$prior" cold-owner candidate "$out/prior-live-start-refused.log"
    expect_refusal 9 'refused host lock projected-list' "$prior" projected-list candidate "$out/prior-live-read-refused.log"
    touch "$lane/continue-candidate"
    wait_line '^continued candidate epoch 1$' "$out/candidate-owner.log"
    grep -Fxq 'live snapshot unchanged except lease timestamps' "$out/candidate-owner.log"
    touch "$lane/stop-candidate"
    wait "$owner"
    owner=
    grep -Fxq 'stopped candidate epoch 1' "$out/candidate-owner.log"

    fingerprint candidate > "$out/candidate-before-closed-probes.sha256"
    for role in install cold-owner sweeper projected-list journal; do
        expect_refusal 8 "$prior_refusal" "$prior" "$role" candidate "$out/prior-candidate-$role-refused.log"
        fingerprint candidate > "$out/candidate-after-$role.sha256"
        cmp "$out/candidate-before-closed-probes.sha256" "$out/candidate-after-$role.sha256"
        printf 'ok %s prior %s refuses the closed candidate file without changing it\n' "$fence" "$role"
    done

    probe "$floor" resume candidate > "$out/floor-owner.log" 2>&1
    grep -Fxq 'resumed exact actor at 1=3/27/1 2=2/10/0 epoch 2 with its physical journal prefix' "$out/floor-owner.log"
    probe "$floor" projected-list candidate > "$out/floor-list-compatible.log"
    probe "$floor" journal candidate > "$out/floor-journal-compatible.log"
    probe "$floor" sweeper candidate > "$out/floor-sweep-compatible.log"
    grep -Eq '^swept [1-9][0-9]*$' "$out/floor-sweep-compatible.log"
    printf 'ok %s SQLite pre-GA recreation, process exclusion and tested floor %s at 3/27/1 plus 2/10/0\n' "$fence" "$floor_revision"
done
