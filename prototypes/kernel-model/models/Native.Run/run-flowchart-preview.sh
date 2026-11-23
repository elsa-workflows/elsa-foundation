#!/usr/bin/env bash
set -euo pipefail

: "${KERNEL_PREVIEW_POSTGRES:?Set KERNEL_PREVIEW_POSTGRES to a fresh PostgreSQL database}"
cd "$(dirname "$0")"
route="${1:-manual}"
case "$route" in
  manual|automatic) ;;
  *) printf 'Use manual or automatic.\n' >&2; exit 2 ;;
esac

dotnet restore --locked-mode
dotnet build -c Release --no-restore
dll=bin/Release/net10.0/Kernel.Native.Run.dll
if ! waiting="$(dotnet "$dll" pg-flowchart-preview birth "$route")"; then
  printf '%s\n' "$waiting"
  exit 1
fi
printf '%s\n' "$waiting"
read -r phase first_pid actor bookmark epoch <<< "$waiting"
test "$phase" = waiting && test -n "$first_pid" && test -n "$actor" && test -n "$bookmark" && test -n "$epoch"

if ! finished="$(dotnet "$dll" pg-flowchart-preview restart "$route" "$actor" "$bookmark" "$epoch")"; then
  printf '%s\n' "$finished"
  exit 1
fi
printf '%s\n' "$finished"
read -r phase second_pid completed _ <<< "$finished"
test "$phase" = finished && test -n "$second_pid" && test "$first_pid" != "$second_pid" && test "$actor" = "$completed"
