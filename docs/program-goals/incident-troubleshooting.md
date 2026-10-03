# Workflow Incident Troubleshooting

Status: active.

Area: workflow-runtime diagnostics and Foundation Studio operator triage.

Steward(s): Sipke plus the incident-troubleshooting program lead and bounded workers.

## Purpose

Make a run with incidents visibly actionable and provide a coherent journey from run list to affected activity/input and root cause. Preserve lifecycle policy while separating incident health from execution state.

## In Scope

- Structured causal association and failed-input evidence for scheduler/start-time failures.
- Authoritative incident-health summaries/filtering with existing permission/tenant scope.
- Shared designer node cues, run list/header visibility, nested incident navigation and clear diagnostic reading.
- Rebuilt normally composed Studio/Workbench proof, regression protection and exact-head delivery gates.

## Out Of Scope

- New recovery/strategy policy or blanket workflow Faulted conversion.
- Expression-editor/IntelliSense work owned by Program #2310.
- Dashboard, authentication and authoring-toolbar redesign; performance measurement; production deployment.

## Active Objectives

1. [Program #2334](https://github.com/elsa-workflows/elsa-foundation/issues/2334) — lead, delivery and completion evidence.
2. [Epic #2335](https://github.com/elsa-workflows/elsa-foundation/issues/2335) — operator incident triage.
3. [Feature #2336](https://github.com/elsa-workflows/elsa-foundation/issues/2336) — recognize and diagnose activity incidents.

## Linked Surfaces

- [Project 54](https://github.com/orgs/elsa-workflows/projects/54): cross-repository scheduling/readiness/verification.
- [Specification](../../specs/192-incident-troubleshooting/spec.md): approved observable behavior.
- [QA assessment](../reports/incident-troubleshooting/analysis-and-plan.md): original before-state screenshots and source investigation.
- [Runtime fault behavior](../runtime-fault-behavior.md): authoritative existing intervention/lifecycle policy.

## Roadmap and Completion

Establish additive backend contracts first; deliver dependent Studio triage against them; integrate and demonstrate the real undefined-variable scenario plus controls. Keep one integration/merge lane and no more than two implementation workers. Complete only after both repositories merge with green gates and real operator acceptance passes. Issue checkpoints record ownership and evidence; Project fields mirror them.

No existing goal owns this human-facing cross-repository outcome: Runtime Execution Evidence explicitly excludes Studio UI, and expression developer experience owns authoring assistance. This bucket therefore preserves those boundaries.
