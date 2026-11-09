# Research: Scripts Read Variables By Name

**Spec**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md) · Provisional (initiative #3052)

This resolves the plan's open questions and the mechanism choices, grounded in the current code.

## R-1: Two read surfaces today, and which one we keep

**Facts.** A script today sees two roots:

- `args` — spec 095 declared parameters, each resolved to one source, type/policy-checked, pinned into the input
  snapshot; read set known at publish (`IsolatedJintEngine.SetReadOnlyArgsCore`; materialized by
  `RuntimeActivityInputMaterializer.MaterializeSnapshotAsync` + `RuntimePortableExpressionEvaluator.MaterializeParametersAsync`).
- `variables` / `getVariable` / `get<Name>()` — the issue-#984 *ambient* surface: regex-discovered per evaluation
  (`RuntimePortableExpressionEvaluator.IsAmbientVariableReferenced`, `HasComputedAmbientAccess`), projected from the
  runtime visible-variable frame chain, policy-tainted at run time; computed access over-approximates to **all** visible
  variables. spec 095 (`binding-pure-v1`) forbids this surface in pure input bindings.

**Decision.** Keep the **declared-parameter (pinned) surface** as the one mechanism for variable reads, and expose it
under a `variables` root. Remove the ambient regex/frame-projection surface for scripts (ADR 0089 decision 4 and
Consequences). The `get<Name>()` runtime surface is removed (decision 6); it survives only as a converted Elsa 3 form.

**Rationale.** Pinning gives a publish-known read set (precise dependency, protection check, tooling), avoids loading
unused/large/external variables, and keeps secrets out of sandboxes that didn't name them. Matches Sipke's minimal
direction ("keep pinning the statically resolvable reads").

**Alternatives considered.** Ambient-only (load the visible frame): rejected — read set unknown at publish, computed
over-approximation, cannot check reads. (This is also why "always load all visible" is rejected, R-4.)

## R-2 (OQ-1): Serialized shape — marking a pinned parameter as exposed under `variables`

**Decision.** Add a **per-parameter surface flag** to the compiled `Variable` parameter: `surface = variables | args`
(default `args`). One parameter list, no second map. The evaluator builds the `variables` root from parameters whose
surface is `variables`, and the `args` root from the rest. Name collisions are impossible because the roots are
separate objects; a parameter exposed under `variables` uses the variable key as its property name, independent of any
authored `args.X`.

**Rationale.** Smallest change to spec 095's compiled binding; one list keeps materialization uniform; separate roots
remove the decision-7 ambiguity (a script may read `variables.X` and `args.X` for the same value — R-6).

**Alternatives considered.** A separate `variables` parameter map (more serialized surface, duplicate materialization
paths) — rejected. Rewriting `variables.X` into `args.X` at publish (today's converter approach) — rejected: it is the
root of #3296 and conflates the two roots.

**Contract impact.** spec 095 expression contract §1 (the compiled binding gains the surface flag) and §6 (the importer
keeps `variables.X` instead of lowering it to `args.X`). Recorded as a contract change; flows to #3420 / spec 095.

## R-3 (OQ-2): Loop iteration values

**Facts.** Nearest-scope resolution already reuses `ScopedVariableResolver.Resolve`; iteration scope (ADR 0028) is the
innermost scope; spec 265 projects graph variables through synthetic frames.

**Decision.** The publish-time resolver resolves an iteration-scope variable as the nearest scope (innermost first) and
pins it as a `Variable` parameter recording the iteration scope node id + key. At run time the pinned value is taken
from the iteration frame, through the same scoped reader the runtime already has. No new surface.

**Rationale.** Reuses the existing resolver order and the runtime scoped reader; keeps iteration reads on the one
pinned path.

## R-4: Computed names — refuse at publish, defer the capability

**Facts.** A computed read (`variables[expr]`, `getVariable(expr)`) needs its value synchronously mid-script, but the
name is unknown at publish and values may be stored externally (async). The only way to serve all possible keys is to
pre-load every visible variable before evaluation — a hidden cost that compounds under repeated activity evaluation.

**Decision.** **Refuse computed variable reads at publish** with a stable diagnostic and guidance (restores ADR 0089
decision 5). Defer a real dynamic-access mechanism (e.g. an explicitly declared dynamic subset) to its own spec.

**Rationale.** Keeps the default path honest and cheap; matches faithful-or-refuse; avoids a hidden performance cliff.
Finding F-1: this collapses the #3420 amendment back to the original ADR 0089.

**Alternatives considered.** (a) Pre-load all visible for computed reads — hidden cost, rejected. (b) Allow but bound
(no large/external auto-load + loud diagnostic) — more rules and residual surprise, deferred to the dedicated spec.
(c) A Jint proxy recording actual reads — Jint's synchronous reads + async values make it unworkable and it is
unnecessary once computed reads are refused.

**Open data point.** Frequency of computed `getVariable(name)` in the real Elsa 3 corpus would quantify the conversion
cost of refusing; the private JTest corpus is unavailable locally, so this is flagged for whoever holds it.

## R-5: Publish parses, never regex

**Decision.** Publish determines the read set by parsing the source with the language parser (JavaScript parser for JS,
Liquid's parser for Liquid), replacing the runtime regex detection. The publish parser must accept the same source the
runtime parser accepts (the trailing-line-comment bug is the #3171 side-track on `main`, consumed here).

**Rationale.** spec 095 §6 already forbids regex semantic rewriting; parsing gives exact direct-name resolution and
reliable computed-access detection.

## R-6: Decision-7 ambiguity refusal is dropped

**Decision.** A script may read one value both as `variables.X` and `args.X`; the former "declare the same variable two
ways is ambiguous" refusal is removed, because the roots are separate and both resolve to the same value.

## R-7: Secrets / protection

**Decision.** A direct read of a protected variable is protection-checked at publish (the name is known). The hard
guarantee is **redaction**: secret values never appear in logs or diagnostics. A value the context has no permission to
read is not provided (access control, independent of redaction). Laundering a secret into a non-secret variable is not
hard-blocked (author accountability); diagnostics are emitted where detectable.

**Rationale.** Joey's direction: redaction + accountability over publish-blocking. With computed reads refused, every
read is a known name, so the run-time conservative-protection machinery the amendment proposed is unnecessary.

## R-8: Rename safety

**Decision.** Readable names stay in source (not internal ids). Studio updates references it can resolve on rename and
flags the rest for manual repair. Publish refuses a name that no longer resolves; a name resolving to a *different*
declaration than the previously published version is a publish warning. Published versions never change (Sipke's
direction, reviewer Q2/Q4 on #3420). With computed reads refused, the "flag computed references" case is rare.

## R-9: RunJavaScript body vs input bindings

**Decision.** Input-binding expressions read the pinned input snapshot. The `RunJavaScript` body reads through its
variable **session** view (ADR 0092), which equals the pinned snapshot until writes exist. Writes (ADR 0090 / S-7) are
out of scope; the spec records the write assumptions it relies on (writes commit at output evaluation; a computed write
to a non-existent variable refuses) to be verified against S-7. Finding F-2 flows this carve-out back to #3420.

## R-10: Converter reconciliation (spec 247)

**Decision.**

- Emit `variables.X` for Elsa 3 variable reads (`Variables.X`, `getVariable("X")`, generated `get<Name>()` via
  `Elsa3JavaScriptNames`), spelled as declared. Keep `args.X` for inputs, activity results, instance metadata.
- **#3296:** refuse (faithful-or-refuse) with guidance when a script's own declared `args` would collide with lowered
  `args` reads in the same scope (the existing guard at `Elsa3ExpressionRewriter.cs` is extended to the declared case).
  Variable reads no longer lower to `args`, so they no longer collide.
- **#3287:** give the `arguments` expression its real type from the input contract instead of the hard-coded `Object`
  (`Elsa3ValueFlowLowerer.LowerScript`). Full fix pairs with #3167 (S-0, alias inference for `JsonElement`).
- **#3010:** emit statement bodies without the IIFE wrapper on the `RunJavaScript` body path (already done for Script
  mode; confirm no regression).
- Faithfulness: refuse a read when Elsa 4's nearest-scope resolution picks a different declaration than Elsa 3's.
- A converted read of a variable that is transient in Elsa 4 is flagged at import with its own diagnostic.
