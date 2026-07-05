# Wave B handoff — control-room brief

Date: 2026-07-05. Written by the Wave-A control-room agent at Wave A completion. This is the
boot document for the Wave-B control room: read this, the
[Phase 4 handoff](phase-4-handoff.md) §2 (the operating protocol — still authoritative), and
the [bucket doc](../../program-goals/elsa-4-review-remediation.md) Phase 4 section, then take
over.

## 1. Current state

- **Main tip:** `e2402758` (Wave A closure, PR #482). Build 0 errors, no known failing tests.
- **Wave A complete 2026-07-05** — 22 issues closed across PRs #474, #476, #477, #478, #479,
  #480, #481. Full unit→PR→issue mapping in the bucket doc.
- **Ratified at kickoff (PR #470):** MD-5 as framework §2.16.1 (framework v3.1.0, Elsa v3.3.0
  cascade); ADR 0033 accepted (execution = W28).
- **#254 closed** at Seam R1 (PR #477). R2 (synchronous execute-and-return) is routed into the
  W16 "HTTP synchronous response correlation" co-design — do NOT build a bespoke await. R3 =
  [elsa-foundation-studio#218](https://github.com/elsa-workflows/elsa-foundation-studio/issues/218).
- **User-owned, do not touch:** anything on `feat/studio-auth-ootb` lineage (user's Studio auth
  work, PR #475 merged; more may follow), PR #372.

## 2. Wave B (approved 2026-07-04, sequenced — run ONE unit at a time)

1. **W27 Groundwork durable placement + transport stores** (M). W20 follow-up: durable
   Groundwork stores for the frozen leaf contracts `IExecutionPlacementStore` +
   `IExecutionCommandTransport`, against the committed `executionCommandTransport` v1 golden
   fixture (drift-test-protected). Was launched Stage-1 and killed by a host restart before
   reporting — no work existed yet; relaunch from scratch. Stage-1 must verify the bucket doc's
   "mechanical drop-in" claim and propose wire-safe names + fixtures for any NEW persisted kind
   (§E6). Follow the Groundwork store patterns: `GroundworkDocumentStore` base (W13), W18
   identity store, W8 durable-timer store. Check how W5 fencing and W16 `TryAdvanceAsync`
   implement CAS before inventing lease semantics.
2. **W28 ADR 0033 execution** (L–XL, SOLO — nothing else may touch Runtime.Core concurrently).
   Split per [ADR 0033](../../adr/0033-runtime-core-splits-contracts-from-engine.md): `Services/`
   + `Resolvers/` + composition root (renamed `AddWorkflowRuntimeCore` → `AddWorkflowRuntime`)
   move to new `Elsa.Workflows.Runtime`; contracts/models/pipeline-contract stay in `.Core`
   (NuGet identity preserved); 5 engine-hosting projects gain the new reference; ship the
   semantic guard test (engine-shaped types banned from `.Core`). **Stage-1 must include the
   `Models/` domain-decisive audit** (6,246 LoC — the ADR defers whether any of it moves) so the
   user can rule on that boundary before code moves. Behavior-preserving; §E6 untouched by
   design. Expect slnx + maps + EXTENSION_POINTS churn.
3. **W29 security/design follow-ups** (M): design-endpoints `AllowAnonymous` removal
   (config-gated), `ISecretManager` store-vs-resolver split (proposal in #461 body), Secrets
   golden-fixture gate, **plus #414 item 7** (agent provider logs raw exception at Debug —
   possible API-key leak; the client path redacts via `Normalize`).

Wave C after: W30 god-classes (#275, #418, #421 — each needs a design gate), W31 DRY batch
(remaining #412/#413/#414-3/4/6/#415-live/#416-2..6/#417/#422 slices — dispositions recorded on
the issues and in the bucket doc; #416 slice 3 needs its own gate: EXTENSION_POINTS Priority
contract), W32 cleanup (+ flowchart `Scopes` residual growth — wire-shape change, own gate).
Also open: [#473](https://github.com/elsa-workflows/elsa-foundation/issues/473) (dotted
activity-type-key filter; likely tied to the dead execution-time JS accessors — needs scoping).

## 3. Operating notes learned in Wave A (additive to the Phase 4 handoff §2)

- **Workers = background agents** (Agent tool, model `opus`, `run_in_background`, continued via
  SendMessage across the Stage-1 gate). Worker worktrees live under the **session scratchpad**
  — the user rejected worker worktrees under the repo's `.claude/worktrees/`. Never operate in
  `/Users/sipke/Projects/Elsa/elsa-foundation` itself (one worker did; it was caught and
  relocated).
- **Host restarts kill in-flight background agents and armed monitors.** This killed workers
  five times in Wave A. Mitigations that worked: (a) instruct workers to run builds/tests
  FOREGROUND and never park on background-monitor notifications; (b) on silence >15–20 min,
  audit worktree file mtimes + running processes and nudge via SendMessage; (c) everything
  durable goes to a pushed branch early. Resume via SendMessage works across restarts — worker
  transcripts survive.
- **`git stash` is banned for workers** — the stash list is shared repo-wide across worktrees.
  WIP commits instead.
- **QA red-proof nuance:** where a fix changes a signature/interface, reverting src makes tests
  fail to *compile* — accept the compile-pin as the red signature (record it). Where a change
  is a behavior-preserving removal, its characterization tests staying green on revert is the
  *correct* outcome, not a QA failure.
- **Merge train:** serialize landings; after each merge the next PR freshens (second-lander).
  Pre-brief both sides of any known same-file collision (W25/W26 `EfCoreStructuredLogStore`
  resolved additively exactly as pre-briefed).
- **Maps rule bites:** a new project *reference* (not just a new project) requires
  `bash tools/maps/generate-maps.sh` — W25 missed it; QA caught it. Check `docs/maps/` is in
  the diff whenever csproj references change.
- Architecture guard suite lives at `tests/Elsa/Architecture` (no `/Tests` suffix). Run it in QA
  whenever a dependency edge changes.

## 4. First actions for the incoming control room

1. Verify main tip and clean state (`git fetch`, `gh pr list`).
2. Relaunch **W27** as a Stage-1-gated worker (brief in §2.1 above).
3. On W27 landing, launch **W28** solo with the Models/-audit Stage-1; take that audit to the
   user for a ruling before approving Stage 2.
4. Keep the bucket doc updated per landing; close issues with PR references at each merge.
