# Quickstart: validating spec 257

## Local

```bash
dotnet run --project tools/maps/Elsa.Maps.Generator -- all      # writes every map; git status stays clean
git status --short docs/                                        # expect no output
dotnet run --project tools/maps/Elsa.Maps.Generator -- check    # compares dependency-map.json only
dotnet run --project tools/maps/Elsa.Maps.Generator -- publish "$TMPDIR/maps" --source-commit "$(git rev-parse HEAD)" --repo-url https://github.com/elsa-workflows/elsa-foundation
grep -c 'blob/' "$TMPDIR/maps/docs/maps/test-map.md"            # out-of-set links pinned
dotnet run --project tools/maps/Elsa.Maps.Generator -- publish-self-test
python3 tools/ci/change_scope.py --self-test
```

## CI (on the implementation PR)

1. The PR's own run is a full run, because it changes `ci.yml` and `tools/`. Both required checks go green.
2. Throwaway docs-only PR: edit one line in `docs/README.md`. Expect `Build & test` to succeed in under 5 minutes, the step summary to say "documentation-only", `Architecture guards` and `Core-only` to be skipped, and `Generated maps fresh` to run in full.
3. Throwaway mixed PR: edit `docs/README.md` and one `.cs` file. Expect a full run.
4. Two concurrent spec PRs from the same base, each adding `specs/9xx-probe-*`: merge one; the other stays mergeable with no conflict. Close both without merging the second.

## After merge (main)

1. The `Publish generated maps` job on the merge commit succeeds. `generated-maps` now exists, and its tip message names the merge commit.
2. Open `https://github.com/elsa-workflows/elsa-foundation/tree/generated-maps/docs/maps`. A source link in `test-map.md` opens the project file at the pinned commit.
3. The run's artifacts include `generated-maps-<sha>`.

## Owner action, once

Create the ruleset `generated-maps` (target branch `generated-maps`, rules: restrict deletions, block force pushes), mirroring `publish-state`.
