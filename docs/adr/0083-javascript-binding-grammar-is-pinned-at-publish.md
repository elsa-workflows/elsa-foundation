# JavaScript Binding Grammar Is Selected by Host Policy and Pinned at Publish

Status: proposed (2026-08-05)

Implementation unit: [spec 146](../../specs/146-javascript-binding-grammar/spec.md).
Constrained by [ADR 0038](0038-artifact-hash-is-purely-behavioral-and-executables-are-content-addressed.md).

## Context

A JavaScript binding expression is evaluated as a parenthesized strict-mode expression:

```csharp
result = engine.Evaluate($"\"use strict\"; ({definition.Source})");
```

`JintPortableJavaScriptEvaluator.cs:47`. The authoring validator deliberately mirrors that grammar —
`new Parser().ParseExpression(source, strict: true)` at `JavaScriptExpressionToolingProvider.cs:196` —
so the consequential-operation gate cannot approve a body Jint will reject. The two agree, and the
agreement is pinned by `ExpressionToolingProviderContractTests.cs:188`.

The consequence is that `return <expr>;` is a syntax error in a binding. That is correct for the
grammar we chose, and it is also the most common expression form in Elsa 3. Every migrating author
meets it on their first expression, and the diagnostic they get is Acornima's raw
`Unexpected token 'return' (1:1)` — accurate, and no help.

Elsa 3 achieves this without touching the author's source. It parses the expression as an
ECMAScript **Script** with one parser option set, and evaluates it directly:

```csharp
var prepareOptions = new ScriptPreparationOptions
{
    ParsingOptions = new() { AllowReturnOutsideFunction = true }
};
return Engine.PrepareScript(expression, options: prepareOptions);
```

`elsa-core/src/modules/Elsa.Expressions.JavaScript/Services/JintJavaScriptEvaluator.cs:129-138`,
evaluated at line 112. There is no wrapper and no rewriting: the source runs as authored. Two
consequences of Script semantics matter for compatibility, and both are load-bearing for real Elsa 3
expressions:

- `return <expr>;` yields that value, because of the parser option.
- A body with no `return` yields the **completion value** of its last expression statement, so
  `const total = a + b; total` is a working Elsa 3 expression.

Any migration path that admits the first but not the second is not Elsa 3 compatibility; it is a
third dialect that happens to accept `return`.

The obvious simplification — adopt Script grammar everywhere and keep one grammar — does not work,
because the two are not nested. They fork on object literals, and neither side raises an error:

| source | expression grammar | Script grammar |
|---|---|---|
| `{ a: 1 }` | object `{a: 1}` | `1` — `{` opens a *block*, `a:` is a label |
| `({ a: 1 })` | object `{a: 1}` | object `{a: 1}` |

`{ name: request.name }` is a plausible binding that means an object under one grammar and
`request.name` under the other, silently. Script grammar is therefore not a superset that could
replace the current one; moving a source between grammars can change its value with no diagnostic.
That is what forces the grammar to be recorded per expression rather than inferred from the source or
assumed from the host, and it is the reason this is a setting rather than a fix.

It also bounds what "implicit return" can be relied on. A Script yields the completion value of its
last expression statement, so `const total = a + b; total` works — but `const total = a + b;` and
`if (false) { 1 }` yield `undefined`, which the evaluator already rejects. Elsa 3 behaves the same
way; a value is not guaranteed merely because the body ran.

The obvious remedy — a host setting that switches the runtime grammar — is not available to us.
ADR 0038 states that "same hash = same behavior" is a true invariant **in both directions**, and
content-addressed promotion depends on it. A host-level grammar flag would make one `ArtifactHash`
mean one thing in staging and another in production, with no signal at the promotion boundary. The
flag must not reach the runtime.

## Decision

**Two grammars for JavaScript bindings, selected by host policy at authoring time, recorded per
expression at publish, and dispatched at runtime from the record alone.**

1. **The grammars.** `expression` (today's behavior, the default) evaluates
   `"use strict"; (<source>)`. `script` parses the authored source as an ECMAScript Script with
   `AllowReturnOutsideFunction = true` and evaluates it directly, reproducing Elsa 3 semantics
   including completion values. Under `script` the source is never wrapped, prefixed, or rewritten.

2. **The record lives in `ExpressionDefinition.Options`**, as `{"grammar":"script"}`; an absent key
   means `expression`. `Options` is already declared as evaluator options, is already part of the
   wire contract, and is already serialized into `ComputeFingerprint()`
   (`ExpressionDefinition.cs:87-94`). The evaluator's current "options must be empty" guard
   (`JintPortableJavaScriptEvaluator.cs:37-38`) relaxes to admit exactly this key and no other.

3. **The capability profile does not change.** Both grammars resolve to the same
   `ExpressionEvaluationCapabilities` grant: Script parsing admits local sequencing, not reach.
   `args` and `variables` are installed read-only, and the deterministic sandbox strips `Date`,
   `Math.random`, `crypto`, and the rest either way, so `AllowsAmbientWorkflowState`,
   `AllowsMutation`, `AllowsServiceLocation`, and `AllowsNondeterminism` all stay false and
   `IsBindingPure` stays true. Both remain `binding-pure-v1`.

4. **The host setting is an authoring-time default, never a runtime input.** A `[ManifestSetting]`
   selects which grammar newly compiled expressions are stamped with. The publish compiler writes
   that choice into each `ExpressionDefinition`. The evaluator reads only the recorded value and
   never consults configuration. Granularity is host-level; per-definition selection is not
   introduced until demand appears.

5. **Draft validation validates in the grammar the draft would compile to** — the current host
   default — so authoring diagnostics and publish outcomes cannot disagree. Under `script` the
   validator parses as a Script with the same parser option. The tooling provider, registered in
   `JavaScriptFeature` while the setting is owned by `JintFeature`, must reach the setting through a
   home both features can see.

6. **An `expression`-grammar diagnostic names the grammar.** When a source fails to parse as an
   expression but parses as a Script, the diagnostic says so and names the fix, instead of returning
   the bare parser message.

## Considered Options

**Host-level runtime flag** (the original request). Rejected: it breaks ADR 0038's biconditional.
Identical Execution Material would evaluate differently per environment, and promotion — the thing
content-addressing exists to make safe — would silently change semantics.

**Arrow-function wrapper for the second grammar** — `"use strict"; (() => { <source> })()`, the
wrapper `JintJavaScriptScriptEvaluator.cs:20` already uses for `RunJavaScript`. Rejected: it accepts
`return <expr>;` but turns a completion-value body such as `const total = a + b; total` into
`undefined`, which the evaluator then rejects. It would deliver a dialect that looks Elsa 3
compatible until an author hits the half of their expressions that have no `return`. Elsa 3's own
mechanism is the parser option, and copying it is both simpler and exactly right.

**A sibling capability profile, `binding-script-v1`.** Rejected on inspection of
`ExpressionEvaluationCapabilities`: the grant is flag-identical to `binding-pure-v1`, so a second
profile would encode a *dialect* in the field that encodes *reach*, and would need registering in
`Resolve` with duplicate flags. `Options` is the field named for evaluator options.

**Import-time lowering of `return <expr>;` to `<expr>`** in Foundation's Elsa 3 importer. Not
sufficient alone: it does nothing for hand-authored expressions or for authors typing from Elsa 3
memory, and it cannot lower a completion-value body without restructuring the source. It remains
worth doing as a complement and is listed under Follow-up. Note this is Foundation's import-side
`Elsa3ExpressionRewriter`; Elsa 3 itself performs no lowering.

**Accept the status quo and improve only the diagnostic.** Rejected as insufficient for the stated
migration goal, though the diagnostic improvement is retained above as decision point 6.

## Consequences

- Two expressions with identical source and different grammars produce different fingerprints, and
  therefore different `ArtifactHash`es. This is the intended reading of ADR 0038, not an exception
  to it: the grammar is Execution Material.
- An executable promoted into an environment with a different host default keeps its recorded
  grammar. Host configuration governs only what the next publish records.
- Flipping the host setting and re-publishing an unchanged definition mints a new hash and may
  change behavior. That is a normal publish, but it is a behavior change without a source change,
  so it must be visible in the publication review rather than discovered at runtime.
- Studio must show which grammar an expression is bound to. Once two dialects exist, identical
  sources behaving differently is otherwise unexplainable to an author.
- `Options` stops being uniformly empty for JavaScript bindings. Any consumer asserting emptiness
  must be found and updated; the evaluator guard is one such site.
- Strict mode is an open compatibility question, not a settled one. Elsa 3 evaluates non-strict;
  the `expression` grammar forces `"use strict"`. Spec 146 records the decision for `script`.
- Under `script`, an object-literal binding must be written `({ ... })` or `return { ... };`, as in
  Elsa 3. A bare `{ ... }` is a block, and its value is that of its last labeled expression. This is
  the cost of the grammar, paid only by hosts that opt in.

## Follow-up

- Decide strict mode for `script` (spec 146 open question). Full Elsa 3 fidelity implies non-strict,
  which is a real reduction in the sandbox story and should be chosen deliberately.
- Fix Foundation's `Elsa3ExpressionRewriter`, which parses with `ParseScript` and no options
  (`Elsa3ExpressionRewriter.cs:51`) and therefore cannot lower a `return`-bearing Elsa 3 expression
  at all — it fails with "JavaScript could not be parsed for safe lowering". Setting
  `AllowReturnOutsideFunction` there is required for the source to reach the analyzer regardless of
  which grammar the importer targets. No rewriter test covers a `return` source today.
- Decide whether the importer stamps `script` or lowers toward `expression`. Recorded as an open
  question in spec 146.
- `RunJavaScript`'s arrow wrapper diverges from Elsa 3 completion-value semantics in the same way
  the rejected option above does. Out of scope here; worth confirming it is intended.
