# Research: Activities change variables, committed with the step (spec 1231, S-7)

Code facts are read on the initiative branch at `dc32983a2`, S-10's PR #4316 (head `7ec7673fa`) and S-11's PR #4725
(head `19db93473e`). Neither PR is edited by this spec.

## R-1 — What exists today

- **No variable session.** `IActivityExecutionContext` (`Essentials/src/Activities/Runtime/Core/Contracts/IActivityExecutionContext.cs`)
  has `Activity` and `CancellationToken` only. Structural activities marked `IRuntimeScopedVariableReader` read
  committed values through `IRuntimeActivityExecutionContext.TryReadScopedVariableValue` (read-only, innermost scope
  wins, `VF-ACT-010` for a withheld value). Nothing can write from activity code.
- **RunJavaScript** gets `args` only today. S-4 (spec 1222, merged) specifies the body read surface (`variables.X`,
  `variables['X']`, `getVariable`, run-time fallback for computed and opaque reads); its body tasks T045–T050 wait on
  S-10 and on this spec's session.
- **S-10 (#4316)** builds: `VariableVersion`, `VariableReadSetEntry`, `VariableInputReadSet` (one entry per variable,
  oldest read wins), `VariableWriteBase`, `VariableCommitRule`/`VariableCommitDecision`, `LostUpdateMode` with
  `[ActivityLostUpdateMode]` and the pinned node metadata, `VariableWriteRecord` with
  `VariableWriteSource.SessionChange` already defined, `RuntimeVariableReadRecorder`, `RuntimeCommitSequenceStamper`.
  Its tasks.md moves T015 and T016 to S-7 and records that the own-write base advance on resume (part of T033) moved
  with T016.
- **S-11 (#4725)** builds `BackgroundEligibility` in `Essentials/src/Workflows/Runtime/Core/Background/`, with C9 as
  one predicate `AccessesVariablesByName(Type)` (today: `IRuntimeScopedVariableReader`) and a subject
  (`BackgroundEligibilitySubject`) built from the pinned node. It marks `RunJavaScript` `[BackgroundExecutionSafe]`
  in its first head; the coordinator then decided it is not (`eb9b1b1a4a`): its scripts read and write variables by
  name.
- **Converter.** `Elsa3ExpressionRewriter` refuses `setVariable`/`setOutput`/`setInput`/`setResult` (`SetterCalls`),
  generated setters (`TryGetAccessorName(…, "set", …)`), and assignments to ambient roots, all with
  `ForbiddenCapability` and `MutationGuidance` ("Move the assignment into a Set Variable activity…"). The same visitor
  serves input expressions and `RunJavaScript` (script mode, `Elsa3JavaScriptMode`).

## R-2 — The session lives in Runtime Core, behind the activity context

**Decision.** Add `Variables` to the activity execution context as a narrow `IStepVariables` surface (`Get<T>`, `Set`,
free-name overloads), implemented in `Elsa.Workflows.Runtime` over the step's visible frame chain (the
`RuntimeContainerScopeService` projection the scoped reader already uses). The session object is created per step by
the invoke and resume work handlers, and its intents are handed to the checkpoint commit those handlers already build.

**Rationale.** One read path for the scoped reader and the session (ADR 0092 decision 1: one mechanism). Committing
through the existing checkpoint keeps decision 6 (intents with the step's commit) without a new commit path.
`context.Variables` exposes only variables (ADR 0045 §6 exception, not a service locator).

**Alternatives.** A separate service injected into activities: rejected, it would be callable outside a step. Writes
as scheduled `Set` nodes: rejected by ADR 0090 decision 6.

## R-3 — Bases: session reads join S-10's read set

**Decision.** A session read records a `VariableReadSetEntry` in the attempt's session reads. A session write's base
is chosen as: the session read of that variable if any, else the input read set's entry, else none (blind). When both
exist, the older version is kept as the base for writes built on the input (a capture), and the session read's version
for session writes; where the source is ambiguous, the oldest in-use dependency wins (spec 1221 R-4). An applied own
write advances the invocation's base for that variable; the advanced base is persisted with the invocation's state
and restored on resume (ADR 0092 decisions 4 and 11).

**Rationale.** ADR 0092 decision 4: a newer session read must not hide an older input dependency. S-10 already keeps
one entry per variable for the input read set (oldest wins); session reads are a separate per-attempt list, so both
survive.

**Alternatives.** One advancing base per variable: rejected in spec 1221 R-4 (loses the older dependency).

## R-4 — Script writes: publish pins targets, S-8 finds changes

**Decision.** The publish analyzer that S-4 builds for body reads is extended with write forms: assignment and compound
assignment to `variables.x`/`variables['x']`, `setVariable('x', v)`, member assignment on a value read from a pinned
variable, and escapes of such values. It pins each target and the may-write set onto the node. At run time S-8's
bridge reports whole assignments and changed paths per variable; this spec turns them into intents (C3) and commits
them. Until S-8's bridge lands, whole assignments go through a minimal path; member changes land with S-8 (spec 1228 Q1:
whichever arrives second carries the end-to-end test).

**Rationale.** One resolver for reads and writes (ADR 0090 decision 3). The mechanism for finding changes is S-8's by
agreement (spec 1228 Q1, Q2).

## R-5 — C# references are inputs with a reference kind

**Decision.** `VariableReference<T>` and `ReadOnlyVariableReference<T>` are input types whose contract input carries a
variable-reference kind and direction. They bind like a capture target (a variable declaration reference), resolve
through the same scoped frame resolution, and are validated at publish (FR-024). `[AccessesUndeclaredVariables]` is an
activity-type attribute pinned into the contract (like `SupportsBackgroundExecution`), so a shell without the runtime
type still sees it.

**Rationale.** The contract is what publication, the descriptor and C9 read; carrying both signals in the contract
lets C9 refuse them at placement even when the CLR type is not loaded (spec 1230 `TypeCheckedAtRunTime`).

## R-6 — C9: which signals, and the read-only reference (Q2)

**Decision (recommended).** C9 refuses, in `BackgroundEligibility`:
1. `IRuntimeScopedVariableReader` (unchanged);
2. an activity type marked `[AccessesUndeclaredVariables]`;
3. any input of kind variable reference, writable **or read-only**.

`RunJavaScript` needs no rule: the coordinator decided on #4725 (`eb9b1b1a4a`) that it is not
`[BackgroundExecutionSafe]`, and S-11 refuses a background `RunJavaScript`.

`AccessesVariablesByName(Type)` covers 1–3 from the CLR type; a new node-level fact on the subject
(`TouchesVariables`, from the pinned contract and node metadata) covers 2–3 without the CLR type. The refusal reason
names the signal.

**Why the read-only reference is refused (Q2).** C9's criterion text in ADR 0093 is "The body does not read or write
workflow variables". Its enforcement sentence names `IRuntimeScopedVariableReader` (a read-only signal) and "later"
ADR 0090's writable signals; it does not name the read-only reference because ADR 0090's read-only reference was not
discussed there, not because reads are allowed. A body read on a worker would see durable state that can lag the
working state (ADR 0090 open question 7) and would add a session to background steps, which C9's *Limit* says lifts
only with a background session through ADR 0092. Refusing it applies the criterion as written; no ADR change is needed.
If the coordinator prefers to make the enforcement sentence list every signal explicitly, that is a one-line ADR 0093
amendment in its own PR (offered, not required).

**Alternative.** Allow read-only references on the worker (needs a background session, ADR 0093 amendment).

## R-7 — Opaque writes (Q5)

**Decision (recommended).** A body in which the `variables` root or `setVariable` escapes (aliased, passed, spread,
enumerated) may read through S-4's fallback but may not write: publish refuses a write anywhere in such a body, and a
write through the escaped root at run time throws (the root is read-only in that body). Computed write names are
refused.

**Rationale.** ADR 0090 decision 3 refuses computed names in any form for writes, and the may-write set must stay
exact for the concurrent-write rule. ADR 0092's *Consequences* let decision 3 follow the ADR 0089 amendment, which
covers reads only.

## R-8 — Converter

**Decision (recommended, Q6).** In script mode only: `setVariable('<lit>', v)` and `set<Name>(v)` of a variable the
resolver finds become `variables.<Name> = v` (bracket form for non-identifier names); `getVariable('<lit>').m = v`
becomes `variables.<Name>.m = v` (S-4 already rewrites the read). New refusals keep stable codes under
`Elsa3ImportDiagnosticCodes` (computed setter name; setter of an undeclared variable; setter of a variable that
converts to a transient one). Expression mode keeps today's refusal; `MutationGuidance` adds "or move the logic into a
Run JavaScript activity". `setOutput`, `setInput`, `setResult`, `setCorrelationId` and `setWorkflowInstanceName` stay
refused (Q1).

**Faithfulness check.** Elsa 3's `setVariable` writes the live variable immediately; later reads in the same script
see it (read-your-writes holds). Other branches saw it only after the activity finished on Elsa 3's single thread,
which matches commit-with-step for inline steps. Elsa 3 created undeclared variables on write; Elsa 4 refuses
(decision 5), so the converter refuses rather than guess a type.

## R-9 — Cost

To be measured in implementation (FR-035): per written variable one serialization and one canonical digest (S-10's
`VariableValueDigest`, ~17–19 ms/MiB measured in #4316); per session read one read-set entry (~no hashing in default
mode). Reads serialize nothing (C1).

## R-10 — Out of scope, recorded

- Host interop (decision 8): #4103 (D-16).
- Structural sessions (ADR 0090 open question 6): not built (Q4).
- Background sessions (open question 7): not reached because C9 refuses.
- Outputs, correlation id, instance name from step bodies (Q1): proposed new row.
- #3023: graph-activity intrinsics, not step writes (Q7).
