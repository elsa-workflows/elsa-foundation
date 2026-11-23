#!/usr/bin/env bash
set -euo pipefail
: "${KERNEL_PREVIEW_POSTGRES:?Supply a fresh, dedicated PostgreSQL database}"
cd "$(dirname "$0")"
writer=${1:-owned}
fence=${2:-checked}
[[ "$writer" == owned || "$writer" == ef ]] && [[ "$fence" == checked || "$fence" == in-batch ]] || { echo "Usage: $0 [owned|ef] [checked|in-batch]" >&2; exit 2; }
dotnet restore --locked-mode
dotnet build -c Release --no-restore
case "${3:-balanced}" in
  balanced) preview=pg-delay-preview ;;
  durable) preview=pg-durable-delay-preview ;;
  *) echo "Use balanced or durable for the persistence mode." >&2; exit 2 ;;
esac
run=(dotnet bin/Release/net10.0/Kernel.Native.Run.dll "$preview" "$writer" "$fence")
waiting="$("${run[@]}" birth)"
printf '%s\n' "$waiting"
read -r phase pid actor epoch due issued handle cursor <<<"$waiting"
[[ "$phase" == waiting && "$pid" =~ ^[1-9][0-9]*$ && "$epoch" =~ ^[1-9][0-9]*$ && -n "$cursor" ]]
finished="$("${run[@]}" restart "$actor" "$epoch" "$due" "$handle" "$cursor")"
printf '%s\n' "$finished"
read -r phase next_pid next_actor _ <<<"$finished"
[[ "$phase" == finished && "$next_pid" =~ ^[1-9][0-9]*$ && "$next_pid" != "$pid" && "$next_actor" == "$actor" ]]
