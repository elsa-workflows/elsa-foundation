#!/usr/bin/env bash
set -euo pipefail

repo_root=$(git rev-parse --show-toplevel)
source_commit=${WORKFLOWS_DESIGN_BEFORE_COMMIT:?WORKFLOWS_DESIGN_BEFORE_COMMIT must pin the pre-migration FastEndpoints source commit}
git cat-file -e "$source_commit^{commit}"
git merge-base --is-ancestor "$source_commit" HEAD || {
    echo "the pinned historical source must be an ancestor of the current checkout" >&2
    exit 1
}
capture_runner_identity=checked-in-commit
runner_tree=$(git rev-parse HEAD^{tree})
worktree_dir=$(mktemp -d "${TMPDIR:-/tmp}/elsa-workflows-design-before.XXXXXX")
output_dir=${1:-"$repo_root/tests/Elsa/Workflows/Design/Api/Tests/Baselines"}
trap 'git -C "$repo_root" worktree remove --force "$worktree_dir" >/dev/null 2>&1 || true' EXIT

git -C "$repo_root" worktree add --detach "$worktree_dir" "$source_commit" >/dev/null
test -f "$worktree_dir/src/Elsa/Workflows/Design/Api/WorkflowsDesignApiFeature.cs"
rg -q 'FastEndpointsFeatureBase' "$worktree_dir/src/Elsa/Workflows/Design/Api/WorkflowsDesignApiFeature.cs"
test "$(git -C "$worktree_dir" ls-files 'src/Elsa/Workflows/Design/Api/Endpoints' | wc -l | tr -d ' ')" -ge 25
copy_committed_blob() {
    local path=$1
    local destination=$2
    local blob
    blob=$(git -C "$repo_root" rev-parse "$runner_tree:$path")
    git -C "$repo_root" cat-file -e "$blob"
    mkdir -p "$(dirname "$destination")"
    git -C "$repo_root" cat-file blob "$blob" > "$destination"
}

copy_committed_blob \
    tools/compatibility/capture-workflows-design-before.sh \
    "$worktree_dir/tools/compatibility/capture-workflows-design-before.sh"
copy_committed_blob \
    tools/compatibility/WorkflowsDesignFastEndpointsCapture/Program.cs \
    "$worktree_dir/tools/compatibility/WorkflowsDesignFastEndpointsCapture/Program.cs"
copy_committed_blob \
    tools/compatibility/WorkflowsDesignFastEndpointsCapture/WorkflowsDesignFastEndpointsCapture.csproj \
    "$worktree_dir/tools/compatibility/WorkflowsDesignFastEndpointsCapture/WorkflowsDesignFastEndpointsCapture.csproj"
copy_committed_blob \
    tools/compatibility/WorkflowsDesignFastEndpointsCapture/HistoricalOpenApiEvidenceCapture.cs \
    "$worktree_dir/tools/compatibility/WorkflowsDesignFastEndpointsCapture/HistoricalOpenApiEvidenceCapture.cs"
copy_source_blob() {
    local path=$1
    local destination="$worktree_dir/$path"
    local blob
    blob=$(git -C "$repo_root" rev-parse "$source_commit:$path")
    git -C "$repo_root" cat-file -e "$blob"
    mkdir -p "$(dirname "$destination")"
    git -C "$repo_root" cat-file blob "$blob" > "$destination"
}

for source_dependency in \
    tests/Elsa/Api/Compatibility/Testing/OpenApi/OpenApiEvidenceCapture.cs \
    tests/Elsa/Api/Compatibility/Testing/Serialization/CompatibilityJson.cs \
    tests/Elsa/Api/Compatibility/Testing/Manifests/EndpointIdentity.cs; do
    copy_source_blob "$source_dependency"
done

for dependency in \
    tools/compatibility/capture-workflows-design-before.sh \
    tools/compatibility/WorkflowsDesignFastEndpointsCapture/Program.cs \
    tools/compatibility/WorkflowsDesignFastEndpointsCapture/WorkflowsDesignFastEndpointsCapture.csproj \
    tools/compatibility/WorkflowsDesignFastEndpointsCapture/HistoricalOpenApiEvidenceCapture.cs \
    tests/Elsa/Api/Compatibility/Testing/OpenApi/OpenApiEvidenceCapture.cs \
    tests/Elsa/Api/Compatibility/Testing/Serialization/CompatibilityJson.cs \
    tests/Elsa/Api/Compatibility/Testing/Manifests/EndpointIdentity.cs; do
    test -f "$worktree_dir/$dependency"
done

WORKFLOWS_DESIGN_BEFORE_COMMIT="$source_commit" \
WORKFLOWS_DESIGN_CAPTURE_RUNNER_IDENTITY="$capture_runner_identity" \
dotnet run --project "$worktree_dir/tools/compatibility/WorkflowsDesignFastEndpointsCapture/WorkflowsDesignFastEndpointsCapture.csproj" \
    -- "$worktree_dir" "$output_dir"

printf 'sourceCommit=%s\nrunnerIdentity=%s\nhttpSha256=%s\nopenApiSha256=%s\n' \
    "$source_commit" "$capture_runner_identity" \
    "$(shasum -a 256 "$output_dir/workflows-design-http-fastendpoints.json" | cut -d ' ' -f 1)" \
    "$(shasum -a 256 "$output_dir/workflows-design-openapi-fastendpoints.json" | cut -d ' ' -f 1)"
