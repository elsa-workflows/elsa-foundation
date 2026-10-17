#!/usr/bin/env bash
set -euo pipefail

proof_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source_dir="$proof_dir/.source/bpmn"
source_url="https://github.com/valence-works/bpmn.git"
source_sha="be9c6eaa963b846104d5aebf38d337bafab08710"
project="$proof_dir/UnifiedExecution.BpmnProof.csproj"
artifacts_dir="$proof_dir/artifacts"

mkdir -p "$proof_dir/.source"
if [[ ! -d "$source_dir/.git" ]]; then
  git clone --filter=blob:none --no-checkout "$source_url" "$source_dir"
else
  if [[ -n "$(git -C "$source_dir" status --porcelain --untracked-files=normal)" ]]; then
    printf 'Pinned source checkout has local changes; refusing to overwrite it.\n' >&2
    exit 1
  fi
fi

git -C "$source_dir" fetch --depth 1 origin "$source_sha"
git -C "$source_dir" checkout --detach FETCH_HEAD
actual_sha="$(git -C "$source_dir" rev-parse HEAD)"
if [[ "$actual_sha" != "$source_sha" ]]; then
  printf 'Expected BPMN source %s, got %s\n' "$source_sha" "$actual_sha" >&2
  exit 1
fi

# Refuse to build from a source tree whose tracked or untracked files differ from the pin.
git -C "$source_dir" diff --exit-code HEAD --
git -C "$source_dir" diff --cached --exit-code
if [[ -n "$(git -C "$source_dir" status --porcelain --untracked-files=normal)" ]]; then
  printf 'Pinned source checkout is not clean after checkout.\n' >&2
  exit 1
fi

dotnet restore "$project" --locked-mode

source_tree="$(git -C "$source_dir" rev-parse 'HEAD^{tree}')"
lock_hash="$(shasum -a 256 "$proof_dir/packages.lock.json" | awk '{print $1}')"
project_hash="$(shasum -a 256 "$project" | awk '{print $1}')"
mkdir -p "$artifacts_dir"
cat > "$artifacts_dir/source-integrity.json" <<EOF
{
  "repository": "valence-works/bpmn",
  "commit": "$actual_sha",
  "tree": "$source_tree",
  "workingTreeClean": true,
  "packagesLockSha256": "$lock_hash",
  "proofProjectSha256": "$project_hash"
}
EOF

printf 'Pinned source clean at %s (%s).\n' "$actual_sha" "$source_tree"
