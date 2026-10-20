# Generated maps

Moved from `AGENTS.md` (#2578). Rewritten by spec 257: the documentation maps are no longer committed to `main`.

## What is committed and what is published

- **Published, never committed:** the markdown maps under `docs/maps/` (every `*.md` except `README.md`),
  `docs/maps/manifest.json`, `docs/reports/maps-v1-findings.md` and `docs/reports/maps-v2-findings.md`. Git ignores
  them, so a PR never touches them and two PRs can never conflict on them.
- **Committed and checked:** `docs/maps/dependency-map.json`, which the versioning calculator reads from each commit's
  tree, and the `Elsa.Server.*.slnf` solution filters. Commit them only when they change.

The [maps index](../maps/README.md) lists every map and links to each one.

## Generate locally

```bash
dotnet run --project tools/maps/Elsa.Maps.Generator -- all
```

This writes the full set at its usual paths, so relative links between maps resolve, and leaves `git status` clean
apart from a changed `dependency-map.json`. Pass a layer name instead of `all` to regenerate one layer; the
PowerShell and Bash shims under `tools/maps/` call the same tool and produce byte-identical output, so a local run and
the CI set for the same commit match.

The tool needs the .NET SDK. If it is unavailable, ask the user to install it, or read the published set instead.
It needs no restore of the tree: the dependency map reads the packages NuGet pins for each packable project
transitively from the `packages.lock.json` committed beside it, and refuses a lock file older than the project's pins
or references. After a dependency change, update the lock files first; see
[NuGet lock files](../reference/nuget-lock-files.md).

## The `generated-maps` branch

After every push to `main`, the `Publish generated maps` job in `.github/workflows/ci.yml` generates the set with
`-- publish` and commits it to the `generated-maps` branch:

- **Layout:** the same paths as on `main` (`docs/maps/*.md`, `docs/maps/manifest.json`,
  `docs/reports/maps-v{1,2}-findings.md`) plus a root `README.md` naming the source commit. Links that point outside
  the set are pinned to `https://github.com/elsa-workflows/elsa-foundation/blob/<source-sha>/...`, so each one opens
  the file as it was at the commit the map describes.
- **Commits:** one per `main` commit that changed the set, named `Maps for <short-sha>`, with a `Source-Commit: <sha>`
  trailer. Commits land in `main`-commit order: an older run never overwrites a newer set, and a run that would change
  nothing adds no commit.
- **Writers:** CI only, by convention, as for `publish-state`. Do not push to it or open a PR against it. The
  `generated-maps` ruleset blocks deleting the branch and rewriting its history; the next CI publish adds a correct
  set on top of any hand commit. The weekly [hygiene sweep](repository-hygiene.md) never deletes it.
- **Failure:** a failed publish does not turn `main` red. It shows on the run, and the next push catches the
  branch up.

Read it by browsing <https://github.com/elsa-workflows/elsa-foundation/tree/generated-maps/docs/maps>, through
`https://raw.githubusercontent.com/elsa-workflows/elsa-foundation/generated-maps/docs/maps/<file>`, or with
`git fetch origin generated-maps` and `git show origin/generated-maps:docs/maps/<file>`.

## CI artifact

The same push run uploads the set as the build artifact `generated-maps-<sha>`. It needs GitHub sign-in and expires
under the repository's retention policy; the branch does not. Any older set can be reproduced by checking out its
commit and running `-- all`.

## Freshness check

```bash
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
```

`-- check` regenerates the dependency map into a scratch directory and byte-compares it with the committed
`docs/maps/dependency-map.json`. When it is red, run `-- dependency-map` and commit that file. The required
`Generated maps fresh` job runs it on every pull request and every push to `main`, together with the solution-filter
check and the generator self-tests. It does not gate on a fingerprint over the inputs, which would move on every source
edit. The documentation maps have no freshness check, because none is committed: the branch copy describes the commit
its trailer names, and a local run describes your working tree.

## Findings reports

`maps-v1-findings.md` and `maps-v2-findings.md` are part of the published set. After a merge that changed the project
graph, catalogs or specs, read the findings on the `generated-maps` branch (or generate them locally) before relying
on a map as evidence. If they expose drift that makes the current work unsafe, stop and tell the user rather than
continuing from stale assumptions. Record durable findings in a hand-written report, not by editing generated facts.
