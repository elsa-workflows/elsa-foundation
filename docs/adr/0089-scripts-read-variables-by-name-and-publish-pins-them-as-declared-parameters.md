# Scripts Read Variables By Name And Publish Pins Them As Declared Parameters

Status: proposed (2026-10-23, amended 2026-10-24). Sipke Schoorstra, the decision holder, chose this option on
[#3204](https://github.com/elsa-workflows/elsa-foundation/issues/3204) (US2), and on 2026-10-24 settled the
converter output and the former open questions on the Liquid root and on unset and transient variables.
Acceptance needs agreement with @j03y-nxxbz per #3204; it does not wait for the full review of
[ADR 0045 §5](0045-workflow-value-flow-uses-role-owned-bindings-and-immutable-invocation-records.md#5-make-expressions-serialized-explicit-and-pure)
(#3026), and this ADR changes with it if that review reads §5 more strictly. Its "scripts do not write variables"
is superseded by [ADR 0090](0090-activities-change-variables-as-a-unit-of-work-committed-with-the-step.md)
(proposed).

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
declaration shadows an outer one, as in Elsa 3 and most programming languages. This is the order
`ScopedVariableResolver` already computes for Studio.

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

Input-binding expressions do not write variables. Activities may change variables as described in
[ADR 0090](0090-activities-change-variables-as-a-unit-of-work-committed-with-the-step.md) (proposed); otherwise a
change goes through the `Set` intrinsic (ADR 0045 §5).

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
variable, publish cannot check reads, and tooling can only guess what a script touches. Rejected.

## Open questions

1. **Serialized shape.** How the compiled binding marks a parameter as exposed under `variables` rather than `args`
   (a separate map, or a per-parameter surface flag), and how its name avoids colliding with authored `args` names.
   This belongs to the implementation spec and changes spec 095's contract.
2. **Loop iteration values.** Loop iteration values (ADR 0028) are exposed today from iteration frames by key. The
   implementation spec must confirm how they are pinned.
3. **Inputs by name.** Whether `input.X` (Elsa 3's `getInput`) should get the same treatment later. Out of scope
   here.
4. **Writes.** Settled by [ADR 0090](0090-activities-change-variables-as-a-unit-of-work-committed-with-the-step.md)
   (proposed): input-binding expressions stay read-only, and activities change variables as a unit of work.
