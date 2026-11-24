#!/usr/bin/env bash
set -euo pipefail

runner=essentials/tests/ActorFoundation/Store/EntityFramework
legacy_runner=ef/Kernel.Ef.Conformance
output=bin/Release/net10.0/Kernel.Ef.Conformance.dll
repo=$(git -C "$(dirname "$0")" -c core.fsmonitor=false rev-parse --show-toplevel)
old_revision=08caf14c0e3248e1ddc2caac6643be70c3b9921f
floor_revision=6f38d986481115e4cbb86bd09ca3b9120b17047a
logs=$(mktemp -d "${1:-${TMPDIR:-/tmp}}/kernel-mixed-version.XXXXXXXX")
scratch=$(mktemp -d)
old_pid=
new_pid=
floor_pid=
container=
cleanup() {
    for pid in "$old_pid" "$new_pid" "$floor_pid"; do
        if [[ -n "$pid" ]]; then
            kill -TERM "$pid" 2>/dev/null || true
            wait "$pid" 2>/dev/null || true
        fi
    done
    if [[ -n "$container" ]]; then docker stop --time 3 "$container" >/dev/null; fi
    rm -rf -- "$scratch"
}
trap cleanup EXIT
echo "logs $logs"

new_revision=$(git -C "$repo" -c core.fsmonitor=false rev-parse HEAD)
source="$scratch/new"
build_driver() {
    local name=$1 revision=$2
    local destination="$scratch/$name"
    mkdir -p "$destination"
    git -C "$repo" -c core.fsmonitor=false archive "$revision" | tar -xf - -C "$destination"
    local project=$runner
    local build_root=$destination
    if [[ "$name" != new ]]; then
        # The disposable driver and its database guard are the only additions to the archived test project.
        project=$legacy_runner
        build_root="$destination/prototypes/kernel-model"
        cp "$source/$runner/FormatRehearsalProcess.cs" "$source/essentials/tests/ActorFoundation/Native/Run/PostgresFixtureDatabase.cs" "$build_root/$project/"
        cat > "$build_root/$project/Program.cs" <<'CS'
using Kernel.Ef.Conformance;
return await FormatRehearsalProcess.RunAsync(args);
CS
    fi
    (cd "$build_root" && dotnet build "$project" -c Release -p:RestoreLockedMode=true) > "$logs/$name-build.log" 2>&1
    printf '%s %s %s\n' "$name" "$revision" "$(git -c core.fsmonitor=false hash-object "$build_root/$project/bin/Release/net10.0/Kernel.EntityFramework.dll")"
}

build_driver new "$new_revision"
build_driver old "$old_revision"
build_driver floor "$floor_revision"
old="$scratch/old/prototypes/kernel-model/$legacy_runner/$output"
floor="$scratch/floor/prototypes/kernel-model/$legacy_runner/$output"
new="$source/$runner/$output"
[[ $(git -c core.fsmonitor=false hash-object "$(dirname "$old")/Kernel.EntityFramework.dll") != $(git -c core.fsmonitor=false hash-object "$(dirname "$new")/Kernel.EntityFramework.dll") ]]

export POSTGRES_PASSWORD
POSTGRES_PASSWORD=$(openssl rand -hex 24)
container=$(docker run -d --rm -e POSTGRES_PASSWORD -p 127.0.0.1::5432 postgres:17)
for ((attempt=0; attempt<60; attempt++)); do
    if docker exec "$container" pg_isready -h 127.0.0.1 -U postgres >/dev/null; then break; fi
    sleep 0.25
done
docker exec "$container" pg_isready -h 127.0.0.1 -U postgres >/dev/null
port=$(docker port "$container" 5432/tcp)
port=${port##*:}
export KERNEL_PREVIEW_POSTGRES="Host=127.0.0.1;Port=$port;Database=postgres;Username=postgres;Password=$POSTGRES_PASSWORD"
export KERNEL_REHEARSAL_GATES="$scratch/gates"
mkdir "$KERNEL_REHEARSAL_GATES"
sql() { docker exec "$container" psql -XAtq -U postgres -d postgres -v ON_ERROR_STOP=1 -c "$1"; }
wait_line() {
    local pid=$1 file=$2 pattern=$3
    for ((attempt=0; attempt<200; attempt++)); do
        if grep -qE "$pattern" "$file"; then return; fi
        kill -0 "$pid" 2>/dev/null || break
        sleep 0.05
    done
    echo "Missing $pattern in $file" >&2
    return 1
}
expect_refusal() {
    local file=$1 message=$2 code=0
    shift 2
    "$@" > "$file" 2>&1 || code=$?
    if [[ "$code" == 8 ]] && grep -Fq "$message" "$file"; then return; fi
    echo "Expected an incompatible-format refusal (exit 8), got $code. See $file." >&2
    return 1
}
snapshot() {
    local table
    for table in KernelStoreVersions Partitions Nodes Instances Messages Events Subscriptions Journal Projections Views; do
        sql "SELECT COALESCE(jsonb_agg(to_jsonb(t) ORDER BY to_jsonb(t)), '[]'::jsonb) FROM \"$table\" t"
    done
}
marker='SELECT "StoreSchema", "MessageProtocol", "JournalCodec" FROM "KernelStoreVersions"'
position='SELECT "Position" FROM "Projections" WHERE "Partition" = 0'
revision='SELECT "Revision", "State" FROM "Instances" WHERE "Id" = $$0/format-rehearsal$$'
live_old='SELECT count(*) FROM "Nodes" WHERE "Id" = $$old-owner$$
    AND "LeaseUntil" >= floor(extract(epoch from clock_timestamp())*1000)'

dotnet "$old" old-owner > "$logs/old-owner.log" 2>&1 &
old_pid=$!
wait_line "$old_pid" "$logs/old-owner.log" '^ready old-owner epoch 1$'
[[ $(sql "$marker") == '2|1|1' && $(sql "$revision") == '1|1' && $(sql "$live_old") == 1 ]]
[[ $(sql 'SELECT count(*) FROM "Messages" WHERE "Id" = $$seed$$ AND "Done" AND "KeepUntil" = 1') == 1 ]]
dotnet "$old" sweeper > "$logs/old-sweep-compatible.log"
[[ $(sql 'SELECT count(*) FROM "Messages"') == 0 ]]
dotnet "$old" projector > "$logs/old-project-compatible.log"
[[ $(sql "$position") == 1 ]]
snapshot > "$logs/before-live-refusal.rows"
expect_refusal "$logs/live-migrate.log" "Live node 'old-owner' cannot read" dotnet "$new" postgres-rehearsal migrate
kill -0 "$old_pid"
[[ $(sql "$live_old") == 1 ]]
[[ $(sql 'SELECT "Owner", "Epoch" FROM "Partitions" WHERE "Id" = 0') == 'old-owner|1' ]]
[[ $(sql 'SELECT count(*) FROM information_schema.columns WHERE table_schema = current_schema()
    AND ((table_name = $$Instances$$ AND column_name = $$NextJobGeneration$$)
      OR (table_name = $$Messages$$ AND column_name = $$JobGeneration$$))') == 0 ]]
snapshot > "$logs/after-live-refusal.rows"
cmp "$logs/before-live-refusal.rows" "$logs/after-live-refusal.rows"
echo 'ok live old owner prevents advance and leaves the original rows and schema unchanged'

touch "$KERNEL_REHEARSAL_GATES/stop-old-owner"
wait "$old_pid"
old_pid=
for ((attempt=0; attempt<160; attempt++)); do
    if [[ $(sql "$live_old") == 0 ]]; then break; fi
    sleep 0.25
done
[[ $(sql "$live_old") == 0 ]]
dotnet "$new" postgres-rehearsal migrate > "$logs/advance.log"
grep -q '^advanced$' "$logs/advance.log"
[[ $(sql "$marker") == '3|2|1' && $(sql "$revision") == '1|1' ]]
dotnet "$new" postgres-rehearsal new-owner > "$logs/new-owner.log" 2>&1 &
new_pid=$!
wait_line "$new_pid" "$logs/new-owner.log" '^ready new-owner epoch 2$'
[[ $(sql "$revision") == '2|2' && $(sql "$position") == 1 ]]
[[ $(sql 'SELECT count(*) FROM "Messages" WHERE "Id" = $$first-new$$ AND "Done" AND "KeepUntil" = 1') == 1 ]]
[[ $(sql 'SELECT count(*) FROM "Partitions" WHERE "Id" = 1 AND "Owner" IS NULL AND "Epoch" = 0') == 1 ]]
snapshot > "$logs/before-old-probes.rows"
message='Stored schema=3, message protocol=2, journal codec=1. This build reads schema=2, message protocol=1, journal codec=1.'
for role in cold-owner take sweeper projector; do
    expect_refusal "$logs/old-$role-refused.log" "$message" dotnet "$old" "$role"
    kill -0 "$new_pid"
    snapshot > "$logs/after-old-$role.rows"
    cmp "$logs/before-old-probes.rows" "$logs/after-old-$role.rows"
    echo "ok separate old $role refused before processing"
done

touch "$KERNEL_REHEARSAL_GATES/continue-new"
wait_line "$new_pid" "$logs/new-owner.log" '^continued new-owner epoch 2$'
[[ $(sql "$revision") == '3|3' ]]
[[ $(sql 'SELECT "Owner", "Epoch", "Position" FROM "Partitions" WHERE "Id" = 0') == 'new-owner|2|3' ]]
journal='SELECT string_agg("Entries", $$,$$ ORDER BY "Position") FROM "Journal" WHERE "Partition" = 0'
prefix=$(sql "$journal")
[[ "$prefix" == '["seed"],["first-new"],["second-new"]' ]]
prefix_rows='SELECT jsonb_agg(to_jsonb(j) ORDER BY "Position") FROM "Journal" j
    WHERE "Partition" = 0 AND "Position" <= 3'
sql "$prefix_rows" > "$logs/before-floor-journal-prefix.rows"
echo 'ok the upgraded owner committed another turn at the same epoch'

touch "$KERNEL_REHEARSAL_GATES/stop-new-owner"
wait "$new_pid"
new_pid=
[[ $(sql 'SELECT "Owner" IS NULL FROM "Partitions" WHERE "Id" = 0') == t ]]

dotnet "$floor" floor-owner > "$logs/floor-owner.log" 2>&1 &
floor_pid=$!
wait_line "$floor_pid" "$logs/floor-owner.log" '^ready floor-owner epoch 3$'
[[ $(sql "$revision") == '4|4' && $(sql 'SELECT count(*) FROM "Journal"') == 4 ]]
sql "$prefix_rows" > "$logs/after-floor-journal-prefix.rows"
cmp "$logs/before-floor-journal-prefix.rows" "$logs/after-floor-journal-prefix.rows"
[[ $(sql 'SELECT "Instance", "Revision", "Entries" FROM "Journal" WHERE "Partition" = 0 AND "Position" = 4') = '0/format-rehearsal|4|["floor-turn"]' ]]
[[ $(sql 'SELECT "Owner", "Epoch", "Position" FROM "Partitions" WHERE "Id" = 0') == 'floor-owner|3|4' ]]
dotnet "$floor" sweeper > "$logs/floor-sweep.log"
dotnet "$floor" projector > "$logs/floor-project.log"
[[ $(sql 'SELECT count(*) FROM "Messages"') == 0 && $(sql "$position") == 4 ]]
[[ $(sql 'SELECT "Revision" FROM "Views" WHERE "Instance" = $$0/format-rehearsal$$') == 4 ]]
touch "$KERNEL_REHEARSAL_GATES/stop-floor-owner"
wait "$floor_pid"
floor_pid=
printf 'ok tested PostgreSQL prototype rollback floor %s for schema 3, message protocol 2, journal codec 1\n' "$floor_revision"
