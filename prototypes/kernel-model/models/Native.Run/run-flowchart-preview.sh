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
case "${2:-balanced}" in
  balanced) mode=pg-flowchart-preview ;;
  durable) mode=pg-durable-flowchart-preview ;;
  *) printf 'Use balanced or durable.\n' >&2; exit 2 ;;
esac
dll=bin/Release/net10.0/Kernel.Native.Run.dll
if ! waiting="$(dotnet "$dll" "$mode" birth "$route")"; then
  printf '%s\n' "$waiting"
  exit 1
fi
printf '%s\n' "$waiting"
read -r phase first_pid actor bookmark epoch <<< "$waiting"
if [[ "$mode" == pg-durable-flowchart-preview ]]; then
  [[ "$phase" == seeded && "$first_pid" =~ ^[1-9][0-9]*$ && -n "$actor" && -n "$bookmark" && "$epoch" =~ ^[1-9][0-9]*$ ]]
  seeded_pid="$first_pid"
  seeded_actor="$actor"
  waiting="$(dotnet "$dll" "$mode" wait "$route" "$actor" "$epoch")"
  printf '%s\n' "$waiting"
  read -r phase first_pid actor bookmark epoch <<< "$waiting"
  [[ "$first_pid" != "$seeded_pid" && "$actor" == "$seeded_actor" ]]
fi
if [[ "$phase" != waiting || ! "$first_pid" =~ ^[1-9][0-9]*$ || -z "$actor" || -z "$bookmark" || ! "$epoch" =~ ^[1-9][0-9]*$ ]]; then
  printf 'The birth process did not produce a waiting receipt.\n' >&2
  exit 1
fi

if ! finished="$(dotnet "$dll" "$mode" restart "$route" "$actor" "$bookmark" "$epoch")"; then
  printf '%s\n' "$finished"
  exit 1
fi
printf '%s\n' "$finished"
read -r phase second_pid completed _ <<< "$finished"
if [[ "$phase" != finished || ! "$second_pid" =~ ^[1-9][0-9]*$ || "$first_pid" == "$second_pid" || "$actor" != "$completed" ]]; then
  printf 'The restart did not finish the same actor in another process.\n' >&2
  exit 1
fi
