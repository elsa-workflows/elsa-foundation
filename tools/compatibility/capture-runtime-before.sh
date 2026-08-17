#!/usr/bin/env bash
set -euo pipefail

repo_root=$(git rev-parse --show-toplevel)
script_path=${BASH_SOURCE[0]}
if [[ "$script_path" != /* ]]; then
    script_path="$PWD/$script_path"
fi
script_path=$(cd "$(dirname "$script_path")" && pwd -P)/$(basename "$script_path")
case "$script_path" in
    "$repo_root"/*) script_relative_path=${script_path#"$repo_root"/} ;;
    *)
        echo "capture script must execute from the repository checkout" >&2
        exit 1
        ;;
esac
if [[ "$script_relative_path" != "tools/compatibility/capture-runtime-before.sh" ]]; then
    echo "capture script path is not the checked-in Runtime capture script" >&2
    exit 1
fi
if ! cmp -s "$script_path" <(git -C "$repo_root" show "HEAD:$script_relative_path"); then
    echo "capture script differs from its committed HEAD blob; refusing capture" >&2
    exit 1
fi
source_commit=${RUNTIME_BEFORE_COMMIT:?RUNTIME_BEFORE_COMMIT must pin the pre-migration FastEndpoints source commit}
git -C "$repo_root" cat-file -e "$source_commit^{commit}"
git -C "$repo_root" merge-base --is-ancestor "$source_commit" HEAD || {
    echo "the pinned historical source must be an ancestor of the current checkout" >&2
    exit 1
}
capture_runner_identity=checked-in-commit
runner_tree=$(git -C "$repo_root" rev-parse HEAD^{tree})
worktree_dir=$(mktemp -d "${TMPDIR:-/tmp}/elsa-runtime-before.XXXXXX")
output_dir=${1:-"$repo_root/tests/Elsa/Workflows/Runtime/Api/Tests/Baselines"}
trap 'git -C "$repo_root" worktree remove --force "$worktree_dir" >/dev/null 2>&1 || true' EXIT

git -C "$repo_root" worktree add --detach "$worktree_dir" "$source_commit" >/dev/null
test -f "$worktree_dir/src/Elsa/Workflows/Runtime/Api/WorkflowsRuntimeApiFeature.cs"
rg -q 'FastEndpointsFeatureBase' "$worktree_dir/src/Elsa/Workflows/Runtime/Api/WorkflowsRuntimeApiFeature.cs"
test "$(git -C "$worktree_dir" ls-files 'src/Elsa/Workflows/Runtime/Api/Endpoints' | wc -l | tr -d ' ')" -ge 12
mkdir -p "$worktree_dir/tools/compatibility"
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
    tools/compatibility/capture-runtime-before.sh \
    "$worktree_dir/tools/compatibility/capture-runtime-before.sh"
copy_committed_blob \
    tools/compatibility/RuntimeFastEndpointsCapture/Program.cs \
    "$worktree_dir/tools/compatibility/RuntimeFastEndpointsCapture/Program.cs"
copy_committed_blob \
    tools/compatibility/RuntimeFastEndpointsCapture/RuntimeFastEndpointsCapture.csproj \
    "$worktree_dir/tools/compatibility/RuntimeFastEndpointsCapture/RuntimeFastEndpointsCapture.csproj"
copy_committed_blob \
    tools/compatibility/RuntimeFastEndpointsCapture/HistoricalOpenApiEvidenceCapture.cs \
    "$worktree_dir/tools/compatibility/RuntimeFastEndpointsCapture/HistoricalOpenApiEvidenceCapture.cs"

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
    tools/compatibility/capture-runtime-before.sh \
    tools/compatibility/RuntimeFastEndpointsCapture/Program.cs \
    tools/compatibility/RuntimeFastEndpointsCapture/RuntimeFastEndpointsCapture.csproj \
    tools/compatibility/RuntimeFastEndpointsCapture/HistoricalOpenApiEvidenceCapture.cs \
    tests/Elsa/Api/Compatibility/Testing/OpenApi/OpenApiEvidenceCapture.cs \
    tests/Elsa/Api/Compatibility/Testing/Serialization/CompatibilityJson.cs \
    tests/Elsa/Api/Compatibility/Testing/Manifests/EndpointIdentity.cs; do
    test -f "$worktree_dir/$dependency"
done

RUNTIME_BEFORE_COMMIT=$(git -C "$worktree_dir" rev-parse HEAD) \
RUNTIME_CAPTURE_RUNNER_IDENTITY="$capture_runner_identity" \
dotnet run --project "$worktree_dir/tools/compatibility/RuntimeFastEndpointsCapture/RuntimeFastEndpointsCapture.csproj" \
    -- "$worktree_dir" "$output_dir"

printf 'sourceCommit=%s\nrunnerIdentity=%s\nhttpSha256=%s\nopenApiSha256=%s\n' \
    "$source_commit" "$capture_runner_identity" \
    "$(shasum -a 256 "$output_dir/runtime-http-fastendpoints.json" | cut -d ' ' -f 1)" \
    "$(shasum -a 256 "$output_dir/runtime-openapi-fastendpoints.json" | cut -d ' ' -f 1)"
