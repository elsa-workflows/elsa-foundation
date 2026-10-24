#!/usr/bin/env bash
# Spec 259 gate (FR-008, research R8): before docs/maps/dependency-map.json leaves main, prove that reading it from a
# commit and deriving it from the commit's tree give the same result.
#
# For each of the last N first-parent commits of origin/main:
#   1. pick the newest publish-state record revision whose last_publish_commit is the commit or an ancestor of it
#      (computing an older commit against a newer record is refused by the forward-only gate);
#   2. run the calculator with --dataset committed and with --dataset derived, time both, and byte-compare the outputs
#      (every package's version, affected flag and fingerprint);
#   3. byte-compare the committed and derived dataset of the commit and of every commit that record revision names,
#      which covers the post-switch mix of a derived built commit and committed record commits.
#
# Prints a Markdown table for the PR's gate evidence and exits 1 on any difference or error.
#
# Usage: bash specs/259-derived-dependency-map/validate-dataset-sources.sh [N=30]
# Needs: git with origin/main and origin/publish-state fetched in full; the .NET SDK; python or python3.
set -euo pipefail

count="${1:-30}"
root="$(git rev-parse --show-toplevel)"
cd "$root"
spec_dir="specs/259-derived-dependency-map"
scratch="$(mktemp -d "${TMPDIR:-/tmp}/spec259-validate.XXXXXX")"
python_bin="$(command -v python3 >/dev/null 2>&1 && python3 -c 'print(1)' >/dev/null 2>&1 && echo python3 || echo python)"

json_field() { "$python_bin" -c "import json,sys; d=json.load(sys.stdin); print($1)"; }
now_ms() { "$python_bin" -c 'import time; print(int(time.time()*1000))'; }

echo "Scratch: $scratch" >&2
dotnet build tools/versioning/Elsa.Versioning.Calculator -c Release -p:RestoreLockedMode=true >"$scratch/build.log" 2>&1 \
  || { cat "$scratch/build.log" >&2; exit 2; }
calculator="tools/versioning/Elsa.Versioning.Calculator/bin/Release/net10.0/Elsa.Versioning.Calculator.dll"

# Record revisions, newest first, with the commit each was last published at.
: >"$scratch/revisions.txt"
for revision in $(git rev-list origin/publish-state); do
  last="$(git show "$revision:published-versions.json" | json_field "d.get('last_publish_commit') or ''" | tr -d '\r')"
  [ -n "$last" ] && echo "$revision $last" >>"$scratch/revisions.txt"
done

record_for() {
  local commit="$1" revision last
  while read -r revision last; do
    if git merge-base --is-ancestor "$last" "$commit" 2>/dev/null; then echo "$revision"; return; fi
  done <"$scratch/revisions.txt"
}

record_commits() {
  git show "$1:published-versions.json" | json_field "'\n'.join(sorted({p['commit'] for p in d['packages']}))" | tr -d '\r'
}

declare -a rows=()
declare -A record_of=()
all_commits="$scratch/all-commits.txt"
: >"$all_commits"
failures=0
ratio_sum=0
ratio_n=0

for commit in $(git rev-list --first-parent -"$count" origin/main); do
  revision="$(record_for "$commit")"
  if [ -z "$revision" ]; then
    rows+=("| ${commit:0:9} | — | no record revision precedes it | | | |")
    failures=$((failures + 1))
    continue
  fi
  record_of[$commit]="$revision"
  echo "$commit" >>"$all_commits"
  record_commits "$revision" >>"$all_commits"

  outputs=()
  times=()
  for source in committed derived; do
    out="$scratch/$commit.$source.json"
    start="$(now_ms)"
    if dotnet "$calculator" --repo "$root" --record-ref "$revision" --commit "$commit" --dataset "$source" --output "$out" 2>"$scratch/$commit.$source.err"; then
      outputs+=("$out")
    else
      outputs+=("")
    fi
    times+=($(( $(now_ms) - start )))
  done

  if [ -z "${outputs[0]}" ] || [ -z "${outputs[1]}" ]; then
    versions="error (see $scratch/$commit.*.err)"
    failures=$((failures + 1))
  elif cmp -s "${outputs[0]}" "${outputs[1]}"; then
    versions="same"
  else
    versions="differs"
    failures=$((failures + 1))
  fi

  ratio="$("$python_bin" -c "print(round(${times[1]} / max(${times[0]}, 1), 2))")"
  ratio_sum="$("$python_bin" -c "print($ratio_sum + $ratio)")"
  ratio_n=$((ratio_n + 1))
  rows+=("| ${commit:0:9} | ${revision:0:9} | $versions | DATASET:$commit | RECORDS:$revision | $(( times[0] / 1000 )).$(( times[0] % 1000 / 100 ))s / $(( times[1] / 1000 )).$(( times[1] % 1000 / 100 ))s ($ratio×) |")
done

# One scan per distinct commit: the commits built and every commit their record revisions name.
sort -u "$all_commits" -o "$all_commits"
# shellcheck disable=SC2046
dotnet run "$spec_dir/dataset-bytes.cs" -- "$root" "$scratch/datasets" $(cat "$all_commits") >"$scratch/datasets.txt" 2>"$scratch/datasets.err" \
  || { cat "$scratch/datasets.err" >&2; exit 2; }
tr -d '\r' <"$scratch/datasets.txt" >"$scratch/datasets.lf" && mv "$scratch/datasets.lf" "$scratch/datasets.txt"
declare -A dataset=()
while read -r commit status rest; do dataset[$commit]="$status"; done <"$scratch/datasets.txt"

echo "| Commit | Record revision | Versions and fingerprints | Dataset bytes (commit) | Dataset bytes (record commits) | Time committed / derived |"
echo "|---|---|---|---|---|---|"
for row in "${rows[@]}"; do
  if [[ "$row" == *"DATASET:"* ]]; then
    commit="$(sed -E 's/.*DATASET:([0-9a-f]+).*/\1/' <<<"$row")"
    revision="$(sed -E 's/.*RECORDS:([0-9a-f]+).*/\1/' <<<"$row")"
    own="${dataset[$commit]:-missing}"
    [ "$own" = "same" ] || failures=$((failures + 1))
    bad=()
    total=0
    for recorded in $(record_commits "$revision"); do
      total=$((total + 1))
      [ "${dataset[$recorded]:-missing}" = "same" ] || bad+=("${recorded:0:9}:${dataset[$recorded]:-missing}")
    done
    if [ "${#bad[@]}" -eq 0 ]; then records="same ($total)"; else records="differs: ${bad[*]}"; failures=$((failures + 1)); fi
    row="${row/DATASET:$commit/$own}"
    row="${row/RECORDS:$revision/$records}"
  fi
  echo "$row"
done

mean="$("$python_bin" -c "print(round($ratio_sum / max($ratio_n, 1), 2))")"
echo
echo "Mean derived/committed time ratio: ${mean}× over $ratio_n commits (limit 1.5×, plan Performance Goals)."
echo "Distinct commits scanned for dataset bytes: $(wc -l <"$all_commits" | tr -d ' ')."
if [ "$failures" -gt 0 ]; then
  echo "FAILED: $failures difference(s) or error(s). Outputs are in $scratch." >&2
  exit 1
fi
echo "PASSED: both dataset sources agree."
