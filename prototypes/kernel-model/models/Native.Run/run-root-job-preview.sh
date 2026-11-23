#!/usr/bin/env bash
set -euo pipefail

: "${KERNEL_PREVIEW_POSTGRES:?Set KERNEL_PREVIEW_POSTGRES to a fresh PostgreSQL database}"
cd "$(dirname "$0")"

mode="${1:-checked}"
case "$mode" in
  checked|in-batch) ;;
  *) printf 'Use checked or in-batch.\n' >&2; exit 2 ;;
esac

dotnet restore --locked-mode
dotnet build -c Release --no-restore
dll=bin/Release/net10.0/Kernel.Native.Run.dll

waiting="$(dotnet "$dll" pg-root-job-preview "$mode" birth)"
printf '%s\n' "$waiting"
read -r phase first_pid actor neighbor epoch position bookmark <<< "$waiting"
if [[ "$phase" != waiting || ! "$first_pid" =~ ^[1-9][0-9]*$ || -z "$actor" || -z "$neighbor" ||
      ! "$epoch" =~ ^[1-9][0-9]*$ || ! "$position" =~ ^[1-9][0-9]*$ || -z "$bookmark" ]]; then
  printf 'The first owner did not produce a pending job receipt.\n' >&2
  exit 1
fi

finished="$(dotnet "$dll" pg-root-job-preview "$mode" restart "$actor" "$neighbor" "$epoch" "$position" "$bookmark")"
printf '%s\n' "$finished"
read -r phase second_pid same_actor _ <<< "$finished"
if [[ "$phase" != finished || ! "$second_pid" =~ ^[1-9][0-9]*$ || "$second_pid" == "$first_pid" || "$same_actor" != "$actor" ]]; then
  printf 'The cold owner did not complete the same actor in a new process.\n' >&2
  exit 1
fi
