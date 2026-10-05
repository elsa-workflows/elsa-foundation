<a id="workspace-launch-readiness"></a>

# Elsa 4 Contributor Experience

Status: active. Evolved from Workspace Launch Readiness on 6 October 2026.

Area: human contributor onboarding and review across `elsa-foundation` and `elsa-foundation-studio`.

Steward: Sipke (product owner), with the Contributor Experience control room owning delivery coordination, integration, review and verification. Repository reviewer coverage must be confirmed on scoped issues before it is promised.

Program: [Foundation #2416](https://github.com/elsa-workflows/elsa-foundation/issues/2416). Scheduling: [Project 56](https://github.com/orgs/elsa-workflows/projects/56). Requirements and decisions: [program PRD](../plans/contributor-experience-program.md).

## Purpose

A new contributor can independently run Elsa 4, find suitable work, make and validate a change, and receive a helpful review without private maintainer knowledge or an AI subscription.

This is the successor to the first-user workspace handoff effort at this same path. Existing links and Git history are retained; the former bucket is not a second active queue. Architecture-tour and source-of-truth orientation remain useful parts of the human contributor path.

## In scope

- A short human entrance and verified backend/Studio source-development journeys.
- Clear contribution/validation guidance, genuinely available starter issues, and willing reviewer coverage.
- Discoverable support, ownership and active work.
- Tested server–Studio preview pairs, repeatable setup checks, and unfamiliar-contributor trials.
- Branch housekeeping only after inventory, ownership and publishing behavior are understood.

## Out of scope

Broad constitution/architecture grooming, performance benchmark gates, silently relaxed merge gates, invented legal/support commitments, or age-only branch deletion. Runtime/product defects remain owned by their engineering program/spec and are linked here as dependencies. Personal operating preferences remain local.

## Active objectives

1. Establish the durable program and source-setup baseline.
2. Deliver the first working source change in each repository.
3. Deliver a supported first contribution, then keep the journey dependable.

The [program issue](https://github.com/elsa-workflows/elsa-foundation/issues/2416) and its native children contain executable scope, current claims and evidence. The [Project](https://github.com/orgs/elsa-workflows/projects/56) presents scheduling; this file does not duplicate the live status ledger.

## Linked surfaces and history

- [Architecture tour](../architecture-tour.md), [skill catalog](../skills/catalog.md), and [issue-tracker workflow](../agents/issue-tracker.md).
- [Earlier workspace launch readiness review](../reports/workspace-launch-readiness-review.md) and [agent maturity audit](../reports/agent-maturity-audit.md) remain historical evidence, not current contributor proof.
- [First-user prompt options](../reference/first-user-prompts.md) remain optional assistance; they are not prerequisites for human contributors.
- [Unfinished-work inventory](../reports/unfinished-work.md) and [program-goals registry](README.md).
- [Constitution Readiness](constitution-readiness.md) owns draft-section ratification; [Code Reality And Test Maturity](code-reality-and-test-maturity.md) owns broader code verification.
- Historical [First-Request / Cold-Start Readiness](first-request-cold-start-readiness.md) is superseded. Its performance measurement remains retired; applicable persistence/activation history is in [EF Core Persistence](ef-core-persistence.md).

## Drift and completion

Keep the program focused on the path from repository choice to reviewed contribution. Reuse existing canonical docs instead of growing a parallel architecture manual. Complete delivery only when the three milestone demonstrations pass, two unfamiliar contributors reach reviewed PRs, starter issues have reviewer coverage, and ongoing checks/triage have accepted owners. Then hand ongoing maintenance to those owners and close the delivery program.
