# Wave C handoff — control-room brief

Date: 2026-07-06. Written by the Wave-B/C control-room agent at Wave-C W30 completion. This is the
boot document for the next control room (W31 onward): read this, the
[Phase 4 handoff](phase-4-handoff.md) §2 (the operating protocol — still authoritative), the
[Wave B handoff](wave-b-handoff.md) §3 (Wave-A/B operating notes — still additive), and the
[bucket doc](../../program-goals/elsa-4-review-remediation.md) Phase 4 section, then take over.

## 1. Current state

- **Main tip:** `6ba085c9` (PR #499). Build 0 errors. Architecture guard **49/49**. No known failing
  tests. Main is green and clean.
- **Wave B complete** (W27/W28/W29 — PRs #484/#487/#491, closures #486/#488/#493). See the bucket doc.
- **Wave C — W30 god-class refactors COMPLETE 2026-07-06** (all three, each behavior-preserving with
  bite-proven guards, control-room QA on a detached worktree before merge):
  - **W30a FlowchartExecutionEngine (#275)** — [#495](https://github.com/elsa-workflows/elsa-foundation/pull/495),
    merge `93864a53`. Engine 920→267 lines; 7 collaborators; single-home `Sequence`/`ScheduleNode`
    rule; W22 `#382` `PruneForPersistence` moved verbatim; `elsa.flowchart.executionState` wire shape
    byte-identical (CT-1/CT-2/CT-3 determinism goldens, bite-verified).
  - **W30b WorkflowExecutableCompiler (#418)** — [#494](https://github.com/elsa-workflows/elsa-foundation/pull/494),
    merge `f756473a`. Compiler 466→91-line orchestrator + 4 DI collaborators; duplicate tree-traversal
    collapsed to one walk; 7-definition golden corpus pins `WorkflowExecutable` + `ArtifactHash`
    byte-identical (bite-verified).
  - **W30c ExtensionBuilderStorage (#421)** — [#497](https://github.com/elsa-workflows/elsa-foundation/pull/497),
    merge `418ad4db`. Façade 2,210→1,288 (net −269 LoC); 5 collaborators; dead git Stack-B + 3 dead
    methods deleted (`GIT_TERMINAL_PROMPT=0` folded into `GitClient`, direct-assertion guard
    bite-verified); single-writer `_gate` invariant preserved. Items #421-3/4/5 left OUT of scope.
- **Peer-session work landed through the control-room gate this wave** (see §3 — this is now policy):
  #485 (user-merged before the gate policy), #496 (validator, self-merged before policy — see below),
  then the **FR-033 stack, all control-room-QA'd**: #500 (Core-purity fix), #501 (maps), #498
  (null-deref), #499 (FindVersion + category sweep).

## 2. Remaining work

### Wave C tail — hand these to the incoming control room

1. **Event-delivery split (RATIFIED, not yet implemented)** — Sipke ratified 2026-07-06. Split
   `IEventPublisher` into **`IInlineEventPublisher`** (awaits handlers; results observable; the
   draft-validation gate depends on this) and **`IDeferredEventPublisher`** (fire-and-forget); **delete
   the caller-supplied `IEventPublishingStrategy` parameter**; the strategy classes + `IEventPublishingStrategy`
   go internal. This makes the merged-#500 footgun (a caller passing `EventPublishingStrategy.Background`
   → draft-gate reads errors before handlers run → invalid draft silently passes) **unrepresentable**.
   `Parallel` is not a third service (it also awaits — an internal variant of inline). Broad Events-domain
   refactor: enumerate every `IEventPublisher.Publish` caller (breaking API, pre-1.0 OK); route each to
   inline/deferred; add a spy-publisher test pinning the gate uses inline. Full design + mechanics in the
   control-room agent memory note `event-delivery-inline-deferred-split.md`. Supersedes the FR-033
   session's proposed "option (a)". Own Stage-1 design gate.
2. **W31 DRY batch** — remaining #412/#413/#414 items 3/4/6, #415 live slices, #416 slices 2–6 (**slice 3
   needs its own gate**: EXTENSION_POINTS Priority-ordering contract), #417 remainder incl. the
   Activities/Design AddVersion sibling hardening, #422 items 1–2. **Includes the cross-provider agent
   log-redaction helper** deliberately deferred from W29 (#414 items 3/4 — the shared `Normalize` helper
   the 4 redaction sites should share).
3. **W32 cleanup batch** — #423, #279 nits; remaining MD-10 registration-test gaps. Plus the Wave-C-flagged
   **flowchart `Scopes` residual O(n) growth** (persisted per-node iteration counter = wire-shape change,
   **own §E6 gate**).

### Open / unscoped
- [#473](https://github.com/elsa-workflows/elsa-foundation/issues/473) (dotted activity-type-key filter;
  likely tied to the dead execution-time JS accessors — needs scoping).
- Product track per the phase-4 handoff §3 (scoped variables Studio UI, Elsa-3 activity port, HTTP
  sync-response correlation, etc.) — user-priority-gated, not scheduled.

## 3. Operating notes learned in Wave C (additive to phase-4 §2 and wave-b §3)

- **Peer-session PRs route through the control-room QA gate** (Sipke ruled 2026-07-06). Any PR to
  elsa-foundation `main` from a peer session is adopted into the merge train and passes build + affected
  suites + **architecture guard 49/49** + diff review + a bite-proof, before merge. Peer sessions do not
  self-merge. **Why:** #485 and #496 self-merged outside the gate and put a real Core-purity violation on
  main (`Validations.Core → Events.Strategies`, guard red at 48/49) that no check caught.
- **CI runs CodeQL + Docker ONLY — no `dotnet test`.** Green PR checks do NOT mean tests pass; unit and
  architecture failures merge undetected. This is why the local QA gate is the real backstop. (Durable fix
  — arch guard in CI — was considered and deferred; it's the CI/#490 session's domain.)
- **Run the architecture guard AND the maps check in QA whenever a csproj/project-reference changes.**
  W30 units were single-project (no ref change → guard/maps not required). But #500 changed a project ref
  and merged without a maps regen (caught after, fixed via #501). Add both to the gate the moment a
  `.csproj` ref moves — same trigger as the constitution's maps rule.
- **Guards must BITE — verify by mutation, not by green.** W30c's first git-prompt test was vacuous (a
  no-TTY env never prompts, so removing `GIT_TERMINAL_PROMPT=0` left it green). The QA bite-check (mutate
  the code, confirm the intended test goes red) caught it; the fix was a direct structural assertion. Do
  this for every new guard: revert/perturb the guarded behavior and confirm the specific test fails.
- **Main is a fast-moving target with multiple sessions merging.** W30a/W30b each freshened twice.
  Prefer: QA the *combined tree* yourself (merge origin/main into a detached QA worktree) rather than
  bouncing the worker for a disjoint/trivial delta (e.g. a dependency bump or a maps-only PR). The
  `gh pr merge` produces the same combination GitHub reports MERGEABLE. Only bounce the worker for a real
  conflict or a substantive main change.
- **Batch docs closures.** Three near-simultaneous W30 landings would have raced three tiny docs PRs on
  the bucket doc. The W30 closures + FR-033 landings are batched into this handoff's docs PR instead.
- Worktrees under the **session scratchpad**, never the repo's `.claude/worktrees/`; `git stash` banned;
  foreground builds (`dotnet build Elsa.Server.slnx -maxcpucount:2`); note test projects are NOT in
  `Elsa.Server.slnx` — `dotnet test <testproj>` builds them itself (don't rely on `--no-build`).

## 4. First actions for the incoming control room

1. Verify main tip `6ba085c9` (or later) and clean state (`git fetch`, `gh pr list`, arch guard 49/49).
2. Confirm scope/order with Sipke: the ratified **event-delivery split** (design ready, memory note), then
   **W31 DRY** (parallel by domain folder, second-lander on shared surfaces), then **W32 cleanup**. The
   event-delivery unit is the natural first W31 pickup (it closes the merged-#500 footgun).
3. Enforce the peer-PR gate for any peer-session PRs that appear.
4. Track per-unit todos; QA + merge per protocol; batch the closure entry when each sub-wave lands.

## User-owned / do not touch
- `feat/studio-auth-ootb` lineage, PR #372 (per wave-b §1).
- The other active sessions' surfaces (CI/#490, NuGet-generator, Studio npm packages) — coordinate via
  session messaging; do not merge their PRs without the gate.
