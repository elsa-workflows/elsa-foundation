# Data model: Generated artifacts never block or conflict

This unit has no persisted domain data. Its "entities" are repository artifacts and their states.

## Documentation map set

| Member | Path (same on `generated-maps` and in a local checkout) | Committed on `main` |
|---|---|---|
| Project reference map | `docs/maps/project-reference-map.md` | no |
| Package map | `docs/maps/package-map.md` | no |
| Feature map | `docs/maps/feature-map.md` | no |
| Feature dependency map | `docs/maps/feature-dependency-map.md` | no |
| Test map | `docs/maps/test-map.md` | no |
| Spec status map | `docs/maps/spec-status-map.md` | no |
| Domain map | `docs/maps/domain-map.md` | no |
| Extension-point map | `docs/maps/extension-point-map.md` | no |
| Architecture reference map | `docs/maps/architecture-reference-map.md` | no |
| Manifest | `docs/maps/manifest.json` | no |
| Maps v1 findings | `docs/reports/maps-v1-findings.md` | no |
| Maps v2 findings | `docs/reports/maps-v2-findings.md` | no |
| Set README | `README.md` (branch root only) | no |

**Invariants**

- The set is a function of one source commit. Generating it twice from the same tree gives identical bytes (FR-010).
- In the published form, a relative link whose target is outside the set is rewritten to `https://github.com/elsa-workflows/elsa-foundation/blob/<source-sha>/<path>`. Links inside the set stay relative.
- Git ignores the set on `main` (FR-001).

## Committed generated inputs (unchanged)

| Member | Path | Read by | Freshness |
|---|---|---|---|
| Dependency map dataset | `docs/maps/dependency-map.json` | versioning calculator, `DependencyMapTests`, `NuGetLockFileTests` | `Generated maps fresh` → `check` |
| Solution filters | `Elsa.Server.*.slnf` | CI, contributors | `Generated maps fresh` → `solution-filters-check` |

## `generated-maps` branch

- **Commit**: tree = one documentation map set. Author and committer `github-actions[bot]`. Message `Maps for <short-sha>`, plus the trailer `Source-Commit: <full-sha>`.
- **Ordering rule**: a new commit is added only if its `Source-Commit` descends from the tip's `Source-Commit`. A run for an older or unrelated main commit exits as superseded.
- **No-op rule**: if the new tree equals the tip's tree, nothing is committed.
- **Protection**: ruleset `generated-maps` (deletion and non-fast-forward), plus the hygiene-sweep exemption.

```text
(absent) --bootstrap orphan commit--> tip(S1)
tip(Sn) --push for S(n+1), a descendant of Sn--> tip(S(n+1))
tip(Sn) --run for Sk, not a descendant of Sn--> tip(Sn)  [superseded, exit 0]
tip(Sn) --same tree--> tip(Sn)                           [unchanged, exit 0]
```

## Change scope (per PR run)

| Field | Values | Rule |
|---|---|---|
| `docs_only` | `true` / `false` | `true` only on `pull_request` when the diff is non-empty and every path matches an include and no exclude (research R2); any error → `false` |
| `reason` | text | written to the step summary: the first path that forced a full run, or "documentation-only: N paths" |
