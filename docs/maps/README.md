# Maps Index

Maps are navigation and generated-fact surfaces. They should be cheap to refresh and should not become long-form concept documentation.

## Existing maps

- [Repo-wide extension points](../../EXTENSION_POINTS.md) - index of per-domain extension catalogs.
- Per-domain `EXTENSION_POINTS.md` files under `src/` - replace/contribute/event surfaces owned by each domain.
- [Project reference map](project-reference-map.md) - direct project references.
- [Package map](package-map.md) - direct external package references and version clusters.
- [Feature map](feature-map.md) - discovered CShells feature classes.
- [Feature dependency map](feature-dependency-map.md) - CShells feature IDs, public feature properties, and dependency evidence from project/package references.
- [Test map](test-map.md) - test projects and direct production references.
- [Spec status map](spec-status-map.md) - current Speckit work-unit status clues.
- [Domain map](domain-map.md) - project grouping, roles, and direct cross-domain references.
- [Extension-point map](extension-point-map.md) - generated facts from root and per-domain `EXTENSION_POINTS.md` catalogs.
- [Architecture reference map](architecture-reference-map.md) - direct design/runtime reference signals for review.
- [Map manifest](manifest.json) - point-in-time freshness metadata and input fingerprint.

## Refresh Maps v1

Windows / PowerShell:

```powershell
tools/maps/generate-maps.ps1
```

macOS / Linux / Bash:

```bash
tools/maps/generate-maps.sh
```

If the local shell is unavailable, try the other script if that shell is installed. If neither
PowerShell nor Bash is available, install one of them before refreshing maps.

## Refresh Maps v2

Each v2 map has its own generator. Run only the map layer you need.

Windows / PowerShell:

```powershell
tools/maps/generate-domain-map.ps1
tools/maps/generate-extension-point-map.ps1
tools/maps/generate-architecture-reference-map.ps1
tools/maps/generate-feature-dependency-map.ps1
```

macOS / Linux / Bash:

```bash
bash tools/maps/generate-domain-map.sh
bash tools/maps/generate-extension-point-map.sh
bash tools/maps/generate-architecture-reference-map.sh
bash tools/maps/generate-feature-dependency-map.sh
```

## Freshness

Generated maps are committed point-in-time snapshots. Use [manifest.json](manifest.json) before relying on them.

- `input_fingerprint` is the authoritative freshness signal for the tracked map inputs.
- `git_head` is advisory because maps are often generated before the commit that records them.
- `relevant_inputs_dirty` tells you whether `src/`, `tests/`, `specs/`, or `tools/maps/` had uncommitted changes when the maps were generated.
- If a workflow invokes a map and relevant inputs are dirty, changed, or freshness is uncertain, refresh the narrowest relevant map layer before using it as evidence.
- After refreshing, review generated findings reports such as `docs/reports/maps-v1-findings.md` or `docs/reports/maps-v2-findings.md` before continuing.
- If the refreshed report exposes drift that makes the current work unsafe, stop and tell the user rather than continuing from stale assumptions.

## Planned maps

- **Domain map:** domains, sub-domains, `.Core` contracts, implementations, providers, and bridges.
- **Testing maturity map:** richer coverage classification beyond direct references.
- **CShells composition map:** approved shell/appsettings generation rules built from the feature dependency map plus configuration/settings classification.

## Map rule

Maps answer "what exists and how it connects." If a map needs to explain a concept, link to the glossary instead.
