# Elsa 4 StateMachine

- **Status:** Active for owner-led design and program establishment, 4 November 2026; delivery follows the applicable design/spec agreement gates.
- **Area:** Generic StateMachine authoring, runtime integration and Studio inspection in Foundation.
- **Steward:** Sipke. Implementation ownership is claimed per leaf through Issue Bus Lite.
- **Program and canonical product/decision register:** [#3815](https://github.com/elsa-workflows/elsa-foundation/issues/3815).

## Purpose and scope

Users author and run state machines as a workflow root or nested activity, composing ordinary Elsa activities.
The first release includes both hierarchical states and parallel regions, delivered hierarchy first, with
a dedicated Studio designer, causal inspection and durable execution. Approval, revision and withdrawal are
sample application concepts, not StateMachine domain types.

The program's [owner decisions](https://github.com/elsa-workflows/elsa-foundation/issues/3815#accepted-owner-decisions-4-november-2026)
and M0 open-decision register distinguish accepted product direction from proposals. In particular, root/nested
use is agreed; native-composite versus separate execution-model placement is still to be assessed against actual
Elsa 4 contracts. Ordinary activities and composites may durably suspend in entry, exit and transition actions;
their pending phase must be visible. Selected transition work finishes before later business events are evaluated.
New machine-addressed business events are durably accepted for later evaluation under explicit capacity and
delivery guarantees; an event with no eligible transition finishes with an observable unhandled outcome rather
than being revived by a later state change. Detailed slot, ingress, lifecycle, data, persistence and failure
contracts still require M0 agreement. Late completions tied to cancelled waits remain stale.
Full SCXML compatibility/interchange, migration/import and history-state support have no implied commitment.

## Sequencing and relationships

[ADR 0080 D0](../adr/0080-elsa-4-simplification-decisions.md#d0--focus) records the scoped owner authorization.
Run beside the active programs, displace none, preserve consolidation's priority one and coordinate shared
runtime/Studio changes. Keep one active implementation leaf. Production implementation follows the agreed
contract/spec; this establishment does not approve unresolved architecture, release or deploy anything.

- [Parity #2771](elsa3-parity.md) and #1152 retain their other scope and sequencing. [ADR 0085 D10](../adr/0085-elsa-3-parity-identity-administration-and-scope.md#d10-non-identity-scope)
  routes this owner-requested revisit here, superseding only StateMachine's former deferral.
- [Native Compensation and Saga Recovery #3035](native-compensation.md) keeps its semantics and authority.
  Investigate composition; state changes/cancellation do not themselves compensate external effects.
- [Unified Execution #2892](unified-execution.md) owns its model/hosting work. M0 compares current seams and
  reuses existing scheduling, inbox, event, persistence, identity and publication authorities.
- Initiative #3052 concerns conversion and core value/expression decisions. This program has no matching
  Initiative ancestry or board topic. Any future dependency on its governed contracts follows that initiative's
  actual agreement gates; this program neither approves them nor invents an approver roster.
- Completed Elsa 3 program #1458, PRD #1456, semantics #1457 and demonstration #1462 are reference evidence,
  not Elsa 4 implementation or acceptance. M0 verifies source and justifies meaningful departures.

## Milestones and work ownership

| Milestone | Outcome and evidence | Owning issues |
|---|---|---|
| M0 — agreed design | Source-grounded product/engine/Studio contract, architecture decision, accepted semantics and traceable qualification matrix | Epic #3816; contract Feature #3820; establishment Task #3826 |
| M1 — durable hierarchy | Actual Studio-authored root/nested hierarchy, event/timer waits, scoped lifecycle and durable restart/resume | Epic #3817; generic execution Feature #3821; hierarchy Feature #3822 |
| M2 — durable parallel statecharts | Compatible same-event regional transitions, conflict arbitration, local/parent cancellation, all-region finality and recovered active configuration | Epic #3818; parallel Feature #3823 |
| M3 — integrated acceptance | Studio-to-runtime/persistence qualification, faults/retry/cancellation/races/restarts, exact versions, documentation and resulting-main evidence | Epic #3819; Studio Feature #3824; qualification Feature #3825 |

Studio design starts in M0; authoring and inspection ship with the M1/M2 slices rather than waiting for M3.
Native issue parent links carry the hierarchy. Blocking dependencies are: #3821 and #3824 need #3820;
#3822 needs #3821; #3823 needs #3822; #3825 needs #3822, #3823 and #3824. The program issue owns the full
requirements/evidence matrix instead of duplicating it here. Implementation leaves are refined after design
agreement; newly created records remain `needs-triage` until a maintainer changes their state.

## Current objective and completion

Establishment #3826 records the program setup and deferral reconciliation; #3820 owns the continuing owner
design session. The still-open M0 choices include the implementable event ordering/capacity/delivery and outcome
contract, event/trigger lifetime and matching, complete hierarchical/parallel selection and recovery semantics,
data/outcomes/versioning, Studio journeys and saga composition. The program issue owns the evolving decision
register; the accepted product direction does not by itself approve an internal engine design.

The [5 November delegated agreement](https://github.com/elsa-workflows/elsa-foundation/issues/3815#issuecomment-6669211076) settles only input/occurrence and owner-lifetime principles at exact proposal commit `036a88bf`. Bounded N9 visit-frame and corrected N10 composite-source evidence are recorded in spec1210; their historical failures and limits remain. The immediate M0 contract work is the real durable source/continuation fence and exact native control/admission protocol, coordinated with Unified Execution. Native placement, Q1–Q3, production planning and full Studio/saga qualification remain gated.

The later [scoped A1 policy decision](https://github.com/elsa-workflows/elsa-foundation/issues/3815#issuecomment-6670586138) and [direct full-delivery instruction](https://github.com/elsa-workflows/elsa-foundation/issues/3815#issuecomment-6670672472) advance that work without reducing its scope. [C2's concrete runtime mapping](../../specs/1210-state-machine/m0-runtime-mapping.md) has three clean independent source lenses and a [completed shared-source co-review](https://github.com/elsa-workflows/elsa-foundation/issues/2892#issuecomment-6670940060); the implementing agent selected it for the reviewed M0 integration work. R1 adds internal accounting and tests only. Normal CI ran all3375 Runtime cases, including all25 new cases, successfully with no skips at the reviewed head’s merge checkout; the [mapping records the precise evidence and architecture-registration failure](../../specs/1210-state-machine/m0-runtime-mapping.md). No local R1 run occurred. Current-main reconciliation retains the newer Runtime claim/coalescing/migration behavior. Physical source/target/receipt/control qualification, final native placement and the complete StateMachine/Studio/saga implementation remain outstanding.

The program completes only when the accepted first-release requirements have actual native/Studio authoring,
execution and persistence evidence, appropriate reviewed/merged changes and resulting-main qualification.
A planning PR, flat-only runtime, hierarchy alone, attractive editor or closed child issues is insufficient.
