#!/usr/bin/env bash
set -euo pipefail
: "${KERNEL_PREVIEW_POSTGRES:?Supply a fresh, dedicated PostgreSQL database}"
cd "$(dirname "$0")"
writer=${1:-owned}
fence=${2:-checked}
[[ "$writer" == owned || "$writer" == ef ]] && [[ "$fence" == checked || "$fence" == in-batch ]] || exit 2
dotnet restore --locked-mode
dotnet build -c Release --no-restore
run=(dotnet bin/Release/net10.0/Kernel.Native.Run.dll pg-root-operators "$writer" "$fence")
admitted="$("${run[@]}" admit)"
printf '%s\n' "$admitted"
read -r phase pid a b c <<<"${admitted##*$'\n'}"
[[ "$phase" == admitted && "$pid" =~ ^[1-9][0-9]*$ && -n "$a" && -n "$b" && -n "$c" ]]
cold="$("${run[@]}" read "$a" "$b" "$c")"
printf '%s\n' "$cold"
read -r phase pid2 a2 b2 c2 <<<"${cold##*$'\n'}"
[[ "$phase" == read && "$pid2" =~ ^[1-9][0-9]*$ && "$pid" != "$pid2" && "$a2" == "$a" && "$b2" == "$b" && "$c2" == "$c" ]]
