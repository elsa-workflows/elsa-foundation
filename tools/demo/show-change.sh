#!/usr/bin/env bash
# Prints what release 1.1.0 changes in the Notes sample as a diff, for the screen: every file in the V2 folder, then the
# two files V2 replaces (the family declaration and the store's Add), then the version the package gets.
set -euo pipefail

usage() {
  cat >&2 <<'USAGE'
Usage: bash tools/demo/show-change.sh [--all]

Prints the release 1.1.0 change of samples/Elsa.Samples.Nuplane.Notes as a readable diff.
  --all   also print the generated migration designers, model snapshots and the PostgreSql migration
USAGE
  exit 2
}

show_all=0
case "${1:-}" in
  "") ;;
  --all) show_all=1 ;;
  *) usage ;;
esac

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
cd "$root/samples/Elsa.Samples.Nuplane.Notes"

# New files, shown whole: a diff against nothing, without the git plumbing lines.
new_file() { git diff --no-index --no-color -- /dev/null "$1" | sed -n '/^+++ /,$p' | sed "1s|.*|+++ $1|" || true; }
# A file V2 takes over from V1, shown as V1 against V2.
replaced() { git diff --no-index --no-color -- "$1" "$2" | sed -n '/^--- /,$p' || true; }

echo "==== The V2 folder: this is the whole change ===="
twins=()
while IFS= read -r file; do
  if [[ -f "V1/${file#V2/}" ]]; then
    twins+=("${file#V2/}")
  elif [[ "$show_all" -eq 1 || ! ( "$file" == *.Designer.cs || "$file" == *ModelSnapshot.cs || "$file" == */PostgreSql/* ) ]]; then
    new_file "$file"
    echo
  fi
done < <(find V2 -type f -name '*.cs' | sort)

echo "==== V2 replaces these files of V1 ===="
for twin in "${twins[@]}"; do
  replaced "V1/$twin" "V2/$twin"
  echo
done

echo "==== The package version ===="
grep -n "<Version Condition" Elsa.Samples.Nuplane.Notes.csproj
