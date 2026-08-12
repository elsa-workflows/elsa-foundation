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
- [Map manifest](manifest.json) - what the v1 layer covers: project, package, feature and spec counts, and the files it generates.

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

Generated maps are committed point-in-time snapshots. The authoritative freshness signal is the
check, not a field in a file:

```bash
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
```

- The check regenerates every map into a scratch directory and byte-compares it with what is committed, [manifest.json](manifest.json) included. Green means every committed map still describes the tree. CI runs it on every pull request and on every push to `main` (`.github/workflows/maps.yml`), so main going stale is reported against main.
- It deliberately does not gate on a fingerprint over the inputs. Such a fingerprint moves on every source edit, so it would oblige every code PR to regenerate and commit twelve map files even though most source edits change no map at all.
- **The manifest describes the tree, not the commit.** Every field is a function of the tree, so `-- all` rewrites it to the same bytes unless a count or a generated file actually changed. Stage it along with the other changed map files. Until #1278 it also carried `git_head`, `input_fingerprint`, `input_file_count` and `relevant_inputs_dirty`, which moved on every commit and made two concurrent map PRs conflict on this file for no semantic reason (PR #1247 went `CONFLICTING` the moment #1248 merged for exactly that, while #1243 left the manifest alone and stayed mergeable). Those fields are gone. If you want to know which commit generated a snapshot, `git log docs/maps/` answers it.
- Map generation is opt-in: do not refresh automatically when the check is red or freshness is uncertain. Report the stale snapshot and let the user explicitly invoke or authorize the narrowest relevant map layer.
- After an explicitly authorized refresh, review generated findings reports such as `docs/reports/maps-v1-findings.md` or `docs/reports/maps-v2-findings.md` before continuing.
- If the refreshed report exposes drift that makes the current work unsafe, stop and tell the user rather than continuing from stale assumptions.

## Planned maps

- **Domain map:** domains, sub-domains, `.Core` contracts, implementations, providers, and bridges.
- **Testing maturity map:** richer coverage classification beyond direct references.
- **CShells composition map:** approved shell/appsettings generation rules built from the feature dependency map plus configuration/settings classification.

## Map rule

Maps answer "what exists and how it connects." If a map needs to explain a concept, link to the glossary instead.
