# Workflow Library Organization

- **Status:** Completed; owner-authorized delivery, 31 October 2026.
- **Steward:** Sipke Schoorstra; delivered by Codex.
- **Program:** [#3499](https://github.com/elsa-workflows/elsa-foundation/issues/3499).
- **Delivery:** [#3500](https://github.com/elsa-workflows/elsa-foundation/issues/3500), [specification](../../specs/1173-workflow-library/spec.md).

## Purpose

Make large workflow libraries easy to organize and revisit through one home folder per definition, overlapping classification and private/shared saved views. The linked specification owns behavior and acceptance; this file owns coordination.

## Authorization and sequencing

The owner approved the proposal and instructed "Turn this into a Program, then deliver it end to end. Use a separate work tree." Run beside the active programs, displace none and coordinate shared Studio paths. Consolidation remains priority one. Only the workflow-organization scope adopted from historical #790 and parked #792 is active; no other backlog is reactivated. Releases and production deployment are outside scope.

## Delivered milestones

1. M0: owner authorization, current-host design and acceptance packet recorded.
2. M1: current-host EF folders, bounded browsing and atomic placement delivered.
3. M2: tags, combined filters, views, favorites and Studio organization journeys delivered.
4. M3: real-host/provider/package/browser qualification, independent review, required CI, merge/resulting-main verification and user/integrator documentation completed.

## Evidence and completion

The program issue and delivery PR carry transition/gate evidence. Older issues marked completed do not establish current host support; their Groundwork guidance is superseded by ADR 0073. Scale qualification uses deterministic bounded-work evidence under ADR 0080 D2, without reviving timing benchmarks. Mark complete only after every specification journey and delivery gate is satisfied on the resulting main.


## Completion receipt

[PR #3503](https://github.com/elsa-workflows/elsa-foundation/pull/3503) merged reviewed
head `509c386d284aa14c3b9bbd21053031bfbdaacefa` as
`c4cfa0736a2216c924092d12a3ad432c6f05e8bb`. The full source trees are identical.
The [resulting-main receipt](https://github.com/elsa-workflows/elsa-foundation/issues/3499#issuecomment-6543405642)
records successful CI, Studio, Weaver, Packages and all four CodeQL analyses,
rebuilt normal-host journeys, restart readback and immutable source-state proof.
Reviewed-head and main browser suites each passed 780 cases with six opt-in
screenshot skips; the existing #3261 quarantine and provider conformance skips
remain separately reported. The actual Workflows landing is 340,443 bytes under
the unchanged 402,000-byte budget.

All M0–M3 outcomes and specification tasks are complete. User guidance is in
[Organizing workflows](../../website/docs/studio/organizing-workflows.md).
[Legacy CSS alias retirement #3513](https://github.com/elsa-workflows/elsa-foundation/issues/3513)
is a separate pending-triage follow-up, not an active objective of this program.
Releases and production deployment remain outside its authorization.
