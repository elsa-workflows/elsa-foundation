#!/usr/bin/env bash
# Thin shim. The generators are now one .NET tool (Elsa.Maps.Generator); this path is kept because
# AGENTS.md, docs/maps/README.md and ~27 spec tasks.md files document this exact invocation.
set -euo pipefail
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec dotnet run --project "$script_dir/Elsa.Maps.Generator" -- feature-dependency
