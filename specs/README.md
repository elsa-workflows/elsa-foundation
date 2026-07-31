# Specs

Feature and work-unit specifications, one directory per unit, produced through the Speckit flow
(`speckit-specify` → `speckit-plan` → `speckit-tasks` → `speckit-implement`).

A spec directory is the durable record of *what a unit set out to do and why*. It is not the
architecture — enforceable rules live in the constitutions under `.specify/memory/`, meanings live
in [`docs/glossary/`](../docs/glossary/), and current findings live in
[`docs/reports/`](../docs/reports/README.md). See [AGENTS.md](../AGENTS.md) for the full
source-of-truth table.

## Finding a spec

- **[Spec status map](../docs/maps/spec-status-map.md)** — generated index of every spec with its
  status and task counts. Start here.
- Directory names are `NNN-kebab-case-title`. The number is allocation order, not priority.

## Reading a spec

Typical contents, though not every unit produces all of them:

| File | What it holds |
|---|---|
| `spec.md` | The specification: user scenarios, requirements, acceptance criteria. Carries the `**Status**:` line. |
| `plan.md` | Implementation plan and technical context. |
| `tasks.md` | Dependency-ordered task list with checkboxes. |
| `research.md` | Investigation results, measurements, refuted hypotheses. |
| `data-model.md`, `contracts/` | Data shapes and contract definitions. |
| `quickstart.md` | Verification walkthrough — how to prove the unit works. |
| `checklists/` | Per-unit review checklists. |

## Lifecycle

Numbering, the status vocabulary, and when a spec stops being live are defined in
**[docs/reference/spec-lifecycle.md](../docs/reference/spec-lifecycle.md)**.

Two rules worth knowing before you touch anything here:

- **A spec reaches a terminal status in the same unit of work that finishes it.** The PR that merges
  the implementation is the PR that sets `**Status**: Implemented`.
- **Specs are never moved or deleted.** A directory keeps its path forever; specs are cross-linked
  from reports, maps, ADRs, other specs, and code. "Retired" means terminal status, not relocated.

Two known conditions of the existing tree, both deliberate: **27 duplicate numbers** are
grandfathered (renumbering would rewrite ~900 links for a cosmetic result), and many specs carry a
stale `Draft` status. Neither is bulk-repaired by guesswork — a status is corrected when someone has
real knowledge of the outcome.
