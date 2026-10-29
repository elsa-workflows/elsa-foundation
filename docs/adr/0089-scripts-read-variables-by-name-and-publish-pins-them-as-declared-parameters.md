# Scripts Read Variables By Name And Publish Pins Them As Declared Parameters

Status: proposed (2026-10-23, amended 2026-10-24). Sipke Schoorstra, the decision holder, chose this option on
[#3204](https://github.com/elsa-workflows/elsa-foundation/issues/3204) (US2), and on 2026-10-24 settled the
converter output and the former open questions on the Liquid root and on unset and transient variables.
Acceptance needs agreement with @j03y-nxxbz per #3204; it does not wait for the full review of
[ADR 0045 §5](0045-workflow-value-flow-uses-role-owned-bindings-and-immutable-invocation-records.md#5-make-expressions-serialized-explicit-and-pure)
(#3026), and this ADR changes with it if that review reads §5 more strictly. Its "Writes are unchanged: scripts do
not write variables" would be superseded by
[ADR 0090](0090-activities-change-variables-as-a-unit-of-work-committed-with-the-step.md) if that is accepted.
Its read model, decisions 4 and 5 included, would then cover input-binding expressions only; RunJavaScript would
read through ADR 0090's step sessions.

Amendment proposed (2026-10-29, [epic #3052](https://github.com/elsa-workflows/elsa-foundation/issues/3052) analysis
A-4 and decision D-7): scripts read through a read-only accessor at evaluation time, publish validates names instead of
pinning them, and the engine records the actual reads. If accepted, it replaces decisions 2 to 5 and 7; decisions 1, 6
and 8 stay. See *Amendment* below.

Part of epic #3052 and program #2310. Constrained by
[ADR 0045](0045-workflow-value-flow-uses-role-owned-bindings-and-immutable-invocation-records.md) §5 (explicit, pure
expressions), [ADR 0027](0027-scoped-variable-references-include-declaring-scope.md) and
[ADR 0048](0048-container-output-captures-resolve-through-producer-visible-variable-frames.md) (a variable is
addressed by its declaring scope and key), [ADR 0083](0083-javascript-binding-grammar-is-pinned-at-publish.md)
(expressions and statement bodies), and
[spec 095's expression contract](../../specs/095-value-flow-redesign/contracts/expression-and-import-contract.md).

## Context

Elsa 4 has two ways for a JavaScript binding to read a variable, and no decision recorded which one is canonical.

- **Declared parameters (`args.X`).** Spec 095's model. An expression carries named parameters, and each one is bound
  to exactly one source: a literal, a workflow request member, a variable (`declaringScopeNodeId` plus
  `variableKey`), an activity result, or workflow-instance metadata. The runtime resolves, type-checks,
  policy-checks and snapshots every parameter before evaluation, and the script sees only a frozen `args` object.
  The Elsa 3 converter (spec 247) lowers `variables.X`, `getVariable("X")` and `get<Name>()` to this form.
- **Ambient variables.** Added by #991 (issue #984). `IsolatedJintEngine.SetReadOnlyVariables` installs a frozen
  `variables` object, `getVariable('X')` and a `get<Name>()` function per variable. On every evaluation,
  `RuntimePortableExpressionEvaluator` finds which visible variables the source names by regular expression and
  hands only those to the engine. Visibility comes from the runtime frame chain, where the innermost scope wins for
  a shadowed name. A computed access (`variables[x]`, `getVariable(x)`) exposes every visible variable and takes all
  their policy taint. Liquid has no ambient surface: it sees only declared parameter roots.

The ambient surface is friendlier, but it sits awkwardly with ADR 0045 §5, which says input-binding expressions
cannot "discover workflow values through ambient name-based accessors". Its read set is only known at run time, so
publish cannot check it. Spec 265 (R-9) had to leave ambient reads unchecked at publish for that reason.

The two `get<Name>()` naming rules also differ. The runtime's `ToGetterName` upper-cases the first character and
accepts letters, digits, `_` and `$`. The converter's `Elsa3JavaScriptNames` matches Elsa 3 (Humanizer `Pascalize`,
letters and digits only). So Elsa 4's `get<Name>()` is not quite Elsa 3's.

Elsa 4 is unreleased and has no existing data, so none of the options below has a compatibility cost.

## Decision

**1. Authors write variables by name.** In JavaScript the standard form is `variables.X`, with
`variables['X']` (a string literal) and `getVariable('X')` (a string literal argument) as aliases. Liquid gets the
same root, `{{ variables.X }}`; the converter rewrites Elsa 3's `{{ Variables.X }}`. There is one name, not both.

**2. Publish resolves each name to its nearest declaring scope.** The read set comes from parsing the source with a
JavaScript parser, the way the converter already does, not from regular expressions; Liquid templates are parsed
the same way, with Liquid's parser. Each name resolves against the node's visible scopes, nearest first: for a loop
body its iteration scope (ADR 0028), then the enclosing container, then outer containers, then the workflow. An inner
declaration shadows an outer one, as in Elsa 3 and most programming languages. The runtime's scoped variable
reader (`IRuntimeActivityExecutionContext.TryReadScopedVariableValue`) already resolves in this order. Studio's
`ScopedVariableResolver` covers containers and the workflow; the implementing spec adds the iteration scope.

**3. Publish pins each read as a declared parameter.** Every resolved name becomes a spec 095 `Variable` parameter
binding that records the declaring scope node id and the variable key. The authored source is not rewritten (ADR
0083 keeps source as written); the compiled binding carries the parameters, and the evaluator exposes them under
`variables`. The parameters take part in the artifact hash like any other, so a change in which declaration a name
resolves to is a behaviour change.

**4. Run time sees only declared values.** The evaluator receives only the pinned parameters, each resolved,
type-checked, policy-checked and snapshotted through the existing declared-parameter path. There is no run-time name
lookup and no frame-chain projection for scripts. Spec 095's guarantees are kept: determinism, policy taint, exact
editor completions, and an exact answer to "which expressions read this variable?".

**5. Computed names are refused at publish.** `variables[name]`, `getVariable(expr)` with a non-literal argument,
or passing `variables` or `getVariable` around as a value, is a publish diagnostic. This is the same reason the
importer refuses such reads with `VF-IMP-006`. A name that resolves to no visible variable is also a publish
diagnostic, not a run-time `undefined`. So is a read of a transient (non-durable) variable, because its value cannot
be snapshotted.

**6. `get<Name>()` leaves the runtime.** It survives only as an Elsa 3 import form that the converter rewrites, using
Elsa 3's naming rule (`Elsa3JavaScriptNames`). The runtime's `ToGetterName` and the getter completions in
`JavaScriptExpressionToolingProvider` are removed, which settles the naming mismatch.

**7. `args` stays the canonical form and stays authorable.** `variables.X` is a convenience for variables; `args` is
what every parameter is underneath. Workflow inputs, activity results and instance metadata are still read through
`args`, as on main today. An author may also write `args.X` for an explicit parameter. A script must not declare
the same variable both ways; publish refuses that as ambiguous.

**8. An unset variable reads as `null`.** A name that resolves to a declared variable with no value binds JSON
`null`, as Elsa 3's `getVariable` returned and as the workflow-instance binding treats an absent correlation ID. It
never binds `undefined`, which the evaluator refuses as a result. A pinned variable parameter is therefore never an
absent required parameter in spec 095's sense: it binds `null` when its variable has no value.

Writes are unchanged: scripts do not write variables. A change goes through an output capture or the `Set`
intrinsic (ADR 0045 §4). (Proposed amendment:
[ADR 0090](0090-activities-change-variables-as-a-unit-of-work-committed-with-the-step.md)
would let activities, RunJavaScript included, change variables; input-binding expressions would stay read-only.)

## Amendment (epic #3052, analysis A-4 and decision D-7, 2026-10-29)

**Why.** Declared parameters, and pinning reads into them at publish, served the determinism and replay model of the
original ADR 0045 §5: every read known before evaluation, re-evaluation giving the same result, and taint, completions
and "which expressions read this variable?" answered statically. Three things changed:
- **D-1 and D-2 dropped expression determinism.** The contract is now that a recorded step never runs again and
  recovery runs forward ([ADR 0045 §5 amendment, #3384](https://github.com/elsa-workflows/elsa-foundation/pull/3384)).
- **The input snapshot already pins the result.** Spec 095 §4: retry and resume do not re-evaluate an already pinned
  input expression. Recovery needs what came out of an expression, not what it read.
- **D-7 already accepts a run-time lookup** for computed names such as `getVariable(name)`. Keeping publish pinning
  beside it means two read mechanisms.

So the main reason for publish-inferred parameters is gone, and inference at publish is machinery kept alive to serve
them. This amendment replaces decisions 2 to 5 and 7 with one read mechanism. Decisions 1 (authors write variables by
name), 6 (`get<Name>()` leaves the runtime) and 8 (an unset variable reads as `null`) stay.

This also changes D-7 as agreed on the epic, which says publish pins the names it can resolve. Everything else in D-7
holds: scripts read `variables.X`, computed names are looked up at run time and reported conservatively, actual reads
are checked for protection, and the converter emits `variables.X`. If the amendment is rejected, the minimal amendment
under *Alternatives considered* writes D-7 as agreed.

**A1. One read mechanism: a read-only accessor at evaluation time.** A script reads through roots the evaluator
provides:
- `variables.X`, with the aliases of decision 1, and computed names (`variables[name]`, `getVariable(expr)`);
- `input.X` for workflow inputs;
- `activities.<name>.<output>` for results of activities the node can see (spec 265's graph scope reads);
- `workflow.correlationId` and `workflow.instanceId`.

Names resolve against the node's visible scopes, nearest first, in the order decision 2 describes, now at run time
through the scoped reader the runtime already has (`TryReadScopedVariableValue`). Liquid gets the same roots. The
RunJavaScript activity body reads the same way; whether it may also write is A-3 and ADR 0090.

**A2. One consistent view per materialization.** Every read made while a node's inputs are materialized comes from the
same committed state: the state the input snapshot is taken from (ADR 0045 §3). A script never sees a value that another
branch commits while it runs. This is the read side of
[ADR 0092](https://github.com/elsa-workflows/elsa-foundation/pull/3411) (decision 10: no gap between reads and body).

**A3. The engine records the actual reads.** Every read enters the invocation's input read set (ADR 0092 decision 3),
with the version and digest it saw, and is committed with the input snapshot. The recorded reads serve:
- **protection:** a result inherits the strongest protection policy of what it read, as A-1 proposes, checked on the
  actual reads instead of refused at publish;
- **inspection:** "which invocations read this variable, and which version";
- **lost-update detection** in ADR 0092 (a read-modify-write needs its base).

**A4. Publish validates; it does not add.** Publish parses each script with the language's parser (decision 2's rule,
no regular expressions) and checks it. It adds no parameter bindings.
- A statically resolvable name that resolves to nothing visible is a publish diagnostic.
- A computed name is allowed. Publish reports it conservatively: "may read any visible variable".
- A statically resolvable name that now resolves to a different declaration than in the previously published version
  is a publish warning (kept from the Consequences below).
- A transient (non-durable) variable is readable, because only the expression's result is persisted. It enters the
  read set without a digest.

**A5. `args` stays for explicit bindings.** Literals, composed expressions and hand-wired values remain declared
parameters, as spec 095 defines them. A script may read one value both ways: both come from the same view (A2), so
decision 7's ambiguity refusal is no longer needed.

**A6. The evaluator gets a read-only reader port.** Spec 095's expression contract §1 says the evaluation request
contains no variable frame. That changes: the request carries a port that answers reads from the view of A2 and records
them (A3). It cannot write, enumerate other scopes or reach services. The evaluator stays testable with a fake port.

**A7. A rename never silently breaks or changes a read.** Renaming a variable or an activity must not change what a
script reads without the author seeing it (agreed by @j03y-nxxbz and @FransVanEk on the epic). A published version
never changes, so the risk is in the draft. Two mechanisms are possible (reviewer question 2):
- the source stores stable ids and Studio shows names;
- Studio's rename updates every reference.

In both, publish refusing a name that no longer resolves (A4) is the safety net.

**Writes are unchanged by this amendment.** Input-binding expressions never write (D-3). Writes from activities are A-3
and ADR 0090.

**What this gives up:**
- **Exact static answers.** "Which expressions read X" becomes best effort in tooling plus the recorded actual reads.
- **Taint at publish.** Protection becomes a run-time check on the actual reads (A3).
- **A fully self-contained evaluator.** It gets a read-only port instead of only parameter values (A6). This reverses an
  explicit spec 095 principle.
- **Resolution at publish.** A new inner declaration that shadows an outer one changes what a script reads. That is a
  definition change, so a new version, and A4 warns about it.

**What this gains:**
- **One read mechanism** instead of pinning plus a run-time fallback.
- **A simpler, more faithful converter.** It keeps Elsa 3 reads as written (`Variables.X` becomes `variables.X`;
  `get<Name>()` is still rewritten, decision 6) instead of rewriting them into `args`. That rewrite is where #3296 (a
  script's own `args` shadows converted reads) and the wrapper problems on #3204 come from. Computed
  `getVariable(name)` converts instead of being refused with `VF-IMP-006`.
- **Transient variables become readable** (A4).
- **Authors write what they mean,** in Elsa 3's form.

**What it touches:**
- **ADR 0045 §5.** "cannot … discover workflow values through ambient name-based accessors" becomes: input-binding
  expressions read through the read-only accessor, and their reads are recorded. #3384 adopts this once the amendment
  is agreed.
- **Spec 095:** expression contract §1 (the reader port) and §6 (the importer no longer lowers reads to `args`), in the
  implementing spec (S-4 on the epic).
- **ADR 0092 decision 3** already records actual reads, including run-time lookups once D-7 is written here.
- **ADR 0090 §3** refuses computed names, as decision 5 does; it follows this amendment.

**Questions for reviewers** (@sfmskywalker, @FransVanEk):
1. Do you agree that publish-inferred parameters lost their main reason with D-1 and D-2? Is anything lost that A2,
   A3 and A6 do not cover?
2. Renames (A7): ids in the source, or rename updates every reference?
3. A computed name that resolves to no visible variable at run time: does it read `null` and record a warning, as Elsa
   3's `getVariable` returned, or does it fault the evaluation?
4. `activities.<name>.<output>`: addressed by name or by id in the source (ties to question 2)?
5. @FransVanEk: your §5 answer (pinning plus a run-time fallback is fine if the reads are part of the step's input
   snapshot) and your answers on matrix rows 12 and 13: do they hold under A2 and A3?
6. @sfmskywalker: this takes over your planned amendment of decisions 4 and 5, which D-7 asked for. Do you agree, or do
   you prefer the minimal amendment under *Alternatives considered*?

## Consequences

- **Converter.** Once publish pinning (decisions 2 to 4) is implemented, the converter emits `variables.X` for
  Elsa 3 variable reads, spelled as the variable is declared, and keeps `args.X` for inputs, activity results and
  instance metadata. Converted workflows then behave like authored ones when they are edited later. To stay
  faithful, the converter checks that publish's resolution picks the same declaration as Elsa 3's resolution, and
  refuses the read when they differ. Until publish pinning lands, it keeps emitting `args.X`. Per #3010 it emits
  statement bodies as they are and no longer wraps them in `(function () { … })()`. A converted read of a variable
  that is transient in the Elsa 4 workflow is flagged at import with its own diagnostic, so the failure points at the
  Elsa 3 source rather than at publish (decision 5).
- **Runtime.** `SetReadOnlyVariables` builds `variables` from pinned parameters, not from `AmbientVariables`. The
  regular-expression read detection, the "computed access exposes everything" path and the ambient frame projection
  for scripts go. `ExpressionEvaluationRequest.AmbientVariables` either goes or is filled only from pinned values.
- **Publish and Studio.** Publish gains a JavaScript read-set check, which closes the spec 265 R-9 gap for workflows
  and graph activity bodies. Studio can show the nearest-scope resolution and flag a name that a new inner
  declaration would start to shadow; `GetShadowingWarnings` is a starting point.
- **Scoping versus ADR 0027.** The pinned executable never retargets. The authored source is name-based, though, so
  moving an activity under a container that declares the same name changes what it reads at the next publish. That
  is Elsa 3's behaviour and was chosen on purpose. Publish warns when a name resolves to a different declaration
  than in the previously published version, so the change is never silent. Explicit `args` parameters keep ADR
  0027's "invalid, not retargeted" rule.
- **Tooling (#2378, #2380).** Both are rebuilt after this ADR is accepted and drop generated-getter naming. Editor
  completions for `variables` come from the same visible-scope list publish uses.
- **ADR 0045 §5.** The script still names a value, but publish turns that name into an explicit, declared parameter,
  so no evaluator discovers values by name at run time. If the §5 review reads the rule more strictly, this ADR
  changes with it.

## Alternatives considered

**Declared parameters only (`args.X`).** Exact and already canonical. Authors, or Studio on their behalf, must
declare every read, which is more ceremony and unlike Elsa 3. Rejected as the authoring surface, kept as the
lowered form.

**Ambient only, looked up at run time (main today).** Easiest to write and closest to Elsa 3. The read set is
known only at run time and detected by regular expression, computed access over-approximates to every visible
variable, publish cannot check reads, and tooling can only guess what a script touches. Rejected. (The amendment's
run-time accessor differs: it parses rather than matches, publish validates every resolvable name, and the engine
records the actual reads.)

**Minimal D-7 amendment (considered for the 2026-10-29 amendment).** Keep decisions 2 to 4 and pin every statically
resolvable name at publish. Change decision 4 so that a computed name falls back to a run-time lookup that is reported
conservatively and whose actual reads are recorded with the input snapshot. Change decision 5 so that computed names are
no longer refused, and reword decision 4's determinism guarantee, which D-1 dropped. This satisfies D-7 and
@FransVanEk's §5 condition with the smallest change, but keeps two read mechanisms side by side. Not chosen; it is the
fallback if the amendment is rejected.

## Open questions

1. **Serialized shape.** How the compiled binding marks a parameter as exposed under `variables` rather than `args`
   (a separate map, or a per-parameter surface flag), and how its name avoids colliding with authored `args` names.
   This belongs to the implementation spec and changes spec 095's contract.
2. **Loop iteration values.** Loop iteration values (ADR 0028) are exposed today from iteration frames by key. The
   implementation spec must confirm how they are pinned.
3. **Inputs by name.** Whether `input.X` (Elsa 3's `getInput`) should get the same treatment later. Out of scope
   here.
4. **Writes.** Stay intrinsics and captures.
   [ADR 0090](0090-activities-change-variables-as-a-unit-of-work-committed-with-the-step.md) (proposed) answers
   this: input-binding expressions stay read-only, and activities change variables as a unit of work.
