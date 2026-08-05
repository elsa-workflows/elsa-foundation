# Activity contract parity tooling

Mechanical extraction and diff of the **activity contract surface** — every activity's inputs,
outputs, outcome ports, child slots and trigger flag — for `elsa-foundation` (Elsa 4) and
`elsa-core` (Elsa 3).

These scripts back [`docs/reports/elsa-4-activity-contract-parity-2026-08.md`](../../docs/reports/elsa-4-activity-contract-parity-2026-08.md).
Re-run them whenever an activity contract changes so the report and its evidence stay honest.

## Why source parsing rather than reflection

The obvious implementation is to run the production reflection-only scanner
(`ClrAssemblyScanner`, `src/Elsa/Activities/Design/Reconciliation/Clr/Services/ClrAssemblyScanner.cs`)
over the built assemblies. That is the better long-term answer and is the intended shape of the
committed snapshot guard.

These scripts exist because the contract surface is entirely **attribute-declared**, so it can be read
straight from source — which means the audit can also run against a checkout of `elsa-core` that is
never built, and in environments where the private preview feeds are unreachable. Treat the output as
the declared contract, not as proof of runtime behaviour.

## Requirements

Python 3.9+. No third-party packages. Note this is currently the only Python tooling in the repo — the
rest of `tools/` is PowerShell + bash.

## Usage

```bash
# 1. Elsa 4 surface (defaults to this repo)
python3 tools/parity/extract-elsa4-activity-surface.py \
  > docs/reports/evidence/activity-contract-parity/elsa4-surface.json

# 2. Elsa 3 baseline (needs an elsa-core checkout)
ELSA_CORE_ROOT=/path/to/elsa-core \
  python3 tools/parity/extract-elsa3-activity-surface.py \
  > docs/reports/evidence/activity-contract-parity/elsa3-surface.json

# 3. Diff — markdown table on stdout, findings JSON written next to the surfaces
python3 tools/parity/diff-activity-surfaces.py
```

Overrides: `ELSA_FOUNDATION_ROOT`, `ELSA_CORE_ROOT`, `ELSA_PARITY_DIR`.

## Reading the diff

`diff-activity-surfaces.py` carries the reviewable judgement calls as explicit tables at the top of
the file — edit these rather than eyeballing the output:

| Table | Meaning |
|---|---|
| `ACTIVITY_MAP` | Elsa 3 activity → Elsa 4 activity. `None` means no counterpart; `@intrinsic:X` means the capability became an engine intrinsic (ADR 0045). |
| `MEMBER_RENAMES` | Declared renames, so a rename is not double-counted as a removal plus an addition. |
| `INTENTIONAL_DIVERGENCE` | The capability exists in Elsa 4 through a different mechanism. Each entry states which. |
| `IGNORED_E3_MEMBERS` | Elsa 3 base-class artefacts and members obsolete in Elsa 3 itself. |
| `NOT_AUTHOR_FACING` | Elsa 3 engine/composition internals with no toolbox entry to reach parity with. |

Verdicts: `gap` (member-level shortfall), `missing` (no Elsa 4 activity), `moved-to-intrinsic`,
`present`, `elsa4-only`, `not-author-facing`.
