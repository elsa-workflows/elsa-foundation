# Research: Scripts Read Variables By Name

**Spec**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md) · Draft (initiative #3052)

This resolves the plan's open questions and the mechanism choices, grounded in the current code. The read model is
decision **D-7**, recorded in ADR 0089 as accepted (amendment R1–R4 of decisions 4 and 5).

## R-1: Two read surfaces today, and how they combine

**Facts.** A script today sees two roots:

- `args` — spec 095 declared parameters, each resolved to one source, type/policy-checked, pinned into the input
  snapshot; read set known at publish (`IsolatedJintEngine.SetReadOnlyArgsCore`; materialized by
  `RuntimeActivityInputMaterializer.MaterializeSnapshotAsync` + `RuntimePortableExpressionEvaluator.MaterializeParametersAsync`).
- `variables` / `getVariable` / `get<Name>()` — the issue-#984 *ambient* surface: regex-discovered per evaluation
  (`RuntimePortableExpressionEvaluator.IsAmbientVariableReferenced`, `HasComputedAmbientAccess`), projected from the
  runtime visible-variable frame chain, policy-tainted at run time; computed access over-approximates to **all** visible
  variables. spec 095 (`binding-pure-v1`) forbids this surface in pure input bindings.

**Decision (D-7).** Literal names use the **declared-parameter (pinned) surface**, exposed under a `variables` root.
Computed names and opaque host-function reads use a **run-time fallback lookup** built on the existing visible-variable
projection, but only for expressions that need it, discovered by parsing at publish rather than by regex at run time.
Generated `get<Name>()` accessors are removed (decision 6); they survive only as a converted Elsa 3 form that the
converter rewrites to `variables.X`. `getVariable` stays as a runtime function, because computed calls need it.

**Rationale.** Pinning gives a publish-known read set for the common case (precise dependency, protection check,
tooling) and avoids loading unused, large or external variables. The fallback keeps computed Elsa 3 scripts convertible
instead of refusing them, and recording the actual reads keeps the snapshot honest.

**Alternatives considered.** Refuse computed names at publish (the earlier F-1 position): rejected by D-7 — it blocks
faithful conversion of real Elsa 3 scripts. Ambient-only for all reads: rejected — read set unknown at publish for
literal names that could be pinned.

## R-2: Serialized shape (settled by ADR 0089)

**Decision.**

- Add a **per-parameter surface flag** to the compiled `Variable` parameter: `surface = variables | args` (default
  `args`). One parameter list, no second map. The evaluator builds the `variables` root from parameters whose surface is
  `variables`, and the `args` root from the rest. Roots are separate objects, so names cannot collide.
- Add an **expression-level fallback marker** to the compiled binding: present when publish found a computed or opaque
  read, carrying the conservative read report (the candidate variable references visible at that point).

**Rationale.** Smallest change to spec 095's compiled binding; one list keeps materialization uniform; the marker lets
the runtime skip the fallback for literal-only expressions (FR-013) without any run-time source inspection.

**Alternatives considered.** A separate `variables` parameter map — more serialized surface, duplicate materialization
paths, rejected. Rewriting `variables.X` into `args.X` at publish (today's converter approach) — rejected: it is the
root of #3296 and conflates the two roots. Keeping the run-time regex to decide when to project — rejected (R-6).

**Contract impact.** spec 095 expression contract §1 (surface flag, fallback marker, recorded reads in the evaluation
result) and §6 (the importer keeps `variables.X` and computed `getVariable(expr)` instead of lowering or refusing).

## R-3 (spec Q1): Loop iteration values

**Facts.** Nearest-scope resolution already reuses `ScopedVariableResolver.Resolve`, but that resolver covers
containers and the workflow, not loop iteration scopes. ADR 0028 makes the iteration scope the innermost scope of the
body, with the loop node as declaring scope; the loop variables are `currentItem` and `currentIndex` (ForEach) and
`index` (For), and the Elsa 3 converter already lowers `CurrentValue` to them (`Elsa3LoopVariableReferencesRewrite`).
At run time `RuntimeLoopIterationFrameFactory.Create` (ADR 0028 still names it `RuntimeLoopIterationScopeFactory`)
builds one frame per pass, and the ambient projection already includes iteration frames, innermost first. Pinned
`Variable` parameters are materialized by `RuntimePortableExpressionEvaluator.MaterializeParametersAsync`, which must
learn to read a parameter whose declaring scope is a loop node from the current pass's frame. On the Design side,
declarations reach `ScopedVariableResolver` only through `IActivityStructureHandler.ProjectScopedVariables`, which
compile into container variables; the loop module's `LoopStructureHandler` (shared with While and Do) has no
iteration projection. ForEach puts `currentIndex` in the pass's frame only when `ExposeIndex` is true. `ExposeIndex`
is an ordinary activity input: it may be bound to an expression, which the runtime resolves and hydrates from the
step's input snapshot, so nothing is missing in the code; publish just cannot know the value. Iteration values follow the normal checkpoint boundary (ADR 0028), so
they are durable. ADR 0090 decision 2 makes a loop item frozen and untracked in a script. Spec 265 projects graph
variables through synthetic frames.

**Decision (spec Q1, accepted).** The publish-time resolver adds the loop's iteration declarations as the innermost
scope of every node in the loop body, resolves an iteration-scope variable as the nearest scope (innermost first) and
pins it as a `Variable` parameter recording the loop node id + key. The iteration declarations come from a new,
default-empty iteration-projection member on the structure handler, implemented for ForEach and For only and kept
apart from container variables (an extension-point catalog change). `currentIndex` is declared unless
`ExposeIndex` is a literal `false` (then a literal read of it is a publish diagnostic). When an `ExposeIndex`
expression resolves to `false` for a run, a pinned read of `currentIndex` faults the step, naming the node and the
variable, instead of reading `null`. BPMN multi-instance is out of scope. At
run time the pinned value is materialized from the current pass's iteration frame. The fallback lookup walks the same
frame chain, innermost first, so a computed name sees the iteration value the same way.

## R-4: The run-time fallback for computed and opaque reads

**Facts.** A computed read (`variables[expr]`, `getVariable(expr)`) needs its value synchronously mid-script, but the
name is unknown at publish and values may be stored externally (async). Today's runtime already handles this by
projecting all visible variables when it detects computed access.

**Decision.**

1. **Publish** marks the expression as needing the fallback and reports conservatively which variables it could read
   (the visible variables at that point). The report feeds dependency tooling, the rename flag (FR-022) and a
   "possible protected read" diagnostic (FR-018), not a refusal.
2. **Before evaluation** the runtime makes the candidate set readable. This starts from the visible-variable
   projection (`RuntimeContainerScopeService.ProjectAmbientVisibleVariableEnvelopes`), which today returns only
   name → envelope, skips unresolvable frames and silently omits transient candidates; it must be extended to carry
   each candidate's declaring scope and a transient marker, so a read can be recorded with its scope (FR-010) and a
   transient match can fault (FR-017). This is the accepted cost of a computed read; it is confined to marked
   expressions.
3. **During evaluation** each computed or host-function read resolves the name nearest-scope-first over the candidates,
   checks protection for the variable actually read (a withheld value is not provided; the step faults with the
   protection diagnostic), and records the read (name, declaring scope, present/absent). A transient or withheld match
   is recorded on the host side and faults the step even if the script catches the JavaScript exception (FR-017a).
4. **After evaluation** the recorded reads of an input-binding expression enter the step's input snapshot alongside
   the pinned values; a body's reads go into its session reads instead (R-10). Candidates that
   were made readable but not read are not recorded.
5. **A missing name** reads `undefined` in JavaScript (never `null`) and nil in Liquid, records a warning, and marks
   the read absent (spec 1216 FR-025/FR-026). A missing **literal** name stays a publish diagnostic (FR-027).

**Rationale.** Matches D-7 word for word: static pinning plus dynamic fallback, conservative reporting, recorded and
checked actual reads. Reuses the projection the runtime already has, so no new pattern.

**Alternatives considered.** (a) Lazy per-read loading from the store — not possible with synchronous Jint reads and
async stores; could be an optimization later for inline-stored values. (b) Protection-check the whole candidate set
before evaluation (today's behaviour: any withheld visible variable refuses the computed access) — rejected: D-7 checks
the actual reads, so a withheld variable the script never reads does not block it.

**Open data point.** Frequency of computed `getVariable(name)` in the real Elsa 3 corpus would size the fallback's cost;
the private JTest corpus is unavailable locally, so this is flagged for whoever holds it.

## R-5: Transient variables and the fallback (settled by ADR 0089 R3)

**Decision.** A literal read of a transient variable keeps its publish diagnostic (D-7: retain the existing rule). A
computed read that resolves at run time to a transient variable faults the step, naming the node and the variable,
because its value cannot enter the input snapshot (ADR 0089 R3); publish lists transient candidates in the
conservative report as a warning. Host-interop mode (ADR 0090 decision 8) is out of scope.

**Rejected.** Treating it like a missing name (`undefined` + warning) would hide the failure behind a value the script
may treat as valid (ADR 0089 R3).

## R-6: Publish parses, never regex

**Decision.** Publish determines the literal read set and detects computed access by parsing the source with the
language parser (JavaScript parser for JS, Liquid's parser for Liquid), replacing the runtime regex detection
(`IsAmbientVariableReferenced`, `HasComputedAmbientAccess`). The publish parser must accept the same source the runtime
parser accepts (the trailing-line-comment bug is the #3171 side track on `main`, consumed here).

**Rationale.** spec 095 §6 already forbids regex semantic rewriting; parsing gives exact literal-name resolution and
reliable computed-access detection, and moves the decision to publish so the runtime reads a marker.

## R-7: Decision-7 ambiguity refusal is dropped

**Decision (spec Q4, accepted).** Variables are read as `variables.X` in input expressions and in the `RunJavaScript`
body; variable reads are no longer lowered to `args`, so the body gets a `variables` root next to `args`. The former
"declare the same variable two ways is ambiguous" refusal is removed, because the roots are separate objects.

**Explicit `args` parameters (spec Q6, accepted).** `variables` is the container for variables. An author may still
declare an explicit `args` parameter bound to a variable (ADR 0089 decision 7); that is the author's choice, and the
author tells their users to read it through `args`.

## R-8: Secrets / protection

**Decision.** A literal read of a protected variable is protection-checked at publish (the name is known). A computed or
opaque read is reported at publish as a possible protected read when the candidate set contains one, and checked at run
time for the variable actually read. The hard guarantee is **redaction**: secret values never appear in logs,
diagnostics or recorded reads. A value the context has no permission to read is not provided (access control,
independent of redaction). Laundering a secret into a non-secret variable is not hard-blocked (author accountability);
diagnostics are emitted where detectable.

## R-9: Rename safety

**Decision.** Readable names stay in source (not internal ids). Studio updates references it can resolve on rename and
flags the rest for manual repair, including computed reads whose conservative report contains the renamed variable.
Publish refuses a literal name that no longer resolves; a literal name resolving to a *different* declaration than the
previously published version is a publish warning. Published versions never change (ADR 0089 R1).

## R-10: Input snapshot vs body session

**Decision.** Input-binding expressions read the step's input snapshot (pinned values plus recorded fallback reads) and
never see pending writes. The `RunJavaScript` body reads — literal and computed — go through its variable **session**
(ADR 0090 decision 2, ADR 0092), which shows the body's own pending writes. Its reads go into the body's **session reads** (ADR 0092 decision 3), recorded per attempt, not into the input snapshot; ADR 0089
R3's "part of the input snapshot" applies to input-binding expressions. The session and the read set are built by
S-10 (spec 1221); this spec's body surface waits on it. Writes
(ADR 0090 / S-7) are out of scope; the spec records the write assumptions it relies on (writes commit at output
evaluation; the session shows pending writes; a computed write to a non-existent variable refuses) to be verified
against S-7. This is the distinction D-7 asks to preserve explicitly.

## R-11: Converter reconciliation (spec 247)

**Decision.**

- Emit `variables.X` for Elsa 3 literal variable reads (`Variables.X`, `getVariable("X")`, generated `get<Name>()` via
  `Elsa3JavaScriptNames`), spelled as declared. Keep `args.X` for inputs, activity results, instance metadata.
- **Computed reads convert faithfully.** Elsa 3 `getVariable(expr)` stays `getVariable(expr)`; `Variables[expr]`
  becomes `variables[expr]`. Both are served by the run-time fallback. They are no longer refused.
- **#3296:** refuse (faithful-or-refuse) with guidance when a script's own declared `args` would collide with lowered
  `args` reads in the same scope (the existing guard at `Elsa3ExpressionRewriter.cs` is extended to the declared case).
  Variable reads no longer lower to `args`, so they no longer collide.
- **#3287** (the `arguments` type) is already fixed by #4315 (`Elsa3ValueFlowLowerer.Lower` takes the node's input
  contract type); nothing left here.
- **#3010:** emit statement bodies without the IIFE wrapper on the `RunJavaScript` body path. The wrapper is emitted in
  `Elsa3ExpressionRewriter` (not the lowerer); Script mode already emits without it through `EndScript` (already done for Script
  mode; confirm no regression).
- Faithfulness: refuse a literal read when Elsa 4's nearest-scope resolution picks a different declaration than Elsa
  3's. For a computed read, record an import warning when a candidate name is declared in more than one visible scope
  (the run-time lookup resolves nearest-scope-first).
- A converted read of a variable that is transient in Elsa 4 is flagged at import with its own diagnostic.
