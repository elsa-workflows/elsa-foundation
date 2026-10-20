# Research: Generated artifacts never block or conflict

All findings were verified against `main` at `aaa6f0722` on 2026-10-20.

## R1 — Narrowing `check` to the committed inputs

- **Decision**: `MapFreshness.Check` (`tools/maps/Elsa.Maps.Generator/MapFreshness.cs:34-39`) keeps only the `DependencyMap.Generate` call. Its doc comment ("thirteen map files") and failure text (`:55-64`) are rewritten. `solution-filters-check` is a separate command and is left as it is.
- **Rationale**: `check` compares every path the six generators return, and a missing committed file counts as stale (`SameBytes`, `:74-75`). Untracking the maps without this change turns the required check red. No generator reads `manifest.json` back, and every layer derives from `ProjectGraph.Read`, so dropping the other layers from `check` hides no input drift that matters to tooling.
- **Alternatives**: keep `check` and have CI compare only a subset. Rejected because the local `-- check` and CI would then disagree.

## R2 — Classifying a PR as documentation-only

- **Decision**: add a classifier script `tools/ci/change_scope.py`, with a `--self-test` mode, that runs as the first step of `build-and-test`. It reads `git diff --name-only <base>...<head>`; the checkout is already `fetch-depth: 0` (`ci.yml:89-92`). It writes `docs_only=true|false` to the step output and a reason line to the step summary. It returns `false` on any non-PR event, on any git error, on an empty diff, and whenever a path is outside the set below.
- **Documentation-only set** (every changed path must match one include and no exclude):
  - **Include:** `docs/**`, `specs/**`, `Studio/specs/**`, `.specify/**`, `Studio/.specify/**`, `.agents/**`, `.claude/**`, `Studio/.agents/**`, `Studio/.claude/**`, and root-level `*.md`.
  - **Exclude, because they are build or test inputs:**
    - root `EXTENSION_POINTS.md`, read by `CatalogLinkGuardTests` and the extension-point layer;
    - `docs/maps/dependency-map.json`, read by `DependencyMapTests`, `NuGetLockFileTests` and the versioning calculator;
    - `specs/*/contracts/**`, because `ManagementApiOperationInventoryTests.cs:62` reads `specs/220-domain-owned-apis/contracts/management-api.openapi.yaml`;
    - any `*.props`, `*.targets`, `*.csproj`, `.editorconfig` or `*.globalconfig`, because `EfCoreDependencyGuardTests:598`, `RetiredTermEnforcementGuardTests:398-405` and `RetiredTermAnalyzerTests:137` scan the whole repo for these.
- **Rationale**: plain git keeps us inside the org Actions allow-list (`docker.yml:43-57`). A third-party paths-filter action is blocked at startup with no logs. A step inside the job keeps the required context name `Build & test` reporting a real conclusion; a skipped job would report "skipped", and a `paths-ignore` trigger leaves required checks pending forever.
- **Alternatives**:
  - workflow-level `paths-ignore`: rejected, because required checks never report.
  - a separate classifier job with `needs`: workable, but it adds a job and makes the required job conditional, so "skipped" counts as success only by ruleset accident.
  - `dorny/paths-filter`: not on the allow-list.

## R3 — What the fast path still runs (FR-007)

- **Decision**: on `docs_only=true`, `Build & test` runs:
  - checkout;
  - the existing cheap Python guards, steps 2–4: `test_workflow_guards.py`, the Studio asset and tag self-tests and `test_studio_docker_publish.py`;
  - `python3 tools/maps/check-catalog-links.py --all-readmes`;
  - `change_scope.py --self-test`.

  It skips setup-dotnet, restore, build and `dotnet test`. `architecture-guards` and `core-only` read the job output `docs_only` and skip. `Generated maps fresh` always runs in full; it builds only the generator and finishes in a few minutes.
- **Rationale**:
  - The only documentation-reading tests are in `Elsa.Architecture.Tests`, which needs the full project-graph restore and build.
  - `CatalogLinkGuardTests` checks links in `EXTENSION_POINTS.md` and `README.md` files under `Essentials/` and `Extensions/`. A docs-only PR that moves or deletes a doc those files link to can still break it. `check-catalog-links.py --all-readmes` covers the same scope without a build. Implementation verifies that equivalence (task), and adds `--all-readmes` coverage of the root catalog if it is missing.
  - `ManagementApiOperationInventoryTests` and the dependency-map tests are protected by excluding their inputs (R2), not by running them.
  - `test_workflow_guards.py:144,155` pins the literal `python3 tools/contributor-checkpoints/test_workflow_guards.py` in `ci.yml`, and that line stays.
- **Alternatives**: run `Elsa.Architecture.Tests` with a `--filter`. Rejected because it still pays the full build, which is the cost we are removing.

## R4 — Producing a publishable map set

- **Decision**: add a generator command `publish <out-dir> --source-commit <sha> --repo-url <url>`. It reuses `MarkdownTable.Redirect` (`MarkdownTable.cs:22,28-31`) to generate every layer into `<out-dir>` without touching the working tree, then rewrites links. A relative link whose target is in the published set stays relative. Any other relative link becomes `<repo-url>/blob/<sha>/<repo-path>`. The command also writes `<out-dir>/README.md`, which names the source commit and the generation command.
- **Bug fixed on the way**: `ArchitectureReferenceMapGenerator.ReadSummary` (`:118-133,161-168`) reads `domain-map.md` and `extension-point-map.md` from `repo.Root`, ignoring the redirect. During `check` today the v2 findings come from the committed maps. In a clean checkout, `-- architecture-reference` on its own writes "not generated". It must read from the redirected location.
- **Rationale**: the maps hold about 6,900 relative links into the source tree, such as `../../Essentials/tests/...csproj`. Copied as they are onto a branch holding only maps, every one would 404. Pinning to the source commit keeps each link correct for the commit the map describes.
- **Alternatives**: commit the maps to `generated-maps` with relative links as they are. Rejected because of the 404s. Copy the whole tree onto the branch: rejected, since it duplicates the repo.

## R5 — Writing the `generated-maps` branch

- **Decision**: add a new job `publish-maps` in `ci.yml`:
  - **Trigger and scope:** `if: github.event_name == 'push'`, with no `needs`, because the maps describe the tree whether or not tests pass.
  - **Permissions:** `contents: write`.
  - **Concurrency:** job-level group `generated-maps` with `cancel-in-progress: true`, so a newer main commit supersedes a pending older one.
  - **Steps:** build the generator, run `publish`, upload `actions/upload-artifact@v4` named `generated-maps-<sha>` with `if-no-files-found: error`, then run `tools/maps/publish-generated-maps.sh <dir> <sha>`.
- **Script mechanics** (mirroring `GitPublishState.cs:52-96`, plain git with no force):
  1. `git ls-remote --exit-code --heads origin refs/heads/generated-maps`. Exit 2 means bootstrap with an orphan commit.
  2. Fetch the tip and read its `Source-Commit:` trailer. If the new sha is not a descendant of that commit (`git merge-base --is-ancestor`), exit 0 with a "superseded" note. This enforces main-commit order.
  3. Build the tree with a temporary `GIT_INDEX_FILE`, `git add -f` the set, then `write-tree`. If the tree equals the tip's tree, exit 0 with "unchanged", so no empty commits.
  4. `commit-tree -p <tip>`, identity `github-actions[bot]`. The message is `Maps for <short-sha>` plus the `Source-Commit: <sha>` trailer.
  5. `git push origin <commit>:refs/heads/generated-maps`. On rejection, re-fetch and retry from step 2 once, then fail.
- **Branch layout**: the same paths as on main, `docs/maps/*.md`, `docs/maps/manifest.json` and `docs/reports/maps-v{1,2}-findings.md`, plus a root `README.md`. Links between maps keep working.
- **Failure handling**: `publish-maps` is not in the alert job's needs, matching `generated-maps` today (`ci.yml:456-459`). A failure is visible on the run, and the next push catches up. GITHUB_TOKEN pushes start no workflow run, so there is no loop.
- **Alternatives**: a separate workflow on `workflow_run` of CI. Rejected because it adds a workflow and a trigger hop for no isolation benefit. GitHub Pages: rejected, because it adds a deploy surface and the raw URLs of a branch are enough for agents.

## R6 — Protecting the branch

- **Decision**: an owner-created ruleset `generated-maps` with `deletion` and `non_fast_forward`, identical to the existing `publish-state` ruleset. The spec's FR-004b is amended to match: deletion and history rewrites are blocked mechanically, and "only CI writes" is a convention stated in the branch README, as for `publish-state`.
- **Rationale**: the repo's `publish-state` ruleset has exactly these two rules. A restrict-updates rule with a GitHub Actions bypass actor is not used anywhere in the org setup today and would need verifying against the org's ruleset bypass options. It is noted as an optional hardening, not a requirement.

## R7 — Hygiene sweep

- **Decision**: `exempt_re` in `tools/repo-hygiene/hygiene.sh:28` becomes `'^(main|publish-state|generated-maps|release/.*|rc/.*)$'`, and `docs/contributing/repository-hygiene.md:34` lists it.
- **Rationale**: the branch never has a PR and would be swept after 14 idle days. `stale.yml` and `delete_branch_on_merge` only remove PR head branches.

## R8 — Untracking and ignoring

- **Decision**:
  - `.gitignore` adds `docs/maps/*.md`, `!docs/maps/README.md`, `docs/maps/manifest.json`, `docs/reports/maps-v1-findings.md` and `docs/reports/maps-v2-findings.md`.
  - `git rm --cached` the 12 files.
  - Local `-- all` keeps writing the same paths, so the shims and about 27 historical `tasks.md` invocations keep working.
- **Rationale**: the generator lists inputs with `git ls-files --cached --others` over the `Essentials/` and `Extensions/` pathspecs only (`RepoContext.cs:47`, `RepoLayout.cs:13-36`). Ignored outputs under `docs/` can never feed back in as inputs.

## R9 — References to retire (FR-009)

From the inventory:

- **Must change**:
  - `AGENTS.md:29,32,74-76`
  - `docs/contributing/generated-maps.md`, rewritten
  - `docs/contributing/merging.md:19,21`
  - `docs/contributing/validation-matrix.md:7`
  - `docs/reference/spec-lifecycle.md:12,55,81`
  - `docs/reference/elsa-domain-tree.md:6,9,41`
  - `docs/reference/framework-examples.md:32-35,170`
  - `docs/reference/first-user-prompts.md:3,16,67`
  - `docs/reference/nuget-lock-files.md:13-15,31`
  - `docs/skills/catalog.md:8,118,133,179,340,366-378`
  - `docs/README.md:42`
  - `docs/reports/README.md:30-31`
  - `docs/maps/README.md`, rewritten as the entry point
  - `Extensions/README.md:132-133`
  - `.specify/templates/tasks-template.md:162`
  - the `ci.yml` comments and summaries at `:377,437-462,524-551`
  - `tools/versioning/Elsa.Versioning.Calculator/VersionCalculator.cs:143,150`, a hint that stays valid but names `-- dependency-map`
- **Skills**:
  - `elsa-auto-review-merge` (step 5 stops staging maps)
  - `elsa-refresh-generated-maps` (it becomes "generate locally or read `generated-maps`")
  - `elsa-extension-point-catalog`, `elsa-feature-dependency-map`, `elsa-feature-composition`, `elsa-verify-codebase`, `elsa-quality-fix`, `elsa-critical-constitution-review`

  Each edited wrapper's SHA256 in `.specify/integrations/claude.manifest.json` is updated, and `tools/skills/validate-claude-wrapper-drift.ps1` passes.
- **Constitution**: `.specify/memory/constitution.md:47` links the domain map. That is a PATCH 6.0.2 → 6.0.3: the link is re-pointed to the `generated-maps` branch, with a sync-impact note. The unit makes its own constitution change, per house practice.
- **Leave as history**: `specs/**` (70 files), `docs/reports/**` point-in-time reports, `docs/reports/archive/**`, and `tools/consolidation/foundation-path-map.json`. That last file is read only by the manual `verify-history.py` against historical refs.

## R10 — Out of scope, recorded

- `studio-ci.yml` runs on every PR but is not a required check, so it is left alone.
- `dependency-map.json` derived inside the calculator is the follow-up spec.
- The memory and feedback rule "set terminal spec status in the finishing PR (map conflicts)" keeps its first half. The conflict reason disappears once `spec-status-map.md` is no longer committed.
