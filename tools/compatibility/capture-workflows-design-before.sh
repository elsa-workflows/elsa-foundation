#!/usr/bin/env bash
set -euo pipefail

repo_root=$(git rev-parse --show-toplevel)
source_commit=${WORKFLOWS_DESIGN_BEFORE_COMMIT:-d402ea48b^}
capture_runner_commit=$(git rev-parse HEAD)
worktree_dir=$(mktemp -d "${TMPDIR:-/tmp}/elsa-workflows-design-before.XXXXXX")
output_dir=${1:-"$repo_root/tests/Elsa/Workflows/Design/Api/Tests/Baselines"}
trap 'git -C "$repo_root" worktree remove --force "$worktree_dir" >/dev/null 2>&1 || true' EXIT

git -C "$repo_root" worktree add --detach "$worktree_dir" "$source_commit" >/dev/null
test -f "$worktree_dir/src/Elsa/Workflows/Design/Api/WorkflowsDesignApiFeature.cs"
rg -q 'FastEndpointsFeatureBase' "$worktree_dir/src/Elsa/Workflows/Design/Api/WorkflowsDesignApiFeature.cs"
test "$(git -C "$worktree_dir" ls-files 'src/Elsa/Workflows/Design/Api/Endpoints' | wc -l | tr -d ' ')" -ge 25
mkdir -p "$worktree_dir/tools/compatibility"
cp -R "$repo_root/tools/compatibility/WorkflowsDesignFastEndpointsCapture" "$worktree_dir/tools/compatibility/"
mkdir -p "$worktree_dir/tests/Elsa/Api/Compatibility/Testing/OpenApi"
cp "$repo_root/tests/Elsa/Api/Compatibility/Testing/OpenApi/OpenApiEvidenceCapture.cs" \
    "$worktree_dir/tests/Elsa/Api/Compatibility/Testing/OpenApi/OpenApiEvidenceCapture.cs"

WORKFLOWS_DESIGN_BEFORE_COMMIT=$(git -C "$worktree_dir" rev-parse HEAD) \
WORKFLOWS_DESIGN_CAPTURE_RUNNER_COMMIT="$capture_runner_commit" \
dotnet run --project "$worktree_dir/tools/compatibility/WorkflowsDesignFastEndpointsCapture/WorkflowsDesignFastEndpointsCapture.csproj" \
    -- "$worktree_dir" "$output_dir"

printf 'sourceCommit=%s\nrunnerCommit=%s\nhttpSha256=%s\nopenApiSha256=%s\n' \
    "$(git -C "$worktree_dir" rev-parse HEAD)" "$capture_runner_commit" \
    "$(shasum -a 256 "$output_dir/workflows-design-http-fastendpoints.json" | cut -d ' ' -f 1)" \
    "$(shasum -a 256 "$output_dir/workflows-design-openapi-fastendpoints.json" | cut -d ' ' -f 1)"
