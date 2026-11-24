#!/usr/bin/env bash
set -euo pipefail

: "${KERNEL_PREVIEW_POSTGRES:?Supply a fresh disposable PostgreSQL database}"
cd "$(dirname "$0")"

mode="${1:-checked}"
case "$mode" in
  checked|in-batch) ;;
  *) printf 'Use checked or in-batch.\n' >&2; exit 2 ;;
esac

dotnet build -c Release -p:RestoreLockedMode=true
dll=bin/Release/net10.0/Kernel.Native.Run.dll

initial="$(dotnet "$dll" pg-root-value-check "$mode" birth)"
printf '%s\n' "$initial"
read -r phase first_pid actor epoch <<< "$initial"
if [[ "$phase" != value-ready || ! "$first_pid" =~ ^[1-9][0-9]*$ || ! "$actor" =~ ^[0-9A-F]{64}$ || ! "$epoch" =~ ^[1-9][0-9]*$ ]]; then
  printf 'The first owner did not return a durable value receipt.\n' >&2
  exit 1
fi

verified="$(dotnet "$dll" pg-root-value-check "$mode" restart "$actor" "$epoch")"
printf '%s\n' "$verified"
read -r phase second_pid same_actor newer_epoch <<< "$verified"
if [[ "$phase" != value-verified || ! "$second_pid" =~ ^[1-9][0-9]*$ || "$first_pid" == "$second_pid" ||
      "$actor" != "$same_actor" || ! "$newer_epoch" =~ ^[1-9][0-9]*$ || "$newer_epoch" -le "$epoch" ]]; then
  printf 'The second process did not take a newer fence for the same actor.\n' >&2
  exit 1
fi
