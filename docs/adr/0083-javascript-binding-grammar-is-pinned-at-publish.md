# JavaScript Bindings Accept Expressions and Statement Bodies

Status: revised (2026-10-19), implementation [#3009](https://github.com/elsa-workflows/elsa-foundation/issues/3009).
Amended: new capability profile `binding-readonly-v1`, and the undefined-as-null policy. See *Amendment* below.

This replaces the unimplemented 5 August proposal for host-selected grammars in
[spec 146](../../specs/146-javascript-binding-grammar/spec.md), following the owner's request to
support either form in the same JavaScript input. It does not introduce a host setting or a second
syntax picker. [ADR 0038](0038-artifact-hash-is-purely-behavioral-and-executables-are-content-addressed.md)
still constrains runtime behavior.

## Context

Bindings previously parsed and evaluated only as strict expressions. This made `args.amount * 2`
work, but rejected `const total = args.amount * 2; return total;`. Requiring authors to write an
immediately invoked function adds unnecessary ceremony.

Replacing expression grammar with Script grammar would silently change existing bindings: bare
`{ total: 42 }` is an object expression but a labeled block under Script grammar. Existing source
must retain its meaning.

## Decision

Use one deterministic parsing rule, shared by runtime and metadata-only authoring validation:

1. Parse the complete source as a strict expression. If valid, retain expression semantics.
2. Only if expression parsing fails, parse as a strict Script with `AllowReturnOutsideFunction`.
   If valid, execute that Script once. Otherwise report the Script syntax error at its authored
   line and column.
3. Select the form before executing any authored code. Runtime errors, sandbox failures and
   result-materialization errors never trigger a retry under another grammar.

Empty and comment-only source remain missing bindings and are rejected by authoring validation and
runtime parsing. An explicitly returned empty string remains a valid value.

Statement bodies support explicit `return`, declarations, loops and conditional control flow.
They can also yield a Script completion value, as Elsa 3 does. Explicit `return` is the recommended
form because its intent is clearer. A bare object always retains expression semantics; to write a
statement body returning an object, use `return { total: 42 };`.

Both forms remain strict, use the same read-only `args` and visible-variable snapshot, and have
identical sandbox restrictions, cancellation, execution limits and JSON result materialization. They
share one capability profile, `binding-readonly-v1` (see the amendment below; originally
`binding-pure-v1`). Statements introduce local computation, not permission to change workflow
state. A result of `undefined` remains an error with guidance to produce or return a value, unless
the workflow selects the undefined-as-null policy (amendment below).

## Amendment: capability profile and undefined-as-null

**Capability profile `binding-readonly-v1` replaces `binding-pure-v1`.**
[ADR 0045 §5](0045-workflow-value-flow-uses-role-owned-bindings-and-immutable-invocation-records.md#5-make-expressions-serialized-explicit-and-pure),
as amended, no longer requires bindings to be deterministic. `binding-pure-v1` forbids time,
randomness and GUIDs by definition, so it is retired rather than redefined. A profile name must always mean the same
behaviour.
- **The new name says what still holds:** a binding reads, it never changes engine-owned workflow state.
- **It allows the native JavaScript intrinsics** `Date`, `Intl`, `Temporal` and `Math` (including `Math.random`),
  besides the registered .NET functions below; it is not limited to host functions. So a binding may use the current
  time, random values and GUIDs. Date and time-zone rules are the ADR 0046 amendment's (*Dates*).
- **Time comes from the host clock; randomness does not.** Native `Date` and the current time run on the host's
  injectable clock (`TimeProvider`), in UTC with the invariant culture, so a test can fix the time. The random source
  behind `Math.random` is the script engine's own and cannot be injected, so no random sequence is controllable: tests
  must not assume a fixed sequence.
- **The name starts a new family at v1.** The succession from `binding-pure-v1` is recorded here, not in the version
  number.
- **It also allows functions implemented in .NET** that are registered for the profile and exchange only JSON values
  with the script. A call is never refused or skipped because of what it does. No function profile is required. A
  function is effect-free unless its host declares it effectful at registration; only a declared call gets the
  commit boundary of input evaluation (ADR 0045 §5, D-6).
- **It forbids live .NET objects** while .NET interop is off, which is the default. A host may enable .NET interop
  for JavaScript through one replaceable engine strategy, provided by a feature (ADR 0046 amendment V10). It then
  applies to script activities and input-binding expressions alike: a JavaScript input expression takes the same
  input as a script in a JavaScript script activity. The guardrails are those of the ADR 0046 amendment, V10: what
  such an expression returns is a storable value or it is refused, and it never writes Elsa-owned workflow state.
  Any workflow node that uses general .NET access, whether a binding or a script activity, gets a publish-time
  warning with a stable diagnostic code and a marker on the node in Studio, so it is never switched on unnoticed
  (ADR 0046 amendment V12 gives the detail). The opt-in's implementing spec decides whether such an expression is
  treated as possibly effectful and how the executable records that it needs .NET access.
- **Spec 095's expression contract defines the profile in full.** That definition is amended alongside this ADR.

The profile is part of the expression definition, so changing it changes expression fingerprints and artifact hashes
([ADR 0038](0038-artifact-hash-is-purely-behavioral-and-executables-are-content-addressed.md)). There is no
compatibility path for `binding-pure-v1`: an executable that pins it is republished.

**Undefined-as-null.** The paragraph above keeps an `undefined` result an error by default.
[ADR 0045 §2](0045-workflow-value-flow-uses-role-owned-bindings-and-immutable-invocation-records.md#2-compile-role-specific-input-bindings),
as amended, adds a visible workflow policy, pinned in the executable.
- **Under it,** an `undefined` result materializes as `null`, and an `undefined`-valued object property is kept as
  `null` at every depth.
- **The policy applies after the form is selected and the source has run,** at the JavaScript-to-workflow boundary.
  It never changes which form is selected, the source, or how JavaScript evaluates.
- **Both forms behave the same** under it: an expression whose value is `undefined`, and a statement body that
  returns `undefined` or ends with an `undefined` completion value.
- **Array positions are preserved under either policy.** An `undefined` array element or sparse hole becomes a
  `null` placeholder with a path diagnostic, never dropped, so later indices never shift; an element already `null`
  needs no diagnostic. This holds whether or not the policy is selected, because an array's positions can never shift
  ([ADR 0045 §2](0045-workflow-value-flow-uses-role-owned-bindings-and-immutable-invocation-records.md#2-compile-role-specific-input-bindings)).

*Left to the implementing specs:*
- **renaming the profile constant** and removing the replay justification that `Inline` derives from
  `binding-pure-v1`;
- **the registration API** that marks a function as allowed in `binding-readonly-v1`;
- **the editor guidance** shown for an `undefined` result when the policy is not selected.

The JavaScript editor keeps a single language choice. The expanded editor explains that users can
write an expression or statements returning a value. Validation uses the same parser selection as
runtime and never evaluates the source. Source, cursor and selection are not rewritten.

## Examples

```javascript
args.Subtotal + args.Shipping
```

```javascript
const total = args.Subtotal + args.Shipping;
if (total < 0) return 0;
return total;
```

```javascript
const total = args.Subtotal + args.Shipping;
total // Script completion value
```

```javascript
return { total: args.Subtotal + args.Shipping };
```

## Compatibility and alternatives

Existing valid expressions keep their results, including bare object literals and anonymous
function expressions. Formerly rejected statement sources become valid. Source alone determines
which form is selected, so promotion cannot change semantics through environment configuration.
There is no grammar option to stamp, and no change to expression fingerprints.

The previous host-selected proposal added configuration, publication stamping, review metadata
and an extra dialect for authors to understand. It is superseded rather than partially implemented.
Its concern about overlapping grammars is addressed by giving expressions precedence everywhere.

An arrow-function wrapper for statement bodies was rejected because it discards Script completion
values. Executing as an expression and retrying on failure was rejected because it can run authored
operations twice. Parsing first avoids that behavior.

The separate `RunJavaScript` activity retains its existing function-body contract. Elsa 3 importer
rewriting and result type conversion are separate work.
