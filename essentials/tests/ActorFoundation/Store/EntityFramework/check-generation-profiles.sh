#!/usr/bin/env bash
# Supply a schema-2 Release runner. The current checkout must also have both runners built.
set -euo pipefail
old=${1:?Supply the absolute path to the predecessor Kernel.Native.Run.dll}
runner=$(cd "$(dirname "$0")" && pwd)
root=$(cd "$runner/../../../../../prototypes/kernel-model" && pwd)
new="$root/models/Native.Run/bin/Release/net10.0/Kernel.Native.Run.dll"
upgrade="$runner/bin/Release/net10.0/Kernel.Ef.Conformance.dll"
[[ -f "$old" && -f "$new" && -f "$upgrade" ]]
export POSTGRES_PASSWORD
POSTGRES_PASSWORD=$(openssl rand -hex 24)
container=$(docker run -d --rm -e POSTGRES_PASSWORD -p 127.0.0.1::5432 postgres:17)
trap 'docker stop --time 3 "$container" >/dev/null' EXIT
for ((attempt=0; attempt<60; attempt++)); do
    if docker exec "$container" pg_isready -h 127.0.0.1 -U postgres >/dev/null; then break; fi
    sleep 1
done
port=$(docker port "$container" 5432/tcp)
port=${port##*:}
sql() { docker exec "$container" psql -XAtq -U postgres -d "$db" -v ON_ERROR_STOP=1 -c "$1"; }

for protocol in 1 3 4; do
    db="profile_$protocol"
    docker exec "$container" createdb -U postgres "$db"
    export KERNEL_PREVIEW_POSTGRES="Host=127.0.0.1;Port=$port;Database=$db;Username=postgres;Password=$POSTGRES_PASSWORD;SSL Mode=Disable"
    case "$protocol" in
        1) mode=pg-flowchart-preview; birth=(birth manual); next=2 ;;
        3) mode=pg-durable-flowchart-preview; birth=(birth manual); next=3 ;;
        4) mode=pg-durable-delay-preview; birth=(owned checked birth); next=4 ;;
    esac
    first=$(dotnet "$old" "$mode" "${birth[@]}")
    read -r -a state <<<"$first"
    actor=${state[2]}
    marker='SELECT "StoreSchema", "MessageProtocol", "JournalCodec" FROM "KernelStoreVersions"'
    [[ $(sql "$marker") == "2|$protocol|1" ]]
    if [[ "$protocol" != 1 ]]; then
        # A real pending continuation or timer and its schema-2 view predate the migration.
        private='SELECT "Id", "Due" FROM "FoundationRootMessages" WHERE "InboxScope"=-5 AND NOT "Done" ORDER BY "Id"'
        original=$(sql "$private")
        [[ -n "$original" ]]
    fi
    for ((attempt=0; attempt<60; attempt++)); do
        if [[ $(sql 'SELECT count(*) FROM "Nodes" WHERE "LeaseUntil">=floor(extract(epoch from clock_timestamp())*1000)') == 0 ]]; then break; fi
        sleep 0.5
    done
    [[ $(sql "SELECT to_regclass('\"FoundationActorList\"') IS NULL") == t ]]
    checkpoint='SELECT jsonb_agg(to_jsonb(i) - $$NextJobGeneration$$ ORDER BY i."Id") FROM "Instances" i'
    original_checkpoint=$(sql "$checkpoint")
    [[ -n "$original_checkpoint" ]]
    projected='SELECT count(*) FROM "FoundationActorList" l JOIN "Instances" i
        ON l."Tenant" = i."FoundationTenant" AND l."Reference" = i."FoundationReference"
        WHERE l."Header" = i."FoundationHeader" AND l."Status" = i."Status"
            AND l."EndedAt" IS NOT DISTINCT FROM i."FoundationTerminalAt"
            AND l."Type" = kernel_actor_list_filter(i."FoundationHeader")-> $$Model$$
            AND l."Labels" = kernel_actor_list_filter(i."FoundationHeader")-> $$Labels$$'
    check_backfill() {
        [[ $(sql "$marker") == "3|$next|1" && $(sql "$checkpoint") == "$original_checkpoint" ]]
        [[ $(sql 'SELECT count(*) FROM "Instances" WHERE "NextJobGeneration" IS NOT NULL') == 0 ]]
        [[ $(sql "$projected") == 1 && $(sql 'SELECT count(*) FROM "FoundationActorList"') == 1 ]]
    }
    [[ $(dotnet "$upgrade" postgres-profile-upgrade) == advanced ]]
    check_backfill
    # Recreate the outcome of an earlier coordinator that advanced the marker without installing the projection.
    sql 'DROP TRIGGER kernel_project_root_actor_list ON "Instances"; DROP TABLE "FoundationActorList"'
    [[ $(dotnet "$upgrade" postgres-profile-upgrade) == "already current" ]]
    check_backfill
    [[ $(dotnet "$upgrade" postgres-profile-upgrade) == "already current" ]]
    if [[ "$protocol" != 1 ]]; then
        [[ $(sql "$private") == "$original" ]]
        [[ $(sql 'SELECT count(*) FROM "FoundationRootMessages" WHERE "InboxScope"=-5 AND NOT "Done" AND "JobGeneration"=0') -gt 0 ]]
    fi
    if [[ "$protocol" == 3 ]]; then
        wait_receipt=$(dotnet "$new" "$mode" wait manual "$actor" "${state[4]}")
        read -r -a state <<<"$wait_receipt"
        [[ ${state[0]} == waiting && ${state[2]} == "$actor" ]]
    fi
    if [[ "$protocol" == 4 ]]; then
        finished=$(dotnet "$new" "$mode" owned checked restart "$actor" "${state[3]}" "${state[4]}" "${state[6]}" "${state[7]}")
        [[ "$finished" == *" due ${state[4]} "*" timer 1 "* ]]
    else
        finished=$(dotnet "$new" "$mode" restart manual "$actor" "${state[3]}" "${state[@]:4}")
        [[ "$finished" == *" join 1 route manual "* ]]
    fi
    read -r phase pid resumed rest <<<"$finished"
    [[ "$phase" == finished && "$resumed" == "$actor" && "$pid" != "${state[1]}" ]]
    echo "ok PostgreSQL protocol $protocol -> $next backfilled on advance and retry, resumed $actor"
done
