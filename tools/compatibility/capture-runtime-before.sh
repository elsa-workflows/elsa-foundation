#!/usr/bin/env bash
set -euo pipefail

repo_root=$(git rev-parse --show-toplevel)
source_commit=${RUNTIME_BEFORE_COMMIT:?RUNTIME_BEFORE_COMMIT must pin the pre-migration FastEndpoints source commit}
capture_runner_commit=${RUNTIME_CAPTURE_RUNNER_COMMIT:?RUNTIME_CAPTURE_RUNNER_COMMIT must pin the committed historical capture runner}
git -C "$repo_root" cat-file -e "$source_commit^{commit}"
git -C "$repo_root" cat-file -e "$capture_runner_commit^{commit}"
test "$source_commit" != "$capture_runner_commit"
git -C "$repo_root" merge-base --is-ancestor "$source_commit" "$capture_runner_commit" || {
    echo "capture runner must descend from the pinned historical source" >&2
    exit 1
}
git -C "$repo_root" merge-base --is-ancestor "$capture_runner_commit" HEAD || {
    echo "capture runner must be reachable from the current branch" >&2
    exit 1
}
worktree_dir=$(mktemp -d "${TMPDIR:-/tmp}/elsa-runtime-before.XXXXXX")
output_dir=${1:-"$repo_root/tests/Elsa/Workflows/Runtime/Api/Tests/Baselines"}
trap 'git -C "$repo_root" worktree remove --force "$worktree_dir" >/dev/null 2>&1 || true' EXIT

git -C "$repo_root" worktree add --detach "$worktree_dir" "$source_commit" >/dev/null
test -f "$worktree_dir/src/Elsa/Workflows/Runtime/Api/WorkflowsRuntimeApiFeature.cs"
rg -q 'FastEndpointsFeatureBase' "$worktree_dir/src/Elsa/Workflows/Runtime/Api/WorkflowsRuntimeApiFeature.cs"
test "$(git -C "$worktree_dir" ls-files 'src/Elsa/Workflows/Runtime/Api/Endpoints' | wc -l | tr -d ' ')" -ge 12
mkdir -p "$worktree_dir/tools/compatibility"
cp -R "$repo_root/tools/compatibility/RuntimeFastEndpointsCapture" "$worktree_dir/tools/compatibility/"
mkdir -p "$worktree_dir/tests/Elsa/Api/Compatibility/Testing/OpenApi"
cp "$repo_root/tests/Elsa/Api/Compatibility/Testing/OpenApi/OpenApiEvidenceCapture.cs" \
    "$worktree_dir/tests/Elsa/Api/Compatibility/Testing/OpenApi/OpenApiEvidenceCapture.cs"
test -f "$worktree_dir/tools/compatibility/RuntimeFastEndpointsCapture/HistoricalOpenApiEvidenceCapture.cs"

RUNTIME_BEFORE_COMMIT=$(git -C "$worktree_dir" rev-parse HEAD) \
RUNTIME_CAPTURE_RUNNER_COMMIT="$capture_runner_commit" \
dotnet run --project "$worktree_dir/tools/compatibility/RuntimeFastEndpointsCapture/RuntimeFastEndpointsCapture.csproj" \
    -- "$worktree_dir" "$output_dir"

printf 'sourceCommit=%s\nrunnerCommit=%s\nhttpSha256=%s\nopenApiSha256=%s\n' \
    "$source_commit" "$capture_runner_commit" \
    "$(shasum -a 256 "$output_dir/runtime-http-fastendpoints.json" | cut -d ' ' -f 1)" \
    "$(shasum -a 256 "$output_dir/runtime-openapi-fastendpoints.json" | cut -d ' ' -f 1)"
