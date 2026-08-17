# Phase 1 Data Model: Final FastEndpoints Retirement

This unit's only durable data structure is the classification artifact required by FR-001. It is a
document, not a runtime type, but it is modelled here because FR-002 makes it the authority that
gates every removal, and because §2.25.4 makes it the basis of the completion report.

## Entity: Classification entry

One row per reference. The set of rows is the authoritative scope of the unit.

| Field | Required | Description |
|---|---|---|
| `reference` | yes | Repository-relative path, plus a symbol or configuration key when the file is only partly affected. |
| `kind` | yes | `project-reference`, `package-reference`, `code-usage`, `configuration`, or `prose`. |
| `disposition` | yes | `Remove`, `Preserve`, `Archive`, `Re-anchor`, or `Unresolved`. |
| `reason` | yes | One line. For `Preserve`, names the guarantee protected, not merely that it compiles. |
| `evidence` | yes once acted on | What verified the disposition. See the evidence rules below. |

### Validation rules

- **V-1**: Every reference appears exactly once. Dispositions are mutually exclusive.
- **V-2**: The union of all entries covers every reference discovered. Coverage is asserted, not assumed.
- **V-3**: `Unresolved` is a valid terminal state during execution but blocks its own reference from removal. Zero `Unresolved` entries may remain at merge (SC-002).
- **V-4**: A `Preserve` reason that says only "still compiles" or "still used" is invalid. It must name the guarantee.
- **V-5**: No entry may move to `Remove` without evidence that satisfies §2.25.3. A text search is never sufficient.

### Evidence rules by kind

| `kind` | Admissible evidence for `Remove` |
|---|---|
| `code-usage`, `project-reference` | Solution builds and affected suites pass after deletion. |
| `package-reference` | Solution builds after the reference is dropped, and the retirement guard still runs. |
| `configuration` | The composition activates cleanly; the compiler cannot see string-keyed feature names. |
| `prose` | Human reading after removal. Prose has no build-time reachability, so a search plus reading is the instrument here, and this is the one kind where a search is appropriate. |

## Entity: Disposition

| Value | Meaning | Constitutional note |
|---|---|---|
| `Remove` | Transitional; no third-party compatibility purpose and no guarantee left unguarded. | §2.25.2 standing applies. |
| `Preserve` | Guards a guarantee outliving the program, or is required for one. | Must survive with its assertion intact. |
| `Archive` | Frozen evidence retained for investigation, no longer regenerated. | Record what it proved and why it is no longer reproducible. |
| `Re-anchor` | Guards a preserved guarantee but depends on a removed surface. | Assertion must be unchanged in substance. |
| `Unresolved` | Disposition not established. | Blocks removal of its own reference. |

## Entity: Retirement guard

The mechanism proving the first-party registration surface is empty: `FastEndpointsTransitionTests`
with `TransitionExceptionValidator` and the `fastendpoints-transition-exceptions.json` baseline,
currently `[]`.

**State**: must remain `passing` throughout and after the unit. Its own disposition is fixed to
`Preserve` by R-004. If a removal would break it, the removal is what gives way, not the guard.

## Entity: Completion report

The program's closing record under `docs/reports/`. §2.25.4 requires two lists, not one:

1. **Retired** — what was removed, with its evidence.
2. **Examined and deliberately kept** — what was looked at and preserved, with the reason.

Plus, from the spec: route and owner counts, residual third-party compatibility boundaries, risks,
rollback guidance, and the recorded withdrawal of mixed-host guard coverage (FR-011).

## Notes on what is deliberately *not* modelled

There is no schema for "FastEndpoints usage" as a domain concept. This unit does not build anything;
it removes. Modelling the removed abstractions would produce a data model of code about to cease
existing.
