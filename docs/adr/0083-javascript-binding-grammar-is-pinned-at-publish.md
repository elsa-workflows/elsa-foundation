# JavaScript Bindings Accept Expressions and Statement Bodies

Status: revised (2026-10-19), implementation [#3009](https://github.com/elsa-workflows/elsa-foundation/issues/3009).

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

Statement bodies support explicit `return`, declarations, loops and conditional control flow.
They can also yield a Script completion value, as Elsa 3 does. Explicit `return` is the recommended
form because its intent is clearer. A bare object always retains expression semantics; to write a
statement body returning an object, use `return { total: 42 };`.

Both forms remain strict, use the same read-only `args` and visible-variable snapshot, and have
identical deterministic sandbox restrictions, cancellation, execution limits and JSON result
materialization. They retain the `binding-pure-v1` capability profile. Statements introduce local
computation, not permission to change workflow state or access services. A result of `undefined`
remains an error with guidance to produce or return a value.

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
