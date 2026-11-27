#!/usr/bin/env bash
set -euo pipefail

runner=essentials/tests/ActorFoundation/Native/Run
output=bin/Release/net10.0/Kernel.Native.Run.dll
repo=$(git -C "$(dirname "$0")" -c core.fsmonitor=false rev-parse --show-toplevel)
prior_revision=fad467643e13087ea77707fb41ee5c667ba4479b
shipped_revision=8b97c31d018d1a2b5aa2dd0f38fd947f4d2bbd73
provider=PostgreSQL
definition_engine=1
prior_markers='1=3/27/1 2=1/6/0'
candidate_markers='1=3/28/1 2=1/7/0'
# shellcheck source=shipped-compatibility-pair.sh
source "$repo/$runner/shipped-compatibility-pair.sh"
read_compatibility_pair "$@"
fixture_revision=$(git -C "$repo" -c core.fsmonitor=false rev-parse HEAD)
logs=$(mktemp -d "$receipt_directory/kernel-postgres-compatibility.XXXXXXXX")
scratch=$(mktemp -d)
owner=
container=
cleanup() {
    if [[ -n "$owner" ]]; then
        kill -TERM "$owner" 2>/dev/null || true
        wait "$owner" 2>/dev/null || true
    fi
    if [[ -n "$container" ]]; then docker stop --time 3 "$container" >/dev/null; fi
    rm -rf -- "$scratch"
}
trap cleanup EXIT
echo "logs $logs"
record_compatibility_pair > "$logs/compatibility-pair.txt"

build_driver() {
    local name=$1 revision=$2 source="$scratch/$1"
    mkdir "$source"
    git -C "$repo" -c core.fsmonitor=false archive "$revision" | tar -xf - -C "$source"
    # Overlay the fixture and launcher only. Each archived product project keeps its own source and dependencies.
    git -C "$repo" -c core.fsmonitor=false show "$fixture_revision:$runner/PostgresShippedCompatibilityProcess.cs" \
        > "$source/$runner/PostgresShippedCompatibilityProcess.cs"
    cat > "$source/$runner/Program.cs" <<'CS'
using Kernel.Native.Run;
return await PostgresShippedCompatibilityProcess.RunAsync(args);
CS
    (cd "$source" && dotnet build "$runner" -c Release -p:RestoreLockedMode=true -p:SourceRevisionId="$revision" -maxcpucount:2) \
        > "$logs/$name-build.log" 2>&1
    local assemblies="$source/$runner/bin/Release/net10.0"
    local provider store
    provider=$(git -c core.fsmonitor=false hash-object "$assemblies/Kernel.Foundation.Postgres.dll")
    store=$(git -c core.fsmonitor=false hash-object "$assemblies/Kernel.EntityFramework.dll")
    printf '%s %s postgres-provider %s ef-store %s\n' "$name" "$revision" "$provider" "$store"
}

{
    printf 'fixture %s\n' "$fixture_revision"
    build_driver prior "$prior_revision"
    build_driver shipped "$shipped_revision"
} | tee "$logs/build-provenance.txt"

prior="$scratch/prior/$runner/$output"
candidate="$scratch/shipped/$runner/$output"
[[ $(git -c core.fsmonitor=false hash-object "$(dirname "$prior")/Kernel.EntityFramework.dll") != \
   "$(git -c core.fsmonitor=false hash-object "$(dirname "$candidate")/Kernel.EntityFramework.dll")" ]]

export POSTGRES_PASSWORD
POSTGRES_PASSWORD=$(openssl rand -hex 24)
container=$(docker run -d --rm -e POSTGRES_PASSWORD -p 127.0.0.1::5432 postgres:17)
for ((attempt = 0; attempt < 120; attempt++)); do
    if docker exec "$container" pg_isready -h 127.0.0.1 -U postgres >/dev/null; then break; fi
    sleep 0.25
done
docker exec "$container" pg_isready -h 127.0.0.1 -U postgres >/dev/null
binding=$(docker port "$container" 5432/tcp)
export KERNEL_COMPAT_POSTGRES_PORT="${binding##*:}"
export KERNEL_COMPAT_POSTGRES_PASSWORD="$POSTGRES_PASSWORD"
docker inspect "$container" --format '{{.Image}}' > "$logs/postgres-image.txt"
docker exec "$container" postgres --version >> "$logs/postgres-image.txt"

sql() { docker exec "$container" psql -XAtq -U postgres -d "$1" -v ON_ERROR_STOP=1 -c "$2"; }

fingerprint() {
    local database=$1 live=$2
    {
        docker exec "$container" pg_dump -U postgres -d "$database" --schema-only --schema=public --no-owner --no-privileges |
            awk '$1 != "\\restrict" && $1 != "\\unrestrict"'
        # Record only hashes of table contents. Mask the active owner's heartbeat clocks and preserve its identity and advertised format.
        docker exec -i "$container" psql -XAtq -U postgres -d "$database" -v ON_ERROR_STOP=1 -v live="$live" <<'SQL'
BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
SELECT format(
    'SELECT %L, md5(coalesce(string_agg(data::text, chr(10) ORDER BY data::text), '''')) FROM (SELECT %s AS data FROM %I.%I row) data',
    tablename,
    CASE WHEN :'live' = 'yes' AND tablename = 'Nodes' THEN 'to_jsonb(row) - ''SeenAt'' - ''LeaseUntil''' ELSE 'to_jsonb(row)' END,
    schemaname, tablename)
FROM pg_tables WHERE schemaname = 'public' ORDER BY tablename;
\gexec
SELECT format('SELECT %L, last_value, is_called FROM %I.%I', c.relname, n.nspname, c.relname)
FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE n.nspname = 'public' AND c.relkind = 'S' ORDER BY c.relname;
\gexec
COMMIT;
SQL
    } | shasum -a 256
}

wait_line() {
    local pattern=$1 log=$2
    for ((attempt = 0; attempt < 600; attempt++)); do
        if grep -qE "$pattern" "$log"; then return; fi
        kill -0 "$owner" 2>/dev/null || break
        sleep 0.05
    done
    echo "Missing $pattern in $log" >&2
    return 1
}

probe() { dotnet "$1" "$2" "$lane" "$3" "$fence"; }
start_owner() {
    (exec dotnet "$1" seed "$lane" "$2" "$fence") > "$3" 2>&1 &
    owner=$!
}

expect_refusal() {
    local driver=$1 role=$2 file=$3 message=$4 log=$5 code=0
    probe "$driver" "$role" "$file" > "$log" 2>&1 || code=$?
    if [[ "$code" == 8 ]] && grep -Fq "refused format $role $message" "$log"; then return; fi
    echo "Expected refused $role with exit 8, got $code. See $log." >&2
    return 1
}

marker='SELECT string_agg("Id" || $$=$$ || "StoreSchema" || $$/$$ || "MessageProtocol" || $$/$$ || "JournalCodec", $$ $$ ORDER BY "Id") FROM "KernelStoreVersions"'
roles=(install cold-owner take sweeper projector edge journal)
for fence in checked in-batch; do
    lane="$scratch/$fence"
    out="$logs/$fence"
    mkdir "$lane" "$out"
    touch "$lane/.postgres-compatibility-rehearsal"
    prior_db="kernel_compat_${fence//-/_}_prior"
    candidate_db="kernel_compat_${fence//-/_}_candidate"
    docker exec "$container" createdb -U postgres "$prior_db"
    docker exec "$container" createdb -U postgres "$candidate_db"

    start_owner "$prior" prior "$out/prior-owner.log"
    wait_line '^ready prior partition [0-9]+ epoch 1$' "$out/prior-owner.log"
    [[ $(sql "$prior_db" "$marker") == "$prior_markers" ]]
    sql "$prior_db" 'SELECT count(*) FROM "Partitions"' > "$out/prior-partition-count.txt"
    fingerprint "$prior_db" yes > "$out/prior-before-live.sha256"
    for role in "${roles[@]}"; do
        expect_refusal "$candidate" "$role" prior "$candidate_refusal" "$out/candidate-prior-live-$role-refused.log"
        fingerprint "$prior_db" yes > "$out/prior-after-live-$role.sha256"
        cmp "$out/prior-before-live.sha256" "$out/prior-after-live-$role.sha256"
    done
    touch "$lane/continue-prior"
    wait_line '^continued prior partition [0-9]+ epoch 1$' "$out/prior-owner.log"
    touch "$lane/stop-prior"
    wait "$owner"
    owner=
    fingerprint "$prior_db" no > "$out/prior-before-cold.sha256"
    for role in install cold-owner take; do
        expect_refusal "$candidate" "$role" prior "$candidate_refusal" "$out/candidate-prior-cold-$role-refused.log"
        fingerprint "$prior_db" no > "$out/prior-after-cold-$role.sha256"
        cmp "$out/prior-before-cold.sha256" "$out/prior-after-cold-$role.sha256"
    done
    echo "ok $fence candidate roles refused the live and closed prior"

    start_owner "$candidate" candidate "$out/candidate-owner.log"
    wait_line '^ready candidate partition [0-9]+ epoch 1$' "$out/candidate-owner.log"
    [[ $(sql "$candidate_db" "$marker") == "$candidate_markers" ]]
    sql "$candidate_db" 'SELECT count(*) FROM "Partitions"' > "$out/candidate-partition-count.txt"
    fingerprint "$candidate_db" yes > "$out/candidate-before-live.sha256"
    for role in "${roles[@]}"; do
        expect_refusal "$prior" "$role" candidate "$prior_refusal" "$out/prior-candidate-live-$role-refused.log"
        fingerprint "$candidate_db" yes > "$out/candidate-after-live-$role.sha256"
        cmp "$out/candidate-before-live.sha256" "$out/candidate-after-live-$role.sha256"
    done
    touch "$lane/continue-candidate"
    wait_line '^continued candidate partition [0-9]+ epoch 1$' "$out/candidate-owner.log"
    touch "$lane/stop-candidate"
    wait "$owner"
    owner=
    fingerprint "$candidate_db" no > "$out/candidate-before-cold.sha256"
    for role in install cold-owner take; do
        expect_refusal "$prior" "$role" candidate "$prior_refusal" "$out/prior-candidate-cold-$role-refused.log"
        fingerprint "$candidate_db" no > "$out/candidate-after-cold-$role.sha256"
        cmp "$out/candidate-before-cold.sha256" "$out/candidate-after-cold-$role.sha256"
    done
    echo "ok $fence prior roles refused the live and closed candidate"

    journal_position=$(sql "$candidate_db" 'SELECT max("Position") FROM "Journal"')
    [[ "$journal_position" =~ ^[1-9][0-9]*$ ]]
    prefix="SELECT jsonb_agg(to_jsonb(j) ORDER BY \"Partition\", \"Position\") FROM \"Journal\" j WHERE \"Position\" <= $journal_position"
    sql "$candidate_db" "$prefix" | shasum -a 256 > "$out/floor-before-journal.sha256"
    probe "$candidate" resume candidate > "$out/floor-owner.log" 2>&1
    grep -Eq '^resumed candidate partition [0-9]+ epoch 2$' "$out/floor-owner.log"
    [[ $(sql "$candidate_db" "$marker") == "$candidate_markers" ]]
    [[ $(sql "$candidate_db" 'SELECT max("Position") FROM "Journal"') -eq $((journal_position + 1)) ]]
    sql "$candidate_db" "$prefix" | shasum -a 256 > "$out/floor-after-journal.sha256"
    cmp "$out/floor-before-journal.sha256" "$out/floor-after-journal.sha256"
    for role in sweeper projector edge journal; do
        probe "$candidate" "$role" candidate > "$out/floor-$role-compatible.log"
    done
    grep -Eq '^swept [1-9][0-9]*$' "$out/floor-sweeper-compatible.log"
    printf 'ok %s PostgreSQL pre-GA recreation and tested floor %s at %s\n' "$fence" "$shipped_revision" "$candidate_markers"
done
