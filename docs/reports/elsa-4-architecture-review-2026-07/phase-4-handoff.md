# Phase 4 handoff — control-room brief and prioritized proposal

Date: 2026-07-04. Written by the outgoing control-room agent at the completion of the
2026-07 architecture-review remediation roadmap (W1–W21, all merged). This document is the
complete handoff for a fresh control-room agent: current state, operating protocol, and the
prioritized Phase 4 proposal awaiting user approval.

## 1. Current state

- **Main tip:** `b69f6f7c` (Phase 3 closure, PR #468). Build 0 errors. **No known failing tests.**
- **All roadmap units W1–W21 merged.** Phase entries + follow-ups:
  [bucket doc](../../program-goals/elsa-4-review-remediation.md). Canonical briefs: [roadmap.md](roadmap.md).
- **Merged this program (Phase 3):** #461 (W18 security), #462 (W21 modularity), #463 (W17
  publishing, closes #397/#398), #464 (W19 observability, closes #393), #465+#466 (W16
  activities), #467 (W20 distributed actors), #468 (closure).
- **Test baselines at `b69f6f7c`** (suite → count): Runtime 679, Activities.Runtime 164,
  Architecture 47, Modularity 108, Groundwork 167, Runtime.Scheduling 57, Runtime.Distributed 31,
  Runtime.Tracing 5, FastEndpoints 12, Secrets 49, Identity 31, Identity.Groundwork 14, Agent 125,
  OTel 49, Publishing.Api 57, Design.Api 8, Design 262, Activities.Design 151, Activities.Http 20,
  Activities.Scheduling 19, Activities.Scripting 6, Mediator 10, Caching 10, Expressions 45.
- **Pending user ratifications:** (a) MD-5 minimum-project-size amendment
  ([report](elsa-4-w21-md5-minimum-project-size-amendment.md)); (b) **ADR 0033**
  ([Runtime.Core contracts-vs-engine split](../../adr/0033-runtime-core-splits-contracts-from-engine.md),
  Status: proposed). Outgoing recommendation: accept ADR 0033.
- **User-owned, do not touch:** PR #372; untracked `docs/plans/studio-bearer-token-issuance.md`
  in the user's checkout (user/other-session authored, relates to Studio issue #208).

## 2. Operating protocol (proven over W1–W21 — keep it)

- **Control room does not implement** (exception: mechanical docs-only closure PRs). Work is
  delegated to worker sessions created with **model `claude-opus-4.8`, reasoning_effort `high`,
  mode `autopilot`, notify_on_idle `always`** (default frontier model banned for implementation —
  cost). One unit per session/branch/PR.
- **Two-stage gate:** worker investigates → messages a Stage-1 plan → control room approves with
  explicit rulings → worker implements. Workers must STOP at the gate. Rulings are authoritative;
  restate them briefly when messages cross (idle-notification echoes are frequent — never act on
  an echo alone).
- **QA before every merge (no CI beyond CodeQL — local reproduction is mandatory):**
  `gh pr view N --json baseRefName,isDraft,mergeable` → verify base=main →
  `git merge-base --is-ancestor origin/main <head>` (bounce to worker if behind/conflicting;
  second-lander rule: merge main, resolve shared surfaces additively, regen maps on merged tree,
  re-run suites) → diff review against the approved plan → `git worktree add /tmp/qa-X <sha>
  --detach` → build + affected suites → for security/runtime changes, source-inspect each fix
  site; for bug fixes, independently re-prove failing-first by reverting the src fix in the QA
  worktree → `gh pr ready N` + `gh pr merge N --merge --delete-branch` → clean worktree →
  ff-only pull main → notify worker, log, next.
- **Git model B:** feature branches, draft PRs to main (`--base main` explicit — worker PRs have
  defaulted to wrong bases before), merge commits, trailer
  `Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>` on every commit.
  Never mutate the user's checkout beyond ff-only pulls of main.
- **Build flake:** default `-m` MSBuild occasionally crashes a node (MSB4166);
  `dotnet build Elsa.Server.slnx -maxcpucount:2` is clean. Tell every worker.
- **Shared-surface churn** (slnx additive tail, `EXTENSION_POINTS.md`, `docs/maps/*`, bucket doc):
  second-lander resolves. Maps-only conflicts may be resolved by the control room directly
  (take main's, regen on merged tree). `gh pr merge --delete-branch` fails on the LOCAL branch
  when a copilot worktree holds it — harmless; verify with `gh pr view N --json state`.
- **Wire-safety discipline:** persisted identifiers (doc kinds, HandlerName literals, enum
  members, member keys) are frozen — constitution §E6. New persisted kinds need wire-safe names +
  golden fixtures + drift tests. Watch for this in every Stage-1 review.

## 3. Phase 4 proposal (prioritized, awaiting user approval)

Sources: 50-issue audit (tracking #424, disposition in
[audit-issue-reconciliation.md](audit-issue-reconciliation.md)), Phase 0–3 follow-up findings in
the bucket doc. ~48 issues open. Proposed structure: **correctness first (parallel wave), then the
ratified split, then debt/refactors, product track per user priority.**

### Wave A — correctness bugs (parallel, after solo-merge of any ratified split — see ordering note)

| Unit | Scope (issues) | Size |
|---|---|---|
| **W22 Runtime & flowchart correctness** | #381 Do/While child validation, #382 FlowchartExecutionState O(n²) growth, #383 OutputArgument widening, #386 outbox misclassification, #399 StateId validation gap; scope-check #412/#413 overlap | M–L |
| **W23 Serialization & expressions correctness** | #407 Jint ICollection cast crash, #408 digit-leading identifiers, #409 $ref/$id dead code, relevant #422 slices | M |
| **W24 HTTP & files correctness** | #388 exhausted-stream XML, #389 ZipFileArchive dispose order, #390 cache stream position, fold #416 leak slices | M |
| **W25 Persistence & EFCore hygiene** | #394 static semaphore, #395 DbContext leak, #396 ChangeToken TOCTOU, #403 prune retry parity, #404 version-publish race, fold #415/#417 slices | L |
| **W26 API & design correctness** | #384 PageArgs, #387 dead exception filter, #391 swallowed cancellation, #392 TenantAgnostic ignored, #411 sync-over-async + SSE loss, fold #414 auth-guard dedup | M |

All Wave-A units: failing-test-first per bug, one concern per commit, tech-debt folds only where
the same files are already open (else leave for Wave C). #410 (container-scope lost update) and
#273 (dispatcher failure policy) need scoping/human input — Stage-1 gates decide.

### Wave B — structural (solo, sequenced)

| Unit | Scope | Size |
|---|---|---|
| **W27 Groundwork durable placement + transport stores** | W20 follow-up; mechanical drop-in against frozen leaf contracts + `executionCommandTransport` v1 fixture; makes the distributed provider production-usable | M |
| **W28 ADR 0033 execution** (only if ratified) | Split Runtime.Core `Services/`+`Resolvers/`+composition root into sibling `Elsa.Workflows.Runtime`; behavior-preserving; per the ADR's consumer analysis | L–XL |
| **W29 Security/design follow-ups** | Design-endpoints AllowAnonymous removal (config-gated), ISecretManager store-vs-resolver split (proposal in #461 body), Secrets fixture gate | M |

**Ordering note:** W28 moves ~10k LoC across a project boundary and will conflict with anything
touching Runtime.Core services. If ADR 0033 is ratified, run W28 **solo before Wave A's W22**
(which touches runtime services) or after Wave A completes — do not run them concurrently.
Outgoing recommendation: Wave A first (small surgical diffs, immediate user value), then W28.

### Wave C — debt & refactors (after correctness)

- **W30 God-class refactors:** #275 FlowchartExecutionEngine (795 lines), #418
  WorkflowExecutableCompiler, #421 ExtensionBuilderStorage. Each needs a Stage-1 design gate.
- **W31 DRY batch:** #272 pipeline builders, #274 scheduler work-handler base (respects #412),
  #419 mediator/events, #420 diagnostics duplication.
- **W32 Cleanup batch:** #423, #279 nits; remaining MD-10 registration-test gaps (18, see
  [gap report](elsa-4-w21-md10-feature-registration-test-gap.md)).

### Product track (user priorities required — not scheduled)

Scoped variables (#205, #213, #225, #285), server-side execution output (#254), Elsa-3 activity
port Section 1 (#255), HTTP synchronous response correlation + remaining W16 follow-ups (DS-8
execution, full DS-9, email/messaging, legacy JS stub retirement, demo-shell composition), Studio
bearer-token issuance (user's `docs/plans/` draft, #208). Import bug #378 is small and
user-facing — candidate to fold into W22 or ship as a hotfix-style solo unit.

## 4. First actions for the incoming control room

1. Read this file, the [bucket doc](../../program-goals/elsa-4-review-remediation.md), and
   [roadmap.md](roadmap.md) §protocol. Verify main tip and clean baselines.
2. Ask the user: (a) ratify MD-5 and ADR 0033? (b) approve the Phase 4 wave structure and
   Wave-A-first ordering? (c) any product-track priorities to pull forward?
3. Launch approved Wave-A units as parallel worker sessions per §2 (ownership boundaries by
   domain folder; second-lander rule on shared surfaces).
4. Track per-unit todos; QA + merge per protocol; write the closure entry when the wave lands.
