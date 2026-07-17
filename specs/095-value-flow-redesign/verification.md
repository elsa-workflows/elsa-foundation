# Verification: Role-Owned Workflow Value Flow

**Verified:** 2026-07-17
**Scope:** spec 095 production code, tests, serialized goldens, migration notes, and the one-way
Elsa 3 importer boundary. Generated-map refresh was intentionally skipped for this final pass.

The constitution remains draft/provisional. This verification therefore proves conformance to the
ratified work-unit contracts and tests for spec 095; it does not present the draft constitution as a
final governance decision.

## Functional-requirement audit

Every requirement in the listed range was checked against the cited implementation and passing
successor evidence. A range groups requirements that share one inseparable contract boundary; it is
not a partial audit.

| Requirements | Result | Evidence |
|---|---|---|
| FR-001–FR-006 | Pass | Canonical role-owned binding records, compiler goldens, code-first/dynamic conformance, and artifact scans distinguish requests, inputs, results, variables, state, and triggers while lowering all authoring forms to one executable. |
| FR-007–FR-009 | Pass | Typed workflow request/result builder contracts, request pinning, terminal-result validation, and deterministic compiler/fingerprint tests. |
| FR-010–FR-013 | Pass | Plain `[ActivityInput]` members, one atomic typed result, read-only projections, stable keys/fingerprints, CLR rename compatibility, and scanner/contract tests. |
| FR-014–FR-018 | Pass | `IActivityActivator`, constructor injection, reduced typed execution context, pinned defaults, reflection/generated/manual metadata conformance, activation requirements, and 218 Activities Design tests. |
| FR-019–FR-026 | Pass | Invocation/snapshot/attempt/completion records, retry/resume identity, normalized faults, durability-policy rejection, atomic checkpoint tests, distributed recovery, and 246 Groundwork tests. |
| FR-027–FR-031 | Pass | Root/container/iteration `VariableFrameState`, materialization-time reads, intrinsic `Set`, immutable expression inputs, and concurrent-write publication diagnostics. |
| FR-032–FR-036 | Pass | Structural/causal result resolution, explicit scope returns and back-edge state, unavailable/ambiguous producer diagnostics, and deterministic branch/iteration ordering across randomized completion. |
| FR-037–FR-040 | Pass | Portable expression definitions, immutable declared parameters, JavaScript/Liquid binding-path prohibition tests, deleted ambient evaluator/context/handler, raw Jint engine/configurator, delegate-host-function, and configuration/preprocessor seams, zero-reference architecture guard, and typed isolated script activity results. |
| FR-041–FR-044 | Pass | Typed stateful activities, immutable private state, fresh resume activations, durable trigger deduplication, distinct complete/suspend/fault/cancel transitions, and return-valued structural continuation decisions with no mutable completion/outcome context channel. |
| FR-045–FR-052 | Pass | IDE-guided `WorkflowDefinition<TRequest,TResult>`, build-once determinism, `.From`/`.Value`, generated named methods and call handles, typed child-workflow authoring, and ordinary builder extensions. |
| FR-053–FR-054 | Pass | Owner-level persistence/sensitivity/redaction policy, source-policy propagation, conflict rules, and external-payload rewrite tests that prevent relabeling data without enforcing the stronger policy. |
| FR-055–FR-056 | Pass | Importer-local memory-reference graph, valid lowering matrix, output-only/combined handling, and path-specific `VF-IMP-*` diagnostics. |
| FR-057–FR-060 | Pass | Deleted canonical memory/argument/factory APIs, no compatibility adapter, alias-only type metadata, completed migration ledger, and architecture zero-reference guards. |
| FR-061 | Pass | Typed trigger start/resume authority and provider-recognition fixtures, HTTP start/resume integration, and resumption tests. |
| FR-062–FR-065 | Pass | Retained three-strategy benchmark, complete workload/counter matrix, intrinsic-zero-activation semantic tests, and ADR 0045 selection of one child scope per CLR attempt. |

## Success-criterion audit

| Criterion | Result | Evidence |
|---|---|---|
| SC-001 | Pass | Generated, fluent, and dynamic canonical conformance/golden tests. |
| SC-002 | Pass | `ValueFlowArchitectureTests` and source/public/internal metadata scans report zero canonical legacy-memory references. |
| SC-003 | Pass | Retry, typed suspension/resume, worker restart, and post-completion recovery retain invocation/snapshot/result identity without reactivation. |
| SC-004 | Pass | Value-flow validator invalid-fixture matrix rejects unavailable, ambiguous, cyclic, out-of-scope, and concurrent-write cases. |
| SC-005 | Pass | Deterministic collection fixtures produce stable branch/iteration order across 128 randomized runs. |
| SC-006 | Pass | Portable JavaScript/Liquid binding tests reject delegates, undeclared/ambient reads, mutation, and nondeterministic host access. |
| SC-007 | Pass | Elsa3.Mapping valid/invalid matrix passes 30/30 and emits no canonical memory artifact. |
| SC-008 | Pass | Stable-key CLR rename/contract compatibility tests accept compatible changes and reject incompatible contracts. |
| SC-009 | Pass | Durable input/state/result policy tests reject nonpersistable values before user code. |
| SC-010 | Pass | The retained benchmark report covers all strategies, workloads, metrics, environment, and correctness gates. |
| SC-011 | Pass | Intrinsic semantic counters assert zero CLR activations and zero child scopes. |
| SC-012 | Pass | Architecture suite passes 97/97 with no new Runtime-to-Design direct references. |
| SC-013 | Pass | Generator, reflection scanner, and manual contract conformance tests agree on stable member/result metadata. |
| SC-014 | Pass | `test-migration-ledger.md` records a passing successor or explicit architectural removal rationale for every affected objective. |

## Focused verification matrix

The final review pass also covers pinned optional-input defaults, sanitized expression incidents,
bounded Jint result materialization, external-storage response validation, scoped evaluator
composition, completed-work replay, bounded attempt diagnostics, child-callback deduplication,
fresh attempt identity on crash redelivery, atomic alternative-bookmark retirement, and terminal
private-state cleanup. It additionally covers canonical absent optional inputs, fail-closed trigger
payload decoding, bounded/redacted trigger-delivery history, stale bookmark-creation suppression,
cancellation-safe and timeout-bounded lifecycle notification, closed bookmark-resume checkpoint
transitions, token-aware cancellation faulting, and disposal-before-checkpoint activation cleanup.

| Project | Result |
|---|---|
| `Elsa.Activities.Design.Tests` | 218 passed |
| `Elsa.Activities.Runtime.Tests` | 150 passed |
| `Elsa.Activities.ControlFlow.Tests` | 196 passed |
| `Elsa.Activities.Sequence.Tests` | 15 passed |
| `Elsa.Activities.Flowchart.Tests` | 55 passed |
| `Elsa.Activities.Scripting.Tests` | 8 passed |
| `Elsa.Activities.Scheduling.Tests` | 25 passed |
| `Elsa.Activities.Http.Tests` | 192 passed |
| `Elsa.Activities.Http.IntegrationTests` | 29 passed; 1 pre-existing performance assertion failed |
| `Elsa.Workflows.Publishing.Api.Tests` | 173 passed |
| `Elsa.Workflows.Design.CodeGeneration.Tests` | 12 passed |
| `Elsa.Workflows.Runtime.Tests` | 905 passed |
| `Elsa.Persistence.Groundwork.Tests` | 246 passed |
| `Elsa.Expressions.Tests` | 93 passed |
| `Elsa.Expressions.JavaScript.Jint.Tests` | 38 passed |
| `Elsa3.Mapping.Tests` | 30 passed |
| `Elsa.Architecture.Tests` | 97 passed |
| Activation semantic test host | 17 passed |

The complete solution build passes with zero errors and one existing package-pruning warning. The
complete solution test was executed with Git commit signing disabled for repository-fixture commits.
Every spec-095 affected functional suite above passes; the solution-wide parallel run had two failures
outside the changed files: one Structured Logs SQLite service-resolution timeout
(isolated project rerun: 30/30) and the HTTP checkpoint-coalescing performance assertion. The latter
also fails in isolation with the same 18-immediate/15-coalesced counts at the untouched fixed-point
commit `0038de76`, confirming it is not introduced by this review pass. The final canonical carrier
scan has zero non-architecture/non-importer hits. Generated maps were not refreshed, and the generated
map snapshot remains unchanged.
