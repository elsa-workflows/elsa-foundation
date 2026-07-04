# Audit Issue Reconciliation — 2026-07 architecture review

Status: complete (disposition pass). Verified against `origin/main` tip **`87dfa9ca`** in a single pass.

This report reconciles the 50 automated-audit GitHub issues **#374–#423** (tracking issue
[#424](https://github.com/elsa-workflows/elsa-foundation/issues/424)) against the architecture-review
remediation work (W-roadmap) that merged after the audit was filed. It belongs to the
[Elsa 4 Architecture Review Remediation](../../program-goals/elsa-4-review-remediation.md) program goal;
per-unit scope lives in the [roadmap briefs](roadmap.md).

> **Verified-at SHA:** `87dfa9ca`. W14 (naming) and a Groundwork package bump are in flight and may
> rename symbols after this pass — re-verify file:line before acting on any row.

## Why two tracks existed

Two independent quality efforts ran in parallel:

1. **The W-roadmap fleet** (this folder's `roadmap.md`): Phase 0 (W1–W6), Phase 1 (W7–W9), Phase 2
   (W10–W13, W15) all merged; W14 naming in flight.
2. **The automated audit** (2026-07-03, #424): 50 issues filed *before* most of the W-fleet merged,
   with its own tier plan. Overlap was expected and is resolved here.

## Headline findings

- **All 5 Tier-0 security issues remain open.** #374, #375, #376, #377, #406 are unchanged in current
  code — no merged PR touched them. This is the top priority of the Phase-3 folding proposal below.
- **#379 is a runtime *correctness* bug in merged Move-2 code, not tech debt.** The parent-completion
  scheduler handler never propagates a handler-thrown fault to the grandparent, unlike both sibling
  handlers — a 3+ level fork/join can hang permanently. Recommended for a standalone hotfix unit.
- **W13's DRY sweep made two bugs trivial but did not fix them.** #381 and #399 now have the exact
  primitive needed (`IsBody` on the shared navigators; `ValidateStateIdMatches<TState>`) but the call
  sites were never added — each is now a one-liner.
- **#380 was resolved by deletion, not a patch.** W12 removed the ambient service-locator mechanism
  entirely; the described divergence cannot occur anymore.

## Method

For each issue the claimed defect was verified against the current source at `87dfa9ca` (not PR
descriptions). Merged PRs referenced: **#442** (W10 mediator), **#443** (W11 hot-paths), **#449**
(W13 DRY), **#450** (W12 runtime structure), **#441** (W15 tests); plus ADR-0030 execution-carrier
PRs #445/#446/#448/#453, #440 identity-via-durable-values, and hotfix #454.

Dispositions: **FIXED** (defect gone; closed) · **PARTIALLY FIXED** (some sub-items done; kept open)
· **STILL OPEN** (defect verified present) · **INVALID/STALE** (claim no longer holds).

## Tally

| Disposition | Count | Issues |
|---|---|---|
| FIXED (closed) | 5 | #380, #400, #401, #402, #405 |
| PARTIALLY FIXED (open) | 5 | #413, #414, #415, #419, #423 |
| STILL OPEN | 40 | #374–#379, #381–#399 (excl. fixed/partial), #403–#412, #416–#422, #406 |
| INVALID/STALE | 0 | — |

## Disposition table

| Issue | Area | Disposition | Evidence (verified @ `87dfa9ca`) | Recommended action |
|---|---|---|---|---|
| #374 | Security / Agent | STILL OPEN | `AgentEndpointActor.cs:7-23` anonymous/default fallback unchanged | **Phase 3 W18 candidate**; ready-for-agent |
| #375 | Security / Expressions | STILL OPEN | `ConfigurationAccessFunctionPreProcessor.cs:25` exact-match/case bypass unchanged | **Phase 3 W18 candidate**; ready-for-agent |
| #376 | Security / Diagnostics | STILL OPEN | `OpenTelemetryRedactor.cs:19-26` `with` never assigns `Traces` | **Phase 3 W18 candidate**; ready-for-agent |
| #377 | Security / Elsa.Server | STILL OPEN | `ElsaExtensionBuilderApi.cs` + `ElsaModuleManagementApi.cs` mgmt API-key auth duplicated verbatim | **Phase 3 W18 candidate** (fold w/ #421); ready-for-agent |
| #378 | Elsa3 import | STILL OPEN | `Elsa3ActivityToState.cs:71` inverted `||` guard | ready-for-agent (1-char + test) |
| #379 | Runtime (Move-2) | STILL OPEN ⚠️ | `WorkflowParentActivityCompletionSchedulerWorkHandler.cs:266-278` never calls `ChildFaultParentEvaluation` (comment only l.731); siblings do (Invoke:499, Resume:503) | **Standalone hotfix candidate** (hang risk); ready-for-agent |
| #380 | Runtime | **FIXED** | W12 #450 commit `7a4dd784` deleted `IWorkflowExecutionAmbientServicesAccessor`; handler uniform | Closed (superseded, no patch) |
| #381 | ControlFlow | STILL OPEN | `Do.cs:103-109`/`While.cs:97-100` never call `navigator.IsBody`; W13 added `IsBody` | ready-for-agent (one-liner via W13 primitive) |
| #382 | Flowchart perf | STILL OPEN | `FlowchartExecutionEngine.cs` paths/arrivals/scopes/diagnostics never pruned | ready-for-agent (larger; runtime perf) |
| #383 | Primitives binding | STILL OPEN | `ActivityArgumentBinder.cs:83-96` widening only walks `InputArgument<>`, not `OutputArgument<>` | ready-for-agent |
| #384 | Primitives | STILL OPEN | `PageArgs.cs:43` silent fallback; `:59` no `Limit==0` guard | ready-for-agent |
| #385 | Reconciliation | STILL OPEN | `ClrAssemblyScanner.cs:149` IsActivityType lacks generic-def exclusion; `:138` `FullName!` | ready-for-agent (also #417) |
| #386 | Runtime | STILL OPEN | `InMemoryRuntimeCheckpointCommitStore.cs:92-94` catch-all rewraps outbox `InvalidOperationException` | ready-for-agent |
| #387 | Runtime API | STILL OPEN | `Execute.cs:42` dead `when (ParamName=="artifactId")` → 500 not 400 | ready-for-agent |
| #388 | Http | STILL OPEN | `XmlHttpContentParser.cs:22-30` deserializes exhausted reader | ready-for-agent (also #416) |
| #389 | Http | STILL OPEN | `ZipFileArchive.cs:10-13` `onCleanup` before `Stream.Dispose` | ready-for-agent (also #416) |
| #390 | Http | STILL OPEN | `FileSystemZipFileCacheStorage.cs:44` no `Position=0`; `:58` `File.OpenWrite` no truncate | ready-for-agent (also #416) |
| #391 | Agent | STILL OPEN | `DefaultAgentServices.cs:401` `catch(Exception)` swallows incl. cancellation, no logging | ready-for-agent |
| #392 | Design API | STILL OPEN | `ListDefinitionsRequestHandler.cs` `TenantAgnostic` never forwarded | ready-for-agent |
| #393 | API base classes | STILL OPEN | `ElsaRequestHandlerEndpoint.cs`/`ElsaCommandHandlerEndpoint.cs` not-found→500 (no 404 branch) | ready-for-agent (W19 error-contract orbit) |
| #394 | Persistence perf | STILL OPEN | `EFCoreSaveCommand.cs:15` process-wide static `SemaphoreSlim` per type | ready-for-agent |
| #395 | Persistence | STILL OPEN | `RunMigrationsStartupTask.cs:27` DbContext never disposed | ready-for-agent (also #415) |
| #396 | Caching | STILL OPEN | `ChangeTokenSignalInvoker.cs` unbounded growth + TOCTOU | ready-for-agent (also #422) |
| #397 | Publishing | STILL OPEN | `WorkflowExecutableCompiler.cs:46` source resolved before `try` → unwrapped exception | ready-for-agent (W17; #418) |
| #398 | Publishing | STILL OPEN | `InMemoryWorkflowTestRunStore.cs` no expiry | ready-for-agent (W17) |
| #399 | Runtime | STILL OPEN | `RuntimeCheckpointCommit.cs:34-39` no `ValidateStateIdMatches` for `activityExecutions` | ready-for-agent (one-liner via W13 `ValidateStateIdMatches<TState>`) |
| #400 | Mediator | **FIXED** | W10 #442; closed-generic dispatch (`HandlerInvokerMiddleware`/`CompiledHandlerInvoker.cs:31`) | Closed |
| #401 | Mediator perf | **FIXED** | W10 #442; `GetServices(closedGeneric)` + `TryAddEnumerable` | Closed |
| #402 | Events | **FIXED** | W11 #443; `BackgroundEventPublisher.cs` host-lifetime token only | Closed |
| #403 | Diagnostics | STILL OPEN | `EfCoreStructuredLogStore.cs:236` prune no-retry (counter reset first); `:262` Dispose no idempotency guard | ready-for-agent (W19; #420) |
| #404 | Design | STILL OPEN | `AddVersionCommandHandler.cs:24-28` no lock/existence check | ready-for-agent (W17) |
| #405 | Tasks | **FIXED** | W11 #443; `TaskManager.cs` awaits+logs; `TaskStateManager.cs:50,150` `StopAsync` wired | Closed |
| #406 | Security / Identity | STILL OPEN | `AspNetCoreIdentityPrincipalFactory.cs:56-69` never expands role→permission claims | **Phase 3 W18 candidate**; ready-for-agent |
| #407 | Expressions | STILL OPEN | `JintEvaluationContext.cs:95` `(ICollection)value` crashes for `HashSet<T>` | ready-for-agent |
| #408 | Expressions | STILL OPEN | `VariableNameValidator.cs:7` accepts digit-leading names | ready-for-agent |
| #409 | Serialization | STILL OPEN | `PolymorphicObjectConverter.cs:103,197,370` `CrossScopedReferenceHandler` never assigned (only `ReferenceHandler.Preserve`) | ready-for-agent (wire or delete) |
| #410 | Runtime concurrency | STILL OPEN | `RuntimeContainerScopeService.cs:215-223` read→diff→save, no optimistic concurrency | needs-triage (store-contract/lock decision; ADR 0030 orbit) |
| #411 | Diagnostics | STILL OPEN | `EfCoreStructuredLogStore.cs:95,119,141` sync `CreateDbContext`/`ToList` in async endpoints; SSE reconnect gap | ready-for-agent (W19; #420) |
| #412 | Runtime DRY | STILL OPEN | scheduler-handler dedup (RT-9) deferred until after W12; still duplicated | needs-triage → W13 residual |
| #413 | Flowchart DRY | PARTIALLY FIXED | W13 #449 collapsed 7 Navigators (item 2); `IFlowchartPolicy` dup (1) + `Parallel.CountBranchesAsync` (3) remain | keep open |
| #414 | Agent DRY | PARTIALLY FIXED | W13 #449 added `AgentProposalAuthorization.cs` (item 2); items 1,3,4,5 remain | keep open |
| #415 | Persistence DRY | PARTIALLY FIXED | W13 `GroundworkDocumentStore` base (item 3); W12 collapsed commit-store ctors (item 5); items 1,2,4 remain | keep open |
| #416 | Http DRY | STILL OPEN | not touched | needs-triage → new DRY unit |
| #417 | Design DRY | STILL OPEN | not touched | needs-triage → new DRY unit |
| #418 | Publishing DRY | STILL OPEN | not touched | needs-triage → W17 |
| #419 | Mediator/Events DRY | PARTIALLY FIXED | W10 #442 unified builders + removed dead contexts + shared dispatch (3,4,5); ordering (1) + `ParallelProcessingStrategy` multi-exception (2) remain | keep open |
| #420 | Diagnostics DRY | STILL OPEN | not touched | needs-triage → W19 |
| #421 | Elsa.Server DRY | STILL OPEN | not touched | needs-triage → W21 (with #377) |
| #422 | Expressions/Caching DRY | STILL OPEN | not touched | needs-triage → W21/W22 |
| #423 | Cleanup nits | PARTIALLY FIXED | W13 removed dead `ExpressionDescriptor` (DS-3) + resolved ArgumentValue/State naming (DS-4); Primitives nits remain | keep open |

## Phase-3 folding proposal

### 0. Security hardening — TOP PRIORITY (all 5 Tier-0 still open)

None of the Tier-0 security findings were touched by any merged remediation. Recommend a dedicated
security-hardening unit (W18-adjacent or a new W18-SEC), not opportunistic fixes:

- **#374** cross-tenant/actor session access (fail-closed on missing claims).
- **#375** `getConfiguration` DisallowedSections child-key/casing bypass.
- **#376** OpenTelemetry trace-name redaction bypass.
- **#377** duplicated management API-key auth (extract shared filter; fold with #421).
- **#406** role-granted permissions never resolved into claims (identity → natural W18 fit).

### 1. Fold into existing planned units

- **W17 Publishing completion:** #397 (exception unwrap), #398 (test-run expiry), #404 (version publish
  race), #418 (compiler god class).
- **W19 Self-observability / error contract:** #393 (404-vs-500 ProblemDetails), #403 + #411 + #420
  (StructuredLogs/OTel resiliency & async reads).
- **W13 DRY residual (or a W13-continuation unit):** #412, #416, #417, #421, #422, plus the residual
  sub-items of #413/#414/#415/#419/#423.
- **W1 fault-semantics follow-up (or standalone hotfix):** **#379** (grandparent fault propagation —
  correctness/hang, pull forward).

### 2. New / unmapped runtime correctness & perf

- **#382** Flowchart state pruning (O(n²) loop growth).
- **#386** checkpoint-store exception misclassification.
- **#399** missing `ActivityExecutions` StateId validation (one-liner via W13 primitive).
- **#410** container-scope lost-update race (needs store-contract/lock decision).

### 3. Standalone ready-for-agent quick wins (no unit needed)

#378, #381, #383, #384, #385, #387, #388, #389, #390, #391, #392, #394, #395, #396, #407, #408, #409 —
each a well-scoped single-file fix; several already have the required primitive in place from W13.

## Notes for future readers

- **#380** has no patch to find — the ambient-scope pathway was removed wholesale by W12 (#450,
  commit `7a4dd784`). Handlers now share transactions through W12's explicit model.
- **#381 / #399** are one-liners because W13 (#449) already introduced the primitive each needs
  (`IsBody` on the shared navigators; `ValidateStateIdMatches<TState>`), but never wired the call site.
