# Implementation Plan: DispatchWorkflow Parent Audit Remediation

**Branch**: `codex/dispatch-674-audit`  
**Spec**: [spec.md](spec.md)

## Summary

Resolve every actionable finding from the parent-program review, add the missing acceptance evidence, and rewrite the audit so it reflects verified behavior. Preserve provider-neutral runtime contracts, deterministic identities, bounded operations, and the explicit `WorkflowDefinitionActivity`/Studio exclusions. Do not refresh generated maps.

## Technical Context

- **Language/runtime**: C# on .NET 10
- **Primary projects**: `Elsa.Workflows.Runtime.Core`, `Elsa.Workflows.Runtime`, `Elsa.Workflows.Runtime.Api`, `Elsa.Activities.DispatchWorkflow.Runtime`, and Groundwork persistence implementations
- **Persistence**: in-memory runtime stores and Groundwork bounded document stores
- **Testing**: xUnit unit, contract, Groundwork convergence, distributed two-node, architecture, and solution suites
- **Constraints**: provider-neutral contracts, deterministic idempotency, bounded reads, tenant-safe projections, no new external packages

## Inputs

- GitHub issue #674 body, inspected read-only.
- Speckit work units:
  - `specs/096-dispatch-workflow-fire-and-forget/`
  - `specs/097-dispatch-dependency-hardening/`
  - `specs/098-dispatch-durability-inspection/`
  - `specs/099-dispatch-wait-success/`
  - `specs/100-dispatch-fault-cancellation/`
  - `specs/101-dispatch-redrive-failures/`
  - `specs/102-dispatch-test-run-scope/`
  - `specs/103-dispatch-distributed-nodes/`
- Final verification commands recorded in the audit report.
- The findings captured in [research.md](research.md) and remediation contracts under [contracts/](contracts/).

## Constitution Check

- **Source-of-truth layering**: PASS. The audit result is a report; it does not duplicate glossary or map content.
- **Feature-workspace boundary**: PASS. The audit remains in the current worktree and does not alter the read-only draft #098 worktree.
- **Runtime/Design boundary**: PASS. All remediation remains inside Runtime, DispatchWorkflow activity, API, and provider persistence surfaces.
- **Provider boundary**: PASS. Core contracts remain provider-neutral; Groundwork-specific limits and conditional writes remain in the provider.
- **Test continuity**: PASS. Existing tests remain and missing branch/fault/convergence tests are additive.
- **Remote mutation boundary**: PASS by current user authority. Push, PR creation, automated review convergence, and merge are authorized; issue edits remain out of scope.
- **Generated maps**: PASS by user instruction. Generated-map regeneration remains skipped; the final report distinguishes prior deltas from this remediation.

## Deliverables

- `docs/reports/dispatch-workflow-674-parent-audit.md`
- Crash-safe final-failure/redrive/resume behavior and regression tests
- Race-safe lifecycle, retention, and TestRun cleanup behavior and regression tests
- Contract-correct safe Runtime API inspection/redrive behavior
- Provider-bounded dispatch/outbox queries
- Missing Groundwork and integrated distributed acceptance suites
- Completed task ledger in [tasks.md](tasks.md)

## Verification

Use [quickstart.md](quickstart.md), run the self-review loop for up to ten iterations, then finish with `git diff --check`.

## Complexity Tracking

- Additive lifecycle/outbox state or conditional persistence operations are permitted only where they close a demonstrated crash or race window.
- Any new public contract member must be additive and provider-neutral, with in-memory and Groundwork implementations plus branch-complete tests.
- No existing test objective is removed.
