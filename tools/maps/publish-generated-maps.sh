#!/usr/bin/env bash
# Commits a published documentation map set (from `Elsa.Maps.Generator -- publish`) to the generated-maps branch.
# Spec 257. Run by the "Publish generated maps" job in .github/workflows/ci.yml after every push to main.
#
# Usage: publish-generated-maps.sh <set-dir> <source-sha> [<remote>]
#
# Plain git plumbing, mirroring tools/versioning/Elsa.Versioning.Publisher/GitPublishState.cs; never a force push
# (ruleset generated-maps forbids it). Each commit carries a "Source-Commit: <sha>" trailer naming the main commit it
# describes, and the branch only moves forward along main:
#   - branch absent                          -> orphan bootstrap commit
#   - <source-sha> does not descend from the newest Source-Commit on the branch (an older run) -> exit 0,
#     "superseded". A hand commit without the trailer is skipped over when reading it, and the next set lands on top.
#   - the set's tree equals the tip's tree   -> exit 0, "unchanged"
#   - otherwise commit on top of the tip and push; on rejection re-fetch and retry once, then fail.
set -euo pipefail

set_dir=${1:?usage: publish-generated-maps.sh <set-dir> <source-sha> [<remote>]}
source_sha=${2:?usage: publish-generated-maps.sh <set-dir> <source-sha> [<remote>]}
remote=${3:-origin}
branch=generated-maps
tracking=refs/remotes/$remote/$branch

[[ -d "$set_dir" ]] || { echo "error: set directory '$set_dir' does not exist" >&2; exit 1; }
source_sha=$(git rev-parse --verify "$source_sha^{commit}")

export GIT_AUTHOR_NAME='github-actions[bot]' GIT_COMMITTER_NAME='github-actions[bot]'
export GIT_AUTHOR_EMAIL='41898282+github-actions[bot]@users.noreply.github.com'
export GIT_COMMITTER_EMAIL="$GIT_AUTHOR_EMAIL"

git_dir=$(git rev-parse --absolute-git-dir)
index=$(mktemp)
trap 'rm -f "$index"' EXIT

# Builds the set's tree without touching the repository's index or working tree.
build_tree() {
  rm -f "$index"
  GIT_INDEX_FILE="$index" GIT_DIR="$git_dir" GIT_WORK_TREE="$set_dir" git add -A -f -- .
  GIT_INDEX_FILE="$index" git write-tree
}

attempt() {
  local tip='' status=0
  git ls-remote --exit-code --heads "$remote" "refs/heads/$branch" >/dev/null || status=$?
  if (( status == 0 )); then
    git fetch --quiet --no-tags "$remote" "+refs/heads/$branch:$tracking"
    tip=$(git rev-parse "$tracking")
  elif (( status != 2 )); then
    echo "error: could not read $remote/$branch (git ls-remote exit $status)" >&2
    return 1
  fi

  if [[ -n "$tip" ]]; then
    local recorded
    recorded=$(git log --format='%(trailers:key=Source-Commit,valueonly)' "$tip" | grep -m 1 . | tr -d '[:space:]' || true)
    if [[ -n "$recorded" ]] && ! git cat-file -e "$recorded^{commit}" 2>/dev/null; then
      echo "error: $branch names Source-Commit $recorded, which this checkout does not have (fetch-depth 0?)" >&2
      return 1
    fi
    if [[ -n "$recorded" ]] && ! git merge-base --is-ancestor "$recorded" "$source_sha"; then
      echo "superseded: $branch already describes $recorded, which $source_sha does not descend from"
      return 0
    fi
  fi

  local tree
  tree=$(build_tree)
  if [[ -n "$tip" && "$tree" == "$(git rev-parse "$tip^{tree}")" ]]; then
    echo "unchanged: the maps for $source_sha match $branch at $tip"
    return 0
  fi

  local commit
  commit=$(git commit-tree "$tree" ${tip:+-p "$tip"} --no-gpg-sign \
    -m "Maps for ${source_sha:0:12}" -m "Source-Commit: $source_sha")
  git push --quiet --no-verify "$remote" "$commit:refs/heads/$branch" || return 3
  echo "published: $branch -> $commit (maps for $source_sha)"
}

for try in 1 2; do
  status=0
  attempt || status=$?
  (( status == 3 )) || exit "$status"
  echo "push to $remote/$branch was rejected (attempt $try); re-reading the tip" >&2
done
echo "error: could not publish $branch after a retry" >&2
exit 1
