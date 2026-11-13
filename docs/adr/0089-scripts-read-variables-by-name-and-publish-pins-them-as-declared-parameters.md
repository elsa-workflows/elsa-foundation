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

Amended: **static pinning plus a run-time fallback.** Publish still resolves and pins every variable read it can
resolve statically (decisions 1 to 4). A computed variable name and a variable read made inside an opaque host
function fall back to a lookup at run time; they are reported conservatively at publish, and the reads actually made
are recorded, checked for protection and taken into the step's input snapshot. This amends decisions 4 and 5. The
amendment also settles the rename mechanism, makes the input snapshot and the activity body's variable session
explicit, and leaves the `activities.<name>.<output>` root to its own spec. See *Amendment* below.

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
editor completions, and an exact answer to "which expressions read this variable?". *Amended (R3):* this holds for
every read publish can resolve; a computed name or an opaque host-function read is looked up at run time, recorded,
protection-checked and taken into the input snapshot. For the RunJavaScript activity **body**, reads go through its
variable session (R2).

**5. Computed names are refused at publish.** `variables[name]`, `getVariable(expr)` with a non-literal argument,
or passing `variables` or `getVariable` around as a value, is a publish diagnostic. This is the same reason the
importer refuses such reads with `VF-IMP-006`. A name that resolves to no visible variable is also a publish
diagnostic, not a run-time `undefined`. So is a read of a transient (non-durable) variable, because its value cannot
be snapshotted. *Amended (R3):* a computed name is no longer refused; it falls back to a run-time lookup. A literal
name that resolves to no visible variable, and a literal read of a transient variable, stay publish diagnostics.

**6. `get<Name>()` leaves the runtime.** It survives only as an Elsa 3 import form that the converter rewrites, using
Elsa 3's naming rule (`Elsa3JavaScriptNames`). The runtime's `ToGetterName` and the getter completions in
`JavaScriptExpressionToolingProvider` are removed, which settles the naming mismatch.

**7. `args` stays the canonical form and stays authorable.** `variables.X` is a convenience for variables; `args` is
what every parameter is underneath. Workflow inputs, activity results and instance metadata are still read through
`args`, as on main today. An author may also write `args.X` for an explicit parameter. A script must not declare
the same variable both ways; publish refuses that as ambiguous. (Exposing pinned variable reads under a `variables`
root distinct from `args`, through a per-parameter surface flag, may make this refusal unnecessary; the implementing
spec decides.)

**8. An unset variable reads as `null`.** A name that resolves to a declared variable with no value binds JSON
`null`, as Elsa 3's `getVariable` returned and as the workflow-instance binding treats an absent correlation ID. It
never binds `undefined`, which the evaluator refuses as a result. A pinned variable parameter is therefore never an
absent required parameter in spec 095's sense: it binds `null` when its variable has no value.

Writes are unchanged: scripts do not write variables. A change goes through an output capture or the `Set`
intrinsic (ADR 0045 §4). (Proposed amendment:
[ADR 0090](0090-activities-change-variables-as-a-unit-of-work-committed-with-the-step.md)
would let activities, RunJavaScript included, change variables; input-binding expressions would stay read-only.)

## Amendment: static pinning plus a run-time fallback

Decisions 1 to 3 and 6 to 8 stand. Decisions 4 and 5 are amended by R3. An earlier draft of this amendment replaced
publish-time pinning with a single run-time accessor for every read; that broad replacement is withdrawn (see
*Alternatives considered*). The amendment keeps pinning as the main mechanism and adds a narrow run-time fallback.

**R1. Rename safety.** Readable names stay in the authored script source, not stable ids. Renaming a referenced
declaration updates the references tooling can resolve; references it cannot safely update, such as computed names,
are surfaced for manual repair. A rename must not silently break a read or retarget it to another declaration.
Publish refusing a literal name that no longer resolves is the safety net, and a name that now resolves to a different
declaration than in the previously published version is a publish warning. Published workflow versions never
change; the risk is in the draft. This chooses readable source with reference updates over embedding ids; it does not
prohibit internal symbol identities.

**R2. The input snapshot versus the activity body's variable session.** The two surfaces stay distinct.
- **Input-binding expressions** read the step's input snapshot: values resolved before the step runs, pinned where
  publish resolved the name and recorded where the run-time fallback looked it up (R3). They never see a write made
  by the step they feed, pending or committed.
- **An activity body**, RunJavaScript included, reads through its variable session (ADR 0092). The session starts
  from the committed state the step was given and **shows the step's own pending writes**: a read after a write in
  the same step returns the written value (read-your-writes, ADR 0090), although the write is applied only when the
  step commits. No other step sees those pending writes, and they are discarded if the step does not commit.

So decision 4's "run time sees only declared values" describes input-binding expressions. The body is not limited to
a frozen snapshot once it can write.

**R3. Computed and opaque reads fall back to a run-time lookup.** Pinning stays the mechanism for every name publish
can resolve: `variables.X`, `variables['X']` and `getVariable('X')`. Two kinds of read cannot be resolved at publish:
a **computed name** (`variables[expr]`, `getVariable(expr)` with a non-literal argument, or `variables` or
`getVariable` passed around as a value), and a variable read made inside an **opaque host function** whose reads
publish cannot see. For these:
- **Publish reports them conservatively.** A binding or body that contains one is reported as possibly reading any
  variable visible to it, so dependency questions ("which expressions read this variable?", concurrent-write
  analysis) over-approximate rather than miss a read.
- **Run time looks the name up** in the node's visible scopes, nearest first, by the same rule as decision 2.
- **The actual reads are recorded and protection-checked.** Each looked-up read passes the same type, policy and
  protection checks as a pinned parameter, is recorded in the read set (ADR 0092 decision 3), and becomes part of the
  step's input snapshot. A replayed or resumed step therefore sees the same values, as it does for pinned reads.
- **A computed name that matches no visible variable reads `undefined`.** A warning is recorded and the read is marked
  absent. It is not `null`, which would erase the difference between a variable that is null and one that does not
  exist. This applies to variable names only; it does not make other missing values optional.

A literal name that resolves to no visible variable, and a literal read of a transient variable, stay publish
diagnostics (decision 5).

The fallback has a cost: a script reads synchronously, and a variable's value may be stored externally and loaded
asynchronously. How the lookup is served, by loading the conservatively reported set before evaluation or by loading
on demand, is for the implementing spec. The cost falls only on a binding or body that uses a computed name or an
opaque host function; a script whose reads all resolve at publish materializes only its pinned variables.

**R4. The `activities.<name>.<output>` root is left to its own spec.** An earlier draft of this amendment added an
`activities.<name>.<output>` script root with its own occurrence-resolution rules. Spec 265 has no such JavaScript
root (body output reads go through the `ActivityResult` binding and the `getOutput` lowering), and it is new surface
beyond variable reads. This ADR covers **variable** reads. `input.X` and `workflow.*` keep reading as they do today
(decision 7); whether `input.X` should later be resolved and pinned by name is out of scope.

**What this touches:**
- **Spec 095.** A per-parameter **surface flag** (`variables` | `args`, default `args`) exposes a pinned `Variable`
  parameter under a `variables` root distinct from `args`, so the roots are separate objects and a `variables` name
  cannot collide with an authored `args` name (expression contract §1 and §6). Converted variable reads are no longer
  lowered to `args`. The contract also gains the run-time fallback of R3 for computed and opaque reads.
- **ADR 0045 §5.** Satisfied: a name publish can resolve becomes an explicit, declared parameter; a computed or opaque
  read is an explicit, conservatively reported fallback whose actual reads are recorded and checked, and become part
  of the step's input snapshot. No evaluator discovers values silently.
- **ADR 0045 §2.** A computed name that matches nothing reads `undefined` with the read marked absent, as R3 states.
- **ADR 0092 decision 3.** Pinned reads and the actual reads of the fallback are recorded in the read set.
- **ADR 0090 §3.** Its refusal of computed names follows this amendment for reads: a computed read in the body session
  falls back as in R3. A computed write stays refused.

## Consequences

- **Converter: static pinning plus dynamic fallback.** Once publish pinning (decisions 2 to 4) is implemented, the
  converter emits `variables.X` for Elsa 3 variable reads with a literal name, spelled as the variable is declared,
  and publish pins them. It keeps `args.X` for inputs, activity results and instance metadata. Converted workflows
  then behave like authored ones when they are edited later. To stay faithful, the converter checks that publish's
  resolution picks the same declaration as Elsa 3's resolution, and refuses the read when they differ. An Elsa 3
  computed read, such as `getVariable(name)` with a non-literal argument, is emitted as written and falls back to the
  run-time lookup of R3; the importer no longer refuses it (`VF-IMP-006` no longer applies to variable reads). The
  converter refuses (faithful-or-refuse), with guidance, when a script's own declared `args` would collide with
  lowered `args` reads, and it gives the RunJavaScript `arguments` expression its real result type instead of a
  hard-coded `Object`. It emits statement bodies as they are and no longer wraps them in `(function () { … })()` on
  the body path. A converted literal read of a variable that is transient in the Elsa 4 workflow is flagged at import
  with its own diagnostic, so the failure points at the Elsa 3 source rather than at publish (decision 5).
- **Runtime.** `SetReadOnlyVariables` builds `variables` from pinned parameters, not from `AmbientVariables`. The
  regular-expression read detection and the ambient frame projection for scripts go. The "computed access exposes
  everything" path is replaced by the recorded run-time lookup of R3, which records only the reads actually made.
  `ExpressionEvaluationRequest.AmbientVariables` either goes or is filled only from pinned values and fallback
  lookups. A script whose reads all resolve at publish materializes only the variables it names.
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
variable, publish cannot check reads, and tooling can only guess what a script touches. Rejected.

**Broad run-time accessor (an earlier draft of this amendment).** Replace pinning entirely: publish only validates
names, and every read, literal or computed, goes through one run-time accessor that records the actual reads.
Withdrawn: it gives up the exact read set, exact editor completions and exact dependency answers for the common case
of literal names, to serve the rare computed one. It would also have let scripts read transient variables. The
chosen amendment keeps pinning for every name publish can resolve and uses a run-time lookup only where it cannot.

**Pinning only, computed reads refused at publish (the original decisions 4 and 5).** Keep one mechanism: pin every
name and refuse every computed name and opaque read at publish, leaving dynamic access to a later spec. It avoids the
fallback's cost and the second read path. Not chosen: Elsa 3 scripts can read a variable by a computed name, and a
converted workflow that does would be refused at import and could not run in Elsa 4 at all. It would also
drop the agreed run-time rule that a computed name matching nothing reads `undefined` with a warning. The fallback's
cost is confined to the bindings and bodies that use a computed or opaque read, and its reads are still recorded,
checked and snapshotted, so it keeps the guarantees ADR 0045 §5 asks for.
