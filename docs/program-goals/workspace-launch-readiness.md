# Workspace Launch Readiness

Status: active.

Area: first-user handoff / repository launch preparation.

Steward(s): Joey plus active architects/agents.

## Purpose

Prepare `elsa-foundation` to receive its first new architects and engineers without relying on chat history or broad operating-model grooming.

This bucket exists to turn the Elsa foundation workspace from "still being polished" into a launchable workspace: a new user should be able to initialize the repo, take the architecture tour, understand the source-of-truth layers, find the active program-goal buckets, and know which work is unfinished, unratified, unverified, or intentionally deferred.

## In Scope

- First-user onboarding and handoff readiness.
- Architecture Tour skill readiness and the short orientation path.
- Clear next-step routing from reports into program-goal buckets.
- Launch-readiness checks for broken links, stale roadmap notes, unresolved setup assumptions, and map-refresh behavior.
- Handoff prompts/templates for large architecture workers when useful.
- A concise "where to start" path for architects who should not reread the whole constitution first.

## Out Of Scope

- Broad constitution ratification.
- Runtime execution design itself; use [Runtime Execution Seam](runtime-execution-seam.md).
- Code/test implementation work; use [Code Reality And Test Maturity](code-reality-and-test-maturity.md).
- CShells generator implementation; use [Feature Composition Readiness](feature-composition-readiness.md).
- Turning personal workflow preferences into shared doctrine.

## Active Objectives

1. Make the Architecture Tour the primary first-user orientation route.
2. Ensure `docs/program-goals/README.md` shows focused buckets for the remaining launch work.
3. Ensure reports remain evidence/inventory and planned durable work moves into a bucket before execution.
4. Identify whether a new architect can start with a constrained handoff instead of a broad prompt.
5. Keep launch readiness focused on usability for first users, not on endlessly polishing the operating model.

## Linked Surfaces

- [Architecture tour](../architecture-tour.md)
- [Skills catalog](../skills/catalog.md)
- [Agent maturity audit](../reports/agent-maturity-audit.md)
- [Workspace launch readiness review](../reports/workspace-launch-readiness-review.md)
- [First-user prompt options](../reference/first-user-prompts.md)
- [Unfinished work](../reports/unfinished-work.md)
- [Program goals index](README.md)
- [AGENTS.md](../../AGENTS.md)
- Dependency: [First-Request / Cold-Start Readiness](first-request-cold-start-readiness.md) — a launchable workspace must also start fast; host boot / first-request latency is tracked in its own bucket, not here.

## Current Roadmap Notes

- Treat this as the launch/handoff bucket for first users.
- The next hard architecture unit should live in its own bucket, not here.
- If an onboarding gap is discovered while preparing a worker handoff, fix the onboarding surface only when it prevents the handoff from being reliable.
- When a handoff invokes maps and the manifest is dirty or freshness is uncertain, refresh the relevant map first and review generated findings before continuing.
- The constitutions are ratified at the document level (v4.0.0, 2026-08-08); warn users about the section-level gates still marked draft/provisional (framework §2.24, Elsa §E2.9) when that affects their task, and route ratification-focused work through [Constitution Readiness](constitution-readiness.md).
- Do not continue broad foundation-workspace polishing. If the workspace can route the user, redirect to the focused hard-work bucket.

## Drift / Review Notes

- This bucket is successful when new users can start work without broad context archaeology.
- If this becomes another meta-polishing loop, redirect to a hard program bucket such as Runtime Execution Seam or Code Reality And Test Maturity.

## Removal or Completion Conditions

Complete or pause this bucket when the first-user handoff path has been verified, the first architect can start from a named bucket, and remaining work is tracked in focused program-goal buckets rather than in the broad Elsa Foundation Operating Model bucket.
