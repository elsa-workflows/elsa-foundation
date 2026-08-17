#!/usr/bin/env bash
set -euo pipefail

repo_root=$(git rev-parse --show-toplevel)
source_commit=${PUBLISHING_BEFORE_COMMIT:?PUBLISHING_BEFORE_COMMIT must pin the pre-migration FastEndpoints source commit}
git cat-file -e "$source_commit^{commit}"
git merge-base --is-ancestor "$source_commit" HEAD || {
    echo "the pinned historical source must be an ancestor of the current checkout" >&2
    exit 1
}

runner_paths=(
    tools/capture-publishing-before.sh
    tests/Elsa/Workflows/Publishing/Api/Tests/Capture/Elsa.Workflows.Publishing.BeforeCapture.csproj
    tests/Elsa/Workflows/Publishing/Api/Tests/Capture/Program.cs
    tests/Elsa/Workflows/Publishing/Api/Tests/Support/PublishingCompatibilityCases.cs
    tests/Elsa/Workflows/Publishing/Api/Tests/Support/PublishingCompatibilityHost.cs
)
if [[ "${PUBLISHING_ALLOW_DIRTY:-0}" != 1 ]]; then
    git diff --quiet -- "${runner_paths[@]}" && git diff --cached --quiet -- "${runner_paths[@]}" || {
        echo "capture runner content must be committed before capture" >&2
        exit 1
    }
fi

output_dir=${1:-"$repo_root/tests/Elsa/Workflows/Publishing/Api/Tests/Baselines"}
worktree_dir=$(mktemp -d "${TMPDIR:-/tmp}/elsa-publishing-before.XXXXXX")
trap 'git -C "$repo_root" worktree remove --force "$worktree_dir" >/dev/null 2>&1 || true' EXIT
git -C "$repo_root" worktree add --detach "$worktree_dir" "$source_commit" >/dev/null

runner_tree=$(git rev-parse HEAD^{tree})
for path in "${runner_paths[@]}"; do
    blob=$(git rev-parse "$runner_tree:$path")
    git cat-file -e "$blob"
    mkdir -p "$worktree_dir/$(dirname "$path")"
    git cat-file blob "$blob" > "$worktree_dir/$path"
done

PUBLISHING_BEFORE_COMMIT="$source_commit" \
PUBLISHING_CAPTURE_RUNNER_IDENTITY=checked-in-commit \
dotnet run --project "$worktree_dir/tests/Elsa/Workflows/Publishing/Api/Tests/Capture/Elsa.Workflows.Publishing.BeforeCapture.csproj" \
    -- "$worktree_dir" "$output_dir"
