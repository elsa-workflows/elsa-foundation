#!/usr/bin/env bash
# Exercises tools/maps/publish-generated-maps.sh against a scratch repository and bare remote (spec 257):
# bootstrap, append, superseded, unchanged, a hand commit without the trailer, and no force push.
# Run by the "Generated maps fresh" job; needs only git and bash.
set -euo pipefail

script=$(cd "$(dirname "$0")" && pwd)/publish-generated-maps.sh
scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT

fail() { echo "FAIL: $*" >&2; exit 1; }
expect() { # expect <name> <needle> <output>
  [[ "$3" == *"$2"* ]] || fail "$1: expected output containing '$2', got: $3"
}

git init -q --bare "$scratch/remote.git"
git init -q "$scratch/work"
cd "$scratch/work"
git config user.email test@example.com
git config user.name Test
git remote add origin "$scratch/remote.git"

commit_main() { echo "$1" > "f.txt"; git add f.txt; git commit -q -m "$1"; git rev-parse HEAD; }
set_with() { rm -rf "$scratch/set"; mkdir -p "$scratch/set/docs/maps"; echo "$1" > "$scratch/set/docs/maps/test-map.md"; echo readme > "$scratch/set/README.md"; }
tip() { git ls-remote origin refs/heads/generated-maps | cut -f1; }
trailer() { git fetch -q origin generated-maps && git log -1 --format='%(trailers:key=Source-Commit,valueonly)' FETCH_HEAD | tr -d '[:space:]'; }

m1=$(commit_main one)
set_with "maps one"
out=$(bash "$script" "$scratch/set" "$m1"); expect bootstrap "published:" "$out"
[[ $(trailer) == "$m1" ]] || fail "bootstrap: tip trailer must name $m1"
[[ -z $(git log -1 --format=%P FETCH_HEAD) ]] || fail "bootstrap: first commit must be an orphan"
git ls-files --error-unmatch f.txt >/dev/null || fail "the repository index must be left alone"

m2=$(commit_main two)
set_with "maps two"
before=$(tip)
out=$(bash "$script" "$scratch/set" "$m2"); expect append "published:" "$out"
[[ $(trailer) == "$m2" ]] || fail "append: tip trailer must name $m2"
[[ $(git log -1 --format=%P FETCH_HEAD) == "$before" ]] || fail "append: new commit must sit on the previous tip (no force)"

set_with "maps one again"
out=$(bash "$script" "$scratch/set" "$m1"); expect superseded "superseded:" "$out"
[[ $(trailer) == "$m2" ]] || fail "superseded: an older main commit must not move the branch"

set_with "maps two"
before=$(tip)
out=$(bash "$script" "$scratch/set" "$m2"); expect unchanged "unchanged:" "$out"
[[ $(tip) == "$before" ]] || fail "unchanged: an identical tree must not create a commit"

# A hand commit without the trailer: the next run reads the newest trailer below it and lands on top.
git fetch -q origin generated-maps
hand=$(git commit-tree "$(git rev-parse FETCH_HEAD^{tree})" -p FETCH_HEAD -m "hand edit")
git push -q origin "$hand:refs/heads/generated-maps"
m3=$(commit_main three)
set_with "maps three"
out=$(bash "$script" "$scratch/set" "$m3"); expect "after hand commit" "published:" "$out"
[[ $(trailer) == "$m3" ]] || fail "after hand commit: tip trailer must name $m3"
[[ $(git log -1 --format=%P FETCH_HEAD) == "$hand" ]] || fail "after hand commit: the new set must sit on top of the hand commit"

out=$(bash "$script" "$scratch/missing" "$m3" 2>&1) && fail "a missing set directory must fail"
expect "missing set" "does not exist" "$out"

echo "publish-generated-maps: all cases pass"
