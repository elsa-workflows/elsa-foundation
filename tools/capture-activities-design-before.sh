#!/usr/bin/env bash
set -euo pipefail

repo_root=$(git rev-parse --show-toplevel)
source_commit=${ACTIVITIES_DESIGN_BEFORE_COMMIT:?ACTIVITIES_DESIGN_BEFORE_COMMIT must pin the pre-migration FastEndpoints source commit}
git cat-file -e "$source_commit^{commit}"
git merge-base --is-ancestor "$source_commit" HEAD || {
    echo "the pinned historical source must be an ancestor of the current checkout" >&2
    exit 1
}

runner_paths=(
    tools/capture-activities-design-before.sh
    tests/Elsa/Activities/Design/Tests/Api/Capture/Elsa.Activities.Design.BeforeCapture.csproj
    tests/Elsa/Activities/Design/Tests/Api/Capture/Program.cs
    tests/Elsa/Activities/Design/Tests/Api/Support/ActivitiesDesignCompatibilityCases.cs
    tests/Elsa/Activities/Design/Tests/Api/Support/ActivitiesDesignCompatibilityHost.cs
)
if [[ "${ACTIVITIES_DESIGN_ALLOW_DIRTY:-0}" != 1 ]]; then
    git diff --quiet -- "${runner_paths[@]}" && git diff --cached --quiet -- "${runner_paths[@]}" || {
        echo "capture runner content must be committed before capture (set no dirty override in CI)" >&2
        exit 1
    }
fi

output_dir=${1:-"$repo_root/tests/Elsa/Activities/Design/Tests/Api/Baselines"}
worktree_dir=$(mktemp -d "${TMPDIR:-/tmp}/elsa-activities-design-before.XXXXXX")
trap 'git -C "$repo_root" worktree remove --force "$worktree_dir" >/dev/null 2>&1 || true' EXIT
git -C "$repo_root" worktree add --detach "$worktree_dir" "$source_commit" >/dev/null

# Copy only the checked-in runner/support blobs from the current tree.  Production source and all
# source-commit dependencies remain the detached historical tree, so a squash-lost runner cannot run.
runner_tree=$(git rev-parse HEAD^{tree})
for path in "${runner_paths[@]}"; do
    blob=$(git rev-parse "$runner_tree:$path")
    git cat-file -e "$blob"
    mkdir -p "$worktree_dir/$(dirname "$path")"
    git cat-file blob "$blob" > "$worktree_dir/$path"
done

ACTIVITIES_DESIGN_BEFORE_COMMIT="$source_commit" \
ACTIVITIES_DESIGN_CAPTURE_RUNNER_IDENTITY=checked-in-commit \
dotnet run --project "$worktree_dir/tests/Elsa/Activities/Design/Tests/Api/Capture/Elsa.Activities.Design.BeforeCapture.csproj" \
    -- "$worktree_dir" "$output_dir"

printf 'sourceCommit=%s\nhttpSha256=%s\nopenApiSha256=%s\nrawOpenApiSha256=%s\n' \
    "$source_commit" \
    "$(shasum -a 256 "$output_dir/activities-design-http-fastendpoints.json" | cut -d ' ' -f 1)" \
    "$(shasum -a 256 "$output_dir/activities-design-openapi-fastendpoints.json" | cut -d ' ' -f 1)" \
    "$(shasum -a 256 "$output_dir/activities-design-openapi-fastendpoints.raw.json" | cut -d ' ' -f 1)"
