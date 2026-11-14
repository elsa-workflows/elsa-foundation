# Workflow Releases and Environment Promotion

- **Status:** Active for owner-authorized end-to-end delivery, 5 November 2026; resumed in reviewable slices on 12 November 2026.
- **Area:** Workflows.Publishing coordination, owner-managed dependency artifacts and runtime bindings; Studio/API/CLI.
- **Steward:** Sipke. One active implementation slice at a time.
- **Canonical request/program:** [#3590](https://github.com/elsa-workflows/elsa-foundation/issues/3590).
- **Authorization:** [owner receipt](https://github.com/elsa-workflows/elsa-foundation/issues/3590#issuecomment-6671598586), recorded in [ADR 0080 D0](../adr/0080-elsa-4-simplification-decisions.md#d0--focus).
- **Specification:** [spec 1225](../../specs/1225-workflow-release-promotion/spec.md).

## Purpose and scope

Create an immutable release from selected exact workflows and their supported required dependencies,
test that content and promote it unchanged through environments with target-owned resource bindings.
Provide equivalent Studio and API/CLI preview/apply, durable activation/recovery, rollback for future
starts, exact run pins, history, diagnostics and practical examples.

The release is a bundle and coordination record, never another serving/start authority. Reuse existing
publication owners, immutable stores, closure validation, retention, resource permissions and backend
sessions. Unsupported dependencies must fail visibly rather than disappear from the bundle.

## Sequencing and milestones

| Stage | Outcome | Completion evidence |
|---|---|---|
| M0 — reviewed contract | Source-grounded supported artifact profile, identity/portability, target bindings, evidence, owner coordination/recovery and Studio acceptance design | Reviewed spec/plan/contracts and resolved consequential decisions; no runtime acceptance claim |
| M1 — immutable releases | Reproducible multi-root bundle, complete supported closure, exact identity-preserving offline import/export and explicit retention | Shared reusable dependency round-trip, corruption/cycle/identity/unsupported controls and package consumer proof |
| M2 — target readiness | Authenticated target capability/resource discovery, immutable mapping capture and equivalent fresh preview/apply validation | Two owned target environments with different mappings; denied/tenant/missing/drift/stale controls |
| M3 — durable promotion | Existing-owner coordinated activation, per-run mappings, retries/concurrency, interruption recovery and explicit rollback | Real composed-host partial/unknown/superseded recovery and long-running pin/binding continuity |
| M4 — integrated delivery | Complete Studio/API/packaged CLI journeys, evidence/history/diagnostics, operator/integrator docs and examples | Current-commit downloadable composed UI evidence, independent UX acceptance, meaningful regressions, reviewed green merge and resulting-main qualification |

Studio design begins in M0 and its journeys develop with the vertical slices. M4 is integrated acceptance,
not permission to leave the UI as an isolated fixture until the end. Keep passed/failed/skipped/cancelled/
unavailable/incomplete evidence separate. The program completes only when all agreed #3590 success criteria
are delivered and qualified; a manifest, prototype, plan or draft PR is insufficient.

## Decisions and coordination

The owner confirmed coordinated per-owner switches with a visible mixed-version interval; mappings
captured per run while normal credential rotation/revocation remains; forward recovery with explicit
rollback. The owner selected native, BPMN and ETL support in the first complete delivery; native-only
support cannot complete the program. Each model retains its own execution, publication and dependency semantics.
Direct BPMN must follow publication-slot eligibility for fresh starts, including explicit activation of older unslotted
publications. Running models and already accepted work remain pinned; this compatibility change was selected by the owner.

- [#3588/#3892 testing coordination](https://github.com/elsa-workflows/elsa-foundation/issues/3588#issuecomment-6671608363): frozen candidate/closure evidence and exact case revisions; preserve controlled versus actual integration claims. Promotion does not wait for the whole test-suite product or impose a universal gate.
- [#3582 trigger boundary](https://github.com/elsa-workflows/elsa-foundation/issues/3582#issuecomment-6671609180): preserve independently managed trigger ownership; no implementation is assumed or added here.
- Shared Publishing Core #3838 is consumed as publication/readiness policy, not a group-deployment transaction. Git authoring reconciliation is not exact tested-content promotion or rollback.
- Accepted ADR 0043 remains authoritative. ADR 0034 and ADR 0043's October convergence amendment remain proposed. This program does not ratify draft/provisional architecture or assume Initiative #3052 agreement.

## Boundaries and bookkeeping

Run beside current priorities, preserve consolidation's priority one and all existing owner-directed pauses.
No unrelated stream is resumed. #3590 had no Initiative parent/type/`Part of` linkage at intake; normal scoped
design applies unless an actual future linkage or governed-contract dependency changes that boundary.

At establishment the repository had 77 open `ready-for-agent` issues, exceeding its cap of 20. Keep the
milestones/checklists on #3590; add no new ready labels or issue hierarchy. No project board is required.
Record every state transition, contract decision, PR and evidence outcome on the canonical issue.

Promotion exercises use owned disposable test hosts/stores. Shared/production deployment, software/package
release publication, infrastructure provisioning, secret transfer, server/schema upgrades, running-instance
migration and reversal of external business effects remain outside this delivery authorization.

## Current delivery state — 12 November

PR #3911 closed without merge on 9 November because the feature exceeded reviewable size. Its work and evidence
remain retained. Main includes the active authorization/registry row, but no release product implementation.
The [resubmission sequence](../../specs/1225-workflow-release-promotion/delivery-slices.md) starts with contracts
and then restores and qualifies each foundation before owner integration and user journeys. All M0–M4 completion
gates remain open. Historical passing suites and the four-case claim fix are not current-main acceptance.

The original spec allocation 1216 collided with another reservation. The initial resubmission reserved 1224 on
[#3590](https://github.com/elsa-workflows/elsa-foundation/issues/3590#issuecomment-6837417950).
Main subsequently acquired `1224-optional-bpmn-js-editor` through #4225. This PR uses 1225 to remove the collision;
this is a numbering correction only and leaves the BPMN program unchanged.
