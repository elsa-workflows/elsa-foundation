# Runtime Execution Evidence — Status and Weekend MVP Recommendation

**Status date**: 2026-08-06

**Program goal**: [Runtime Execution Evidence](../program-goals/runtime-execution-evidence.md)

**Product requirements / epic**: [GitHub issue #1132](https://github.com/elsa-workflows/elsa-foundation/issues/1132)

**Foundation vertical slice**: [GitHub issue #1133](https://github.com/elsa-workflows/elsa-foundation/issues/1133)

This is a delivery-status and handoff report, not a replacement for the canonical PRD, feature
specification, plan, tasks, glossary, or ADRs. A fresh control-room or worker handoff should explicitly
start by reading `docs/reports/runtime-execution-evidence-weekend-mvp-handoff.md`, then follow the
canonical references below.

## Canonical references

- [End-state PRD](../plans/runtime-execution-evidence-prd.md)
- [Program goal](../program-goals/runtime-execution-evidence.md)
- [Feature specification](../../specs/147-execution-evidence-foundation/spec.md)
- [Feature plan](../../specs/147-execution-evidence-foundation/plan.md)
- [Feature tasks](../../specs/147-execution-evidence-foundation/tasks.md)
- [Verification record](../../specs/147-execution-evidence-foundation/quickstart.md)
- Execution Evidence terms in [Elsa glossary](../glossary/elsa.md)
- Proposed ADRs 0052–0061 and 0063 under `docs/adr/`

## Executive status

- Planning PR #1139 is merged.
- Epic #1132 and child issues #1133–#1138 remain open.
- Work is on branch `779-execution-evidence-foundation`; the latest stable committed point at the time
  of this report is `83cce6c0d0190f6c1549128d76b931bf8c401bb3`.
- #1133 has 40 of 94 tracked tasks complete; 54 remain open.
- Completed work is primarily governance, architecture, four-module project skeletons, and generic
  Runtime checkpoint/provenance/replay/coalescing prerequisites.
- The actual Execution Evidence behavior is not yet delivered: the Evidence projects remain skeletons,
  without session/record implementations, capture enricher/materializer, usable HTTP endpoints, or an
  end-to-end demonstration.
- #1134–#1138 have not begun their Speckit feature flows.

## Completed work

- Established the PRD, epic, child issues, dependency sequence, and #1133 specification, plan, tasks,
  and their review gates.
- Repaired the ADR numbering collision and aligned the #1133 glossary, ADR, and module-boundary wording
  without claiming that the Draft constitution or proposed ADRs are ratified.
- Created and architecture-tested four project envelopes: contracts-only Core, provider-neutral base,
  process-local InMemory provider, and HTTP API without an InMemory dependency.
- Inventoried Runtime checkpoint callers and routed them through the generic preparation seam.
- Added generic bounded opaque context/provenance, deterministic checkpoint identity/order, prepared
  reservations, replay checks, provider implementations, post-commit intents, recovery authority, and
  coalescing/fold support.
- Added and reviewed substantial Runtime, provider, Groundwork, and architecture coverage.
- The latest bounded repair made causal Schedule/Start timestamps deterministic and passed 29/29
  focused tests.
- Exact Prepared-commit replay now restores its original recovery authority. Crash representative
  ordinal 7 passes; ordinal 10 preserves authority but remains RED because a later parent/attempt
  continuation reconstructs its timestamp at takeover time.

The latest timestamp and authority repairs are uncommitted work in progress and have not passed the
full integration gate.

## Cause of the delay

#1133 expanded into a Runtime/Groundwork production-hardening program before delivering a thin Evidence
vertical slice. Its current T029d/T030 gate requires exhaustive replay, crash convergence, authority,
coalescing, and spec-123 compatibility across multiple projects. Repairing each layer exposed another
later recovery boundary.

Build and test infrastructure is not the bottleneck: focused tests run in seconds and a complete build
is roughly a minute. The delay comes from the oversized prerequisite scope, serial RED/review/
implementation gates, and genuine replay defects.

The remaining representative recovery failure is precise: an `ActivityAttemptClaimed` commit was
originally prepared at `2026-06-12T12:00:00Z` but is rebuilt after takeover at
`2026-06-12T12:01:00.0000001Z`, producing a canonical-payload conflict. Fixing this safely requires
another causal-timestamp boundary review. It should not remain an unbounded prerequisite for a weekend
MVP demonstration.

## Remaining full-program work

Full #1133 still requires:

- T029d/T030 crash convergence and broad Runtime/Groundwork verification;
- session lifecycle and workflow association, including late-attach and race behavior;
- four deterministic baseline fact descriptors and capture adapter;
- Evidence-owned idempotent post-commit materialization and InMemory stores;
- completeness/integrity reconciliation and bounded query/wait behavior;
- full protected API and explicit server composition;
- documentation, end-to-end tests, benchmarks, generated-map refresh, and final architecture review.

After #1133, the program still requires #1134 committed lifecycle/completeness, #1135 stimulus and
scheduling causation, #1136 state/value capture, #1137 Groundwork durability/distribution, and #1138
conformance fixtures/J-Test integration.

## Recommended Sunday MVP cut

Preserve the current recovery work intact on its branch. Create a clean weekend MVP branch from
`83cce6c0d` or a subsequently reviewed green checkpoint. Explicitly identify the result as a reduced,
process-local demonstration; it cannot close #1133.

Keep only:

1. The validated module boundaries and generic Runtime checkpoint/provenance/post-commit-intent seam.
2. Association before workflow start through opaque generic start context; Runtime never names or
   interprets Evidence.
3. Process-local InMemory session and record stores.
4. Fixed metadata-only `workflow.started`, `workflow.completed`, `activity.started`, and
   `activity.completed` facts.
5. Deterministic identities and ordering derived from committed checkpoint provenance.
6. A deterministic checkpoint enricher, opaque Evidence batch intent, and Evidence-owned idempotent
   post-commit handler.
7. Explicit base + InMemory + API composition.
8. Three protected HTTP operations: create session, associate-and-start, and list session records.
9. One ordinary, non-fused workflow demonstration proving unassociated execution produces no Evidence,
   associated execution produces four ordered facts, duplicate delivery produces no duplicates, and
   materializer failure does not roll back the committed workflow.

Defer late attach, association races/fencing/freeze, full operation receipts, six-status reconciliation,
terminal cutoffs, completeness/integrity, completion/delete, advanced catalog extension, registered-
unknown records, filters/cursors/waits, exhaustive Groundwork crash convergence, benchmarks, the full
end-to-end matrix, and all #1134–#1138 implementation.

The MVP must make no durability, restart, failover, completeness, or definitive-negative claim.

## Estimate

- Direct in-process contingency demonstration without HTTP: 12–18 focused engineering hours.
- Recommended HTTP MVP: best 22–28 hours; likely 32–42 hours; worst 50–65 hours if generic start-context
  or server/auth composition exposes hidden work.
- Full #1133 as specified: approximately 10–15 additional focused engineering days.
- The complete six-feature epic is not credible by Sunday and cannot be responsibly committed until
  #1134–#1138 have completed their specification and planning stages.

Suggested schedule:

- Thursday: approve the cut, create the clean branch, and implement contracts/start context/stores.
- Friday: implement deterministic capture/materialization and make the in-process demonstration green.
- Saturday: compose the modules, add the three endpoints, and add TestServer coverage.
- Sunday: run a dedicated-server smoke test, document and independently review the result, fix findings,
  and retain delivery buffer.

## Decision required

Approve the reduced Sunday MVP as a non-closing subset of #1133 while preserving the full recovery branch
for later completion. Retaining T029d/T030 and every current #1133 acceptance path as Sunday prerequisites
makes a working MVP unlikely.

## Suggested skills for the next session

- `speckit-implement`
- `elsa-create-feature`
- `elsa-feature-composition`
- `review`
- `elsa-verify-codebase`
- `elsa-source-of-truth-audit`
- `elsa-refresh-generated-maps` only after explicitly authorized input changes
- `handoff`
