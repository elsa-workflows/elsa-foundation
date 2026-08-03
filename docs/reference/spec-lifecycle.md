# Spec Lifecycle

This catalog defines how specs under `specs/` are numbered, how their status moves, and when they
stop being live. It is committed reference material, not a personal preference file.

It exists because the repository had no such policy: at the time of writing, 175 spec directories
carry 175 `spec.md` files, 105 of which still say `**Status**: Draft` — including specs whose work
merged months ago — 42 carry no status line at all, and the remainder use roughly fifteen freeform
status strings. Nothing was ever wrong with any single spec; there was simply no rule saying when a
spec stops being live, so none of them ever did.

See also: [Spec status map](../maps/spec-status-map.md) (generated), and
[the simplification review](../reports/simplification-review-2026-07.md) §D2 for the finding that
prompted this.

## Numbering

**One lane, monotonically increasing.** The next spec takes the highest existing number plus one,
across the whole `specs/` tree — not per topic, not per program-goal bucket.

The tree currently contains **27 duplicate numbers** (`015-*` ×3, `090-*` ×3, `092-*` ×3, `095-*` ×3
and 23 more) because a runtime lane and a groundwork lane allocated numbers concurrently. Those
collisions are **not** to be repaired: renumbering would rewrite 884 spec-path links across 61
markdown files plus 14 references from C#, for a cosmetic result. They are grandfathered. The rule
above prevents new ones.

When two work units start at once, allocate both numbers up front rather than letting each pick.

## Status vocabulary

A spec's `**Status**:` line MUST hold exactly one of these values, optionally followed by ` — ` and
free prose (a PR link, a date, a verdict summary).

| Status | Meaning | Terminal |
|---|---|---|
| `Draft` | Being written, or written and awaiting approval. | no |
| `Approved` | Approved to implement; work not finished. | no |
| `In progress` | Implementation underway. | no |
| `Implemented` | The work merged. The spec now describes shipped behaviour. | **yes** |
| `Superseded` | Replaced by another spec or ADR. MUST name the successor. | **yes** |
| `Abandoned` | Deliberately not pursued. MUST say why. | **yes** |

`Draft` is the default a new spec is born with, and it is the value that silently rots. Treat any
spec sitting in a non-terminal status after its work has merged as a bug in the record.

## Transition rule

**A spec reaches a terminal status in the same unit of work that finishes it.** Concretely: the PR
that merges the implementation is the PR that sets `**Status**: Implemented`. The PR that lands a
replacement sets the old spec to `Superseded` and names the successor.

This is the whole policy. Everything else here is vocabulary in service of it.

Where a spec's outcome was a decision not to build — a refuted hypothesis, a kill verdict — use
`Abandoned` and record the verdict; that is a real and valuable result, not a failure state.

## Specs are never moved or deleted

A spec directory keeps its path forever, including after it reaches a terminal status. Specs are
cross-referenced from reports, maps, ADRs, other specs, and code comments; moving one to an
`archive/` directory breaks those links for no navigational gain that a status field does not
already provide.

"Retired" means *terminal status*, not *relocated*.

## Discoverability

- [`specs/README.md`](../../specs/README.md) is the hand-maintained entry point: what the tree is,
  how to read a spec, and where the generated index lives.
- [`docs/maps/spec-status-map.md`](../maps/spec-status-map.md) is the generated index. It reads the
  `**Status**:` line, so the vocabulary above is what makes it useful — a map over 105 identical
  `Draft` values cannot tell anyone anything.

## Applying this to the existing tree

The 175 existing specs are **not** to be bulk-restatused by guesswork. A spec's status is corrected
when someone has real knowledge of its outcome — while working in that area, or while reconciling a
program-goal bucket. The generated map surfaces the backlog; it does not need to be drained at once.

The 42 specs with no status line at all should gain one when next touched.
