# Knowledge Inventory

Status: initial operating-model inventory.

## Classification

| Area | Current role | Canonical future role | Finding | Recommendation |
|---|---|---|---|---|
| `README.md` | Minimal repo description | Human repo identity and pointers | Too small to orient engineers or agents | Keep concise; point to `AGENTS.md` and docs index |
| `AGENTS.md` | New | Provider-neutral entrypoint | Needed so AI providers share one front door | Canonical bootstrap for agents and engineers |
| `CLAUDE.md` | Claude-specific operating manual | Claude compatibility shim | Contained canonical guidance and sibling-repo dependency | Keep thin; point to `AGENTS.md` |
| `.specify/memory/constitution-framework.md` | Framework constitution plus history/glossary/examples/follow-ups | Generic quality gates and governance | Contains non-gate material and draft history | Thin over time; move concepts to glossary and open items to reports |
| `.specify/memory/constitution.md` | Elsa constitution plus history/glossary/examples/follow-ups | Elsa-specific gates, overrides, ratification state | Contains explanations, worked examples, and unresolved work | Preserve gates; move explanations to docs/glossary and findings to reports |
| `docs/seams.md` | Worked concept doc | Worked reference linked from glossary | Useful but not indexed | Keep as reference; glossary owns short definitions |
| `docs/serialization.md` | Rule/exceptions doc | Worked reference linked from glossary/constitution | Useful focused rule | Keep as reference; avoid duplicating in skills |
| `docs/glossary/*` | New | Canonical term definitions | Needed to reduce constitution and skill duplication | Grow as terms appear |
| `docs/skills/catalog.md` | New | Provider-neutral skill catalog | Needed before provider-specific skill wrappers | Treat as canonical skill/workflow source |
| `docs/maps/*` | New | Navigation/generated maps | Needed for feature/dependency/test visibility | Add generated maps in later work units |
| `docs/reports/*` | New | Point-in-time findings and gap reports | Needed for unfinished work and compliance drift | Refresh before planning corrective work |
| `EXTENSION_POINTS.md` | Repo-wide extension index | Repo-wide map/catalog | Already close to target | Keep as map; avoid glossary-style explanations except short legend |
| Project `EXTENSION_POINTS.md` | Per-domain extension catalogs | Authoritative replace/contribute/event surfaces | Strong local pattern exists | Preserve; verify completeness in compliance reports |
| Project `README.md` files | Feature docs | Per-feature behavior and cross-domain contributions | Mixed depth likely | Inventory later by domain |
| `specs/*` | Speckit specs/work units | Planned changes with acceptance criteria | Some specs are superseded or retained for intent | Add status map later |
| `.claude/skills/*` | Claude Speckit skills | Provider-specific adapters | Executable and should remain discoverable | Do not make canonical architecture docs |
| `.specify/workflows/*` | Speckit workflow definitions | Tool workflow adapter | Already integration-aware | Preserve |
| `.specify/extensions/git/*` | Speckit git extension | Branch/spec flow support | Supports official flow | Preserve and document through skill catalog |

## Duplicate knowledge to unwind

- Constitution glossary tables overlap with new glossary pages.
- Constitution worked examples overlap with future reference docs.
- `CLAUDE.md` previously duplicated repo role, Speckit flow, and constitution pointers.
- Extension-point explanations appear in both root and per-domain catalogs; keep root as index and per-domain files as details.

## First extraction targets

1. Move stable term definitions from constitution glossary sections into `docs/glossary/`.
2. Move draft/follow-up state from constitution comments into `docs/reports/unfinished-work.md`.
3. Move long worked examples from constitution sections into `docs/` references, leaving short gate text and links.
4. Add generated project/package maps before making dependency-compatibility claims.

## Review notes

This report intentionally does not move large constitution sections yet. It records the safe extraction path so the next work unit can thin the constitutions section by section with review.
