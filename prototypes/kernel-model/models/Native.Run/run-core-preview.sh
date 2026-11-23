#!/usr/bin/env bash
set -euo pipefail

: "${KERNEL_PREVIEW_POSTGRES:?Set KERNEL_PREVIEW_POSTGRES to a dedicated PostgreSQL database}"
cd "$(dirname "$0")"

dotnet restore --locked-mode
dotnet build -c Release --no-restore
dll=bin/Release/net10.0/Kernel.Native.Run.dll
if ! birth="$(dotnet "$dll" pg-core-preview birth)"; then
  printf '%s\n' "$birth"
  exit 1
fi
printf '%s\n' "$birth"
reference="$(awk '$1 == "reference" { print $2 }' <<< "$birth")"
bookmark="$(awk '$1 == "bookmark" { print $2 }' <<< "$birth")"
first_pid="$(awk '$1 == "owner" { print $2 }' <<< "$birth")"
test -n "$reference" && test -n "$bookmark" && test -n "$first_pid"

if ! restart="$(dotnet "$dll" pg-core-preview restart "$reference" "$bookmark")"; then
  printf '%s\n' "$restart"
  exit 1
fi
printf '%s\n' "$restart"
second_pid="$(awk '$1 == "owner" { print $2 }' <<< "$restart")"
test -n "$second_pid" && test "$second_pid" != "$first_pid"
