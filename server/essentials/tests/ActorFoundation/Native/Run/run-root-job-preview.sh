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
read -r phase first_pid actor neighbor approval epoch position bookmark neighbor_bookmark <<< "$waiting"
if [[ "$phase" != waiting || ! "$first_pid" =~ ^[1-9][0-9]*$ || -z "$actor" || -z "$neighbor" || -z "$approval" ||
      ! "$epoch" =~ ^[1-9][0-9]*$ || ! "$position" =~ ^[1-9][0-9]*$ || -z "$bookmark" || -z "$neighbor_bookmark" ]]; then
  printf 'The first owner did not produce a pending job receipt.\n' >&2
  exit 1
fi

advanced="$(dotnet "$dll" pg-root-job-preview "$mode" advance "$actor" "$neighbor" "$approval" "$epoch" "$position" "$bookmark" "$neighbor_bookmark")"
printf '%s\n' "$advanced"
read -r phase second_pid same_actor same_neighbor same_approval successor_epoch successor_position _ <<< "$advanced"
if [[ "$phase" != successor || ! "$second_pid" =~ ^[1-9][0-9]*$ || "$second_pid" == "$first_pid" ||
      "$same_actor" != "$actor" || "$same_neighbor" != "$neighbor" || "$same_approval" != "$approval" ||
      ! "$successor_epoch" =~ ^[1-9][0-9]*$ || ! "$successor_position" =~ ^[1-9][0-9]*$ ]]; then
  printf 'The cold owner did not persist both successor tickets.\n' >&2
  exit 1
fi

finished="$(dotnet "$dll" pg-root-job-preview "$mode" restart "$actor" "$neighbor" "$approval" "$successor_epoch" "$successor_position" "$bookmark" "$neighbor_bookmark")"
printf '%s\n' "$finished"
read -r phase third_pid same_actor _ <<< "$finished"
if [[ "$phase" != finished || ! "$third_pid" =~ ^[1-9][0-9]*$ || "$third_pid" == "$first_pid" ||
      "$third_pid" == "$second_pid" || "$same_actor" != "$actor" ]]; then
  printf 'The third owner did not complete the same actor in a new process.\n' >&2
  exit 1
fi
