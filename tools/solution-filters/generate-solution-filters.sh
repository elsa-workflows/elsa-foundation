#!/usr/bin/env bash
set -euo pipefail

generator_project="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/../maps/Elsa.Maps.Generator"
command="solution-filters"

if [[ "${1:-}" == "--check" ]]; then
  command="solution-filters-check"
elif [[ $# -gt 0 ]]; then
  echo "Usage: $0 [--check]" >&2
  exit 2
fi

dotnet run --project "$generator_project" -- "$command"
