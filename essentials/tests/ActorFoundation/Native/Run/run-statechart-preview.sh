#!/usr/bin/env bash
set -euo pipefail

: "${KERNEL_PREVIEW_POSTGRES:?Set KERNEL_PREVIEW_POSTGRES to a fresh PostgreSQL database}"
cd "$(dirname "$0")"

dotnet restore --locked-mode
dotnet build -c Release --no-restore
dll=bin/Release/net10.0/Kernel.Native.Run.dll

waiting="$(dotnet "$dll" pg-statechart-preview birth)"
printf '%s\n' "$waiting"
read -r phase first_pid actor bookmark epoch revision state_hash occurrence <<< "$waiting"
if [[ "$phase" != waiting || ! "$first_pid" =~ ^[1-9][0-9]*$ || -z "$actor" || -z "$bookmark" ||
      ! "$epoch" =~ ^[1-9][0-9]*$ || ! "$revision" =~ ^[1-9][0-9]*$ || -z "$state_hash" || -z "$occurrence" ]]; then
  printf 'The first owner did not produce a waiting receipt.\n' >&2
  exit 1
fi

advanced="$(dotnet "$dll" pg-statechart-preview restart "$actor" "$bookmark" "$epoch" "$revision" "$state_hash" "$occurrence")"
printf '%s\n' "$advanced"
read -r phase second_pid same_actor _ <<< "$advanced"
if [[ "$phase" != advanced || ! "$second_pid" =~ ^[1-9][0-9]*$ || "$second_pid" == "$first_pid" || "$same_actor" != "$actor" ]]; then
  printf 'The restart did not advance the same order in a new process.\n' >&2
  exit 1
fi
