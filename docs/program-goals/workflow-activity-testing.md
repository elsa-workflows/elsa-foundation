# Workflow and activity testing

- **Status:** Active; design first, owner authorization 5 November 2026.
- **Steward:** Sipke Schoorstra; delivery Codex.
- **Program:** [#3901](https://github.com/elsa-workflows/elsa-foundation/issues/3901).
- **Canonical features:** shared testing [#3588](https://github.com/elsa-workflows/elsa-foundation/issues/3588), selected-activity execution/designer entry [#3892](https://github.com/elsa-workflows/elsa-foundation/issues/3892).
- **Specification:** [spec 1220](../../specs/1220-workflow-activity-testing/spec.md).

## Purpose and authority

Deliver reusable regression cases for workflows and explicitly selected activity occurrences through one Studio library and headless CI path, with assertions, fixtures, controlled dependencies/time, multi-step interactions and retained history.

The owner authorized a dedicated isolated delivery chat on 5 November to deliver both features together, from necessary product/architecture design with Sipke through implementation, verification, review/merge, resulting-main qualification and closeout. Keep these two issues as canonical feature records. Routine engineering choices and milestone transitions proceed within that authority. Release and production deployment are separate.

## Sequencing and boundaries

M0 reviewed product/spec/runtime contract → M1 shared saved revisions/results → M2 selected-occurrence execution/context → M3 effects/time/steps/isolation → M4 composed Studio and packaged CI → M5 complete acceptance controls → M6 review/merge/resulting-main closeout.

The program issue carries progress, staged critical path and evidence. A partial milestone never closes either feature. No second interpreter, duplicate activity suite product or browser-driven headless execution. Normal model semantics own target support; unsupported targets have explicit reasons.

Run beside existing work, displace none and preserve consolidation priority. This activates only the named testing scope and does not resume Agents #3519, ETL #2892, Runtime Efficiency #2531, StateMachine #3815 or other paused work. Existing delivered contracts may be consumed without transferring those programs' acceptance.

## Owner choices and closeout

Current chosen candidate is the default, with exact result capture and deliberate stale-target rebinding. Saved tests live primarily in Studio with versioned export/import for source control/CI. Earlier choices include workflow/activity targets, controlled and explicit integration modes, multi-step events/actions and controlled time in this delivery.

Suites use latest saved case revisions, resolved and frozen once at run admission; exports likewise retain exact revisions for CI. Running/history manifests do not change after edits.

Completion requires all feature criteria, actual current-commit composed UX and independent visual review, deliberate regression controls, real owned integration, packaged headless equivalence, cancellation/isolation proof, required exact-head checks and resulting-main qualification. Mark completed only after that evidence is on the canonical issues and PRs.
