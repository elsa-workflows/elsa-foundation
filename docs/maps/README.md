# Maps Index

Maps are navigation and generated-fact surfaces. They should be cheap to regenerate and should not become long-form concept documentation.

The documentation maps below are **never committed to `main`**. CI regenerates them after every merge and publishes
them on the `generated-maps` branch, together with the dependency-map dataset, `dependency-map.json`, which the
versioning calculator derives from each commit's tree, so it is not committed either (spec 259).
See [Generated maps](../contributing/generated-maps.md) for how publication and the freshness check work.

## Where to read the maps

- **Browse on GitHub:** <https://github.com/elsa-workflows/elsa-foundation/tree/generated-maps/docs/maps>. The
  branch's tip commit is named `Maps for <short-sha>` and its `Source-Commit:` trailer names the `main` commit it
  describes. Links from a map into the source tree are pinned to that commit.
- **Raw files, for agents:** `https://raw.githubusercontent.com/elsa-workflows/elsa-foundation/generated-maps/docs/maps/<file>`,
  for example `.../generated-maps/docs/maps/test-map.md`. The findings reports are under
  `.../generated-maps/docs/reports/maps-v1-findings.md` and `maps-v2-findings.md`.
- **From a clone:** `git fetch origin generated-maps`, then `git show origin/generated-maps:docs/maps/<file>`.
- **CI artifact:** each push run of `CI` on `main` uploads the same set as `generated-maps-<sha>`.
- **Generate locally** for your own working tree, at the same paths (git-ignored, so there is nothing to stage):

  ```bash
  dotnet run --project tools/maps/Elsa.Maps.Generator -- all
  ```

## Maps

- [Dependency map](https://github.com/elsa-workflows/elsa-foundation/blob/generated-maps/docs/maps/dependency-map.json) - the canonical project-graph dataset (spec 149) the project-graph maps below are projections of; derived per commit, not committed. See [Dependency map dataset](#dependency-map-dataset).
- [Repo-wide extension points](../../EXTENSION_POINTS.md) - index of per-domain extension catalogs; committed, hand-maintained.
- Per-domain `EXTENSION_POINTS.md` files under `src/` - replace/contribute/event surfaces owned by each domain.
- [Project reference map](https://github.com/elsa-workflows/elsa-foundation/blob/generated-maps/docs/maps/project-reference-map.md) - direct project references.
- [Package map](https://github.com/elsa-workflows/elsa-foundation/blob/generated-maps/docs/maps/package-map.md) - direct external package references and version clusters.
- [Feature map](https://github.com/elsa-workflows/elsa-foundation/blob/generated-maps/docs/maps/feature-map.md) - discovered CShells feature classes.
- [Feature dependency map](https://github.com/elsa-workflows/elsa-foundation/blob/generated-maps/docs/maps/feature-dependency-map.md) - CShells feature IDs, public feature properties, and dependency evidence from project/package references.
- [Test map](https://github.com/elsa-workflows/elsa-foundation/blob/generated-maps/docs/maps/test-map.md) - test projects and direct production references.
- [Spec status map](https://github.com/elsa-workflows/elsa-foundation/blob/generated-maps/docs/maps/spec-status-map.md) - current Speckit work-unit status clues.
- [Domain map](https://github.com/elsa-workflows/elsa-foundation/blob/generated-maps/docs/maps/domain-map.md) - project grouping, roles, and direct cross-domain references.
- [Extension-point map](https://github.com/elsa-workflows/elsa-foundation/blob/generated-maps/docs/maps/extension-point-map.md) - generated facts from root and per-domain `EXTENSION_POINTS.md` catalogs.
- [Architecture reference map](https://github.com/elsa-workflows/elsa-foundation/blob/generated-maps/docs/maps/architecture-reference-map.md) - direct design/runtime reference signals for review.
- [Map manifest](https://github.com/elsa-workflows/elsa-foundation/blob/generated-maps/docs/maps/manifest.json) - what the v1 layer covers: project, package, feature and spec counts, and the files it generates.
- Findings reports: [maps v1 findings](https://github.com/elsa-workflows/elsa-foundation/blob/generated-maps/docs/reports/maps-v1-findings.md) and [maps v2 findings](https://github.com/elsa-workflows/elsa-foundation/blob/generated-maps/docs/reports/maps-v2-findings.md).

## Spec status metadata

The Title column prefers the `# Feature Specification: <title>` heading. Otherwise, it uses the
first `# ` heading and removes a leading numeric prefix separated by a spaced em dash or hyphen
(for example, `# 108 — BPMN Container Activity`). If no usable heading is present, it uses the
spec folder id. Speckit titles retain their existing formatting.

The spec-status map reads Notes only from an optional, single-line `**Notes**:` entry in `spec.md`,
alongside metadata such as `**Status**:`. The first explicit Notes entry supplies its trimmed value;
if it is absent or blank, the map displays `-`. Ordinary headings and prose, including an
`## Out of scope` section or discussion of deferred alternatives, do not create notes.

```markdown
**Notes**: Waiting for provider verification.
```

## Dependency map dataset

`dependency-map.json` is the one scan of the project files. The project-reference, package, test, domain,
architecture-reference and feature-dependency maps take every project and package fact from it rather than reading
`.csproj` files themselves, and publishing reads it (spec 150). The `dependency-map` layer writes it locally and
`-- all` includes it. It is not committed (spec 259): the scan lives in `tools/maps/Elsa.Maps.Projects`, and the
versioning calculator runs it over each commit's git objects to derive the same bytes. Only commits from before spec
259 still carry a committed copy, which the calculator reads as it is, so the inputs recorded for their publishes never
move.

- `schema_version` changes when the shape changes in a way a consumer could misread. A consumer checks it when it reads the file; generation does not gate on it.
- `nodes` holds one node per project under `src/` and `tests/`, ordered by `path`, the node's identity. Each records `name`, `kind` (`source` or `test`), `domain`, `sub_domain`, `role`, `declared_target_framework` (null when inherited), and `packable`, with MSBuild's defaults applied. `package_id` and `line` (`A` or `B`, from `VersionLines.props`) are set on packable nodes and null on every other node.
- Each node's `edges` are typed. An `internal` edge is a `ProjectReference`, with the target's package `id` and project `path`; that path can name a project outside `src/` and `tests/` that has no node. An `external` edge is a `PackageReference`, with the package `id` and the `version` that central package management declares. A `pinned-transitive` edge, on packable nodes only, is a package the project reaches only through its other dependencies whose version `Directory.Packages.props` pins, with its `id` and pinned `version`: `CentralPackageTransitivePinningEnabled` makes pack write each one into the nuspec. These come from the project's committed `packages.lock.json`: its `CentralTransitive` entries that a reference not declared `PrivateAssets="all"` reaches, which is the list pack writes. So generating or deriving the dataset needs no restore; a lock file older than the project's pins or references is refused (see [NuGet lock files](../reference/nuget-lock-files.md)).
- A file belongs to the node whose directory is the longest prefix of its path. When no node's directory contains the file, it has no owner.
- It holds no publish state and no list of source files, so it changes only when the project graph does.

## Generate one layer

`-- all` writes every layer. To regenerate only the layer you need, pass its name
(`dependency-map`, `maps`, `domain`, `extension-points`, `architecture-reference`, `feature-dependency`), or use the
shims, which call the same tool and produce identical output:

Windows / PowerShell:

```powershell
tools/maps/generate-maps.ps1
tools/maps/generate-domain-map.ps1
tools/maps/generate-extension-point-map.ps1
tools/maps/generate-architecture-reference-map.ps1
tools/maps/generate-feature-dependency-map.ps1
```

macOS / Linux / Bash:

```bash
bash tools/maps/generate-maps.sh
bash tools/maps/generate-domain-map.sh
bash tools/maps/generate-extension-point-map.sh
bash tools/maps/generate-architecture-reference-map.sh
bash tools/maps/generate-feature-dependency-map.sh
```

`generate-maps` also rewrites the dependency map, because the v1 maps are projections of it.

## Freshness

```bash
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
```

- The check regenerates the solution filters into a scratch directory and byte-compares them with the committed
  `Elsa.Server.*.slnf` files, as `-- solution-filters-check` does: they are the only generated files left on `main`.
  CI runs the filter check in the `Generated maps fresh` job, beside the generator self-tests.
- The dependency map and the documentation maps cannot go stale on `main`, because none is committed: the
  calculator derives the dataset per commit (spec 259). The copy on `generated-maps` describes
  the commit its `Source-Commit:` trailer names; a local `-- all` describes your working tree.
- Review the generated findings reports, `docs/reports/maps-v1-findings.md` and `maps-v2-findings.md`, before
  relying on a map as evidence. If they expose drift that makes the current work unsafe, stop and tell the user.

## Planned maps

- **Domain map:** domains, sub-domains, `.Core` contracts, implementations, providers, and bridges.
- **Testing maturity map:** richer coverage classification beyond direct references.
- **CShells composition map:** approved shell/appsettings generation rules built from the feature dependency map plus configuration/settings classification.

## Map rule

Maps answer "what exists and how it connects." If a map needs to explain a concept, link to the glossary instead.
