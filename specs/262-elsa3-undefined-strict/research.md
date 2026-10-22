# Research: Elsa 3 converter: undefined results and strict mode

Sources: `Extensions/Elsa3/src/Mapping/Services/Elsa3ExpressionRewriter.cs` (the converter), Elsa 4's
`JintPortableJavaScriptEvaluator` (bindings), `JintJavaScriptScriptEvaluator` (RunJavaScript) and
`JavaScriptBindingSyntax` (grammar choice, ADR 0083), all on `main` at `d628fc0da`. Acornima 1.6.1, Jint 4.9.1.
Corpus numbers are from the baseline in the spec.

## R-1 Where `undefined` leaves a converted expression

**Elsa 4.** A binding is evaluated by `JintPortableJavaScriptEvaluator`. `JavaScriptBindingSyntax.IsExpression` first
tries a strict expression parse. If it succeeds, the source runs as `(\n<source>\n)`; otherwise it runs as a strict
script with top-level `return` allowed. An `undefined` result throws ("A JavaScript binding must produce a value…").
The converter's Expression-mode output always parses as an expression: it is either the original single expression or
`(function () {\n…\n})()`.

**Where the value leaves.** In converter output, the result of an Expression-mode source is one of:
1. the single expression itself (the `body.Count == 1` path);
2. the trailing expression of a statement-form source, which the converter prefixes with `return` inside the function;
3. the argument of a top-level `return` (spec 247 R-1), inside the same function;
4. a bare top-level `return;`, which spec 247 already rewrites to `return null;`.

A `return` inside a function the source declares returns to that function's caller, not out of the expression, so it is
not a place where the result leaves.

**Decision.** Wrap each of 1–3 as `(<expr>) ?? null`. Case 4 is unchanged.
- **Why faithful.** `??` replaces only `null` and `undefined`, so every other value, including `0`, `""`, `false` and
  `NaN`, is unchanged, and `null` stays `null`. Elsa 3's `ToObject()` turned `undefined` into `null`, and the Elsa 4
  result is JSON, which has no `undefined`. So `null` and `undefined` cannot be told apart in the result.
- **Why the parentheses.** `a || b ?? null` and `a && b ?? null` are SyntaxErrors: `??` cannot mix with `||`/`&&`
  unparenthesized. A comma expression would otherwise bind only its last operand (`a, b ?? null`). Parentheses keep the
  original expression one unit for every operator (FR-002).
- **Evaluation.** `??` evaluates its left side once, so getters and side effects run exactly as before. Jint 4.9 supports
  `??`, and so does Acornima's default ECMAScript version.

**Corpus.** 16 expressions in 10 definitions fail today with the `undefined` error when a request value is missing.
Each is case 1 or 3, for example `return getHttpRequestBody().subWorkflowDefinitionId;`.

**Alternatives rejected.**
- Wrap only expressions that could be `undefined`. A member read, a call or an identifier can always be `undefined`, so
  this would skip only literals and arithmetic. That buys nothing faithful and adds a classifier.
- Have Elsa 4 turn `undefined` into `null` for converted definitions. ADR 0083 keeps it an error (spec boundary).
- `|| null`. This changes `0`, `""` and `false`.

## R-2 Applying the wrap with source edits

The converter edits source by parser ranges (`SourceEdit`), applied in descending start order. `OrderByDescending` is
stable, so for edits with the same start they apply in the order they were added.

**Decision.**
- Insert `(` at the expression's start and `) ?? null` at its end. For case 2, insert `return (` instead of today's
  `return `. For case 3, insert at the `return` argument's start and end.
- The wrap's edits are added after the analyzer's edits. A replacement such as `getVariable('o')` → `args.o` can start at
  the same offset as the inserted `(`. Applied first, the replacement leaves the `(` in front of the replaced text. A
  zero-width insertion applied before a replacement at the same offset would be overwritten.
- A trailing line comment: in case 1 the existing code drops the statement semicolon and appends `\n` when a `//`
  follows the expression. The `) ?? null` goes at the expression's end, before any comment, so nothing changes there.
  Cases 2 and 3 are inside the function, whose closing `\n})()` already starts on its own line.

The spec 247 test `return 'a' // trailing comment` covers the comment case; a unit test pins the same-offset case
(`getVariable('o').x`).

## R-3 Strict parse after rewriting

**Elsa 4 parse, per mode.**
- **Expression mode:** the source is a strict expression (R-1). If the strict expression parse fails, Elsa 4 tries a
  strict script parse instead. That runs the source with statement semantics the converter did not intend, or fails.
- **Script mode** (RunJavaScript): Elsa 4 evaluates `"use strict"; (() => { <source> })()`, on one line.

**Decision.** After rewriting, parse the converter's output as Elsa 4 will see it, and refuse on failure:
- **Expression mode:** `new Parser().ParseExpression(source, strict: true)`. A failure is refused even if a strict script
  parse would succeed, because Elsa 4 would then run the source with different semantics (the statement path).
- **Script mode:** `new Parser(new ParserOptions { AllowReturnOutsideFunction = true }).ParseScript($"(() => {{ {source} }})()", strict: true)`.
  This is the same text Elsa 4 builds, with strict mode passed as an option instead of the `"use strict"` prologue.
- **The message** carries Acornima's description, for example "Strict mode code may not include a with statement" or
  "Octal literals are not allowed in strict mode".

This one check covers `with`, legacy octal literals and escapes, duplicate parameter names, `delete` of a plain name,
reserved words used as names (`implements`, `interface`, `let`, `package`, `private`, `protected`, `public`, `static`,
`yield`), and `eval`/`arguments` as binding names. Running it on the converter's output also catches anything the
converter itself introduces (spec edge case).

**Order.** A source that fails the sloppy parse keeps today's parse refusal (US2 scenario 3). The strict check runs only
on a source that rewrote successfully. If the analyzer has already refused, its issues are reported and the strict check
is skipped, because no output exists. A strict failure and an analyzer refusal therefore never appear together, and that
is acceptable: the author fixes the analyzer refusals and converts again.

**Corpus.** 119 of 121 rewritten sources pass. The 2 failures are the trailing-comment scripts (R-5), not strict mode.

**Why not reuse `JavaScriptBindingSyntax`.** It is `internal` to `Elsa.Expressions.JavaScript`, and the converter does
not reference that project. The rule is two lines, so copying it is cheaper than a new dependency (framework §2.17,
duplication beats dependency).

## R-4 Assignment to an undeclared name

**Elsa 3.** In sloppy mode, assigning to an unresolvable name creates a property on the global object. Reading it before
the first assignment throws a ReferenceError.

**Elsa 4.** In strict mode, the assignment throws a ReferenceError when it runs. It is never a parse error, so R-3 cannot
see it.

**Decision.** The analyzer already tracks declared names per scope (`IsDeclared`). That tracking covers `var` hoisted to
its function or script, `let`/`const`/class/function per block, parameters, catch variables and loop heads. It now
refuses a write to a name that is not declared:
- `AssignmentExpression` whose left side is an `Identifier`, with any operator (`=`, `+=`, `??=`, …), or a destructuring
  pattern (`ArrayPattern`/`ObjectPattern`, including defaults and rest) that binds such a name;
- `UpdateExpression` (`++`/`--`) on an `Identifier`;
- `ForInStatement`/`ForOfStatement` whose left side is an `Identifier` or a pattern rather than a declaration.

The existing ambient-mutation refusal (`variables`, `args`, values read through `getVariable`/`getInput`) runs first and
keeps its own diagnostic. Names declared later in the same scope count as declared, as in JavaScript. For `let`/`const`
that is the temporal dead zone, where Elsa 3 also throws.

**Writes to global names.** `undefined = 1`, `NaN = 1` and `JSON = …` count as undeclared. In strict mode, assigning to a
non-writable global throws, and the others replace a sandbox global. Neither is what Elsa 3 did.

**Residual risk, not refused.** A function declared inside a block is visible to the whole function in sloppy mode
(Annex B), but only inside its block in strict mode. A *call* to it from outside the block is already refused as an
unknown host function, because the analyzer scopes it to the block. A *bare reference* outside the block would throw in
Elsa 4. This is rare and the corpus has none, so it is recorded here rather than given a rule.

**Corpus.** 0 uses. Every corpus assignment targets a declared `var`.

## R-5 A RunJavaScript body that ends in a line comment

Elsa 4's `JintJavaScriptScriptEvaluator` builds `(() => { <source> })()` on one line. When the source's last line is a
`//` comment, the comment swallows ` })()` and the script does not parse, whatever the mode. Elsa 3 ran the body as a
script, so the comment was harmless. The corpus has 2 such scripts.

**Decision.** In Script mode, append `\n` to the converter's output when its last line contains `//`. This
over-approximates: a `//` inside a string literal on the last line also gets the newline, which is harmless. It is cheaper
than asking the parser for comment positions and leaves every other script byte-identical. R-3's Script-mode check
parses exactly Elsa 4's wrapper text, so it would catch any case this rule misses.

**Follow-up.** The wrapper itself is an Elsa 4 bug that affects native scripts too. It gets its own issue, drafted for the
maintainer (spec FR-014).

## R-6 `this` and `arguments`

**Elsa 3.** `this` at the top level, and in a function called without a receiver, is the engine's global object, which
carries Elsa 3's host functions and any implicit globals. `arguments` in a sloppy function with simple parameters is
linked to them: writing `arguments[0]` changes the first parameter and the other way round. `arguments` outside every
function does not exist and throws a ReferenceError.

**Elsa 4.**
- `this`: a single expression runs at the top level of a strict script, so `this` is the sandbox's global object, which
  differs from Elsa 3's. Inside the converter's `(function () { … })()` wrapper, `this` is `undefined`. In Script mode it
  is the sandbox global, through the arrow function.
- `arguments`: strict functions keep no link between `arguments` and parameters. At the top level of an Expression-mode
  source wrapped in the function, `arguments` is that function's empty arguments object, not a ReferenceError.

**Decision** (clarification Q2: refuse what a static check proves).
- Refuse `ThisExpression` outside every non-arrow function. Class bodies count as a function context, because field
  initializers and static blocks have their own `this`.
- Refuse, inside a non-arrow function, a write to `arguments[…]` (assignment or update whose target is a member of
  `arguments`), or a write to one of the function's own parameters, when the function's body (including nested arrow
  functions, which share its `arguments`) reads `arguments`. This deliberately over-approximates: it also refuses a
  function whose non-simple parameter list means sloppy mode would not have linked them. That shape is rare, and the
  rule stays one sentence.
- Refuse `arguments` outside every non-arrow function, when it is not declared. Elsa 3 threw a ReferenceError there,
  while the wrapped Elsa 4 source returns an empty object. This is a provable case of the same kind, so it falls under
  Q2's answer. The spec's FR-013 is widened in this unit to say so.
- Leave alone: `this` inside a non-arrow function (whether it is called as a method is not static), and `arguments` that
  is only read. The README documents the `this` difference (FR-010).

**Corpus.** 0 uses of either.

## R-7 Diagnostic codes

**Decision.** All new refusals use `VF-IMP-007` (`UnsupportedShape`). They are sloppy-mode shapes Elsa 4 cannot run the
same, which matches the code's existing use for sources the converter cannot lower ("could not be parsed for safe
lowering", "has no expression"). No code is added, removed or renamed (spec assumption). The messages and guidance are
pinned in [contracts/javascript-strict-and-undefined.md](contracts/javascript-strict-and-undefined.md).

**Alternatives rejected.**
- `VF-IMP-005` (`ForbiddenCapability`) for undeclared assignment, as a mutation. It is about mutating workflow state,
  and its guidance (Set Variable) would mislead here.
- A new code. It changes the stable code list and the importer's diagnostic contract for no gain in actionability: the
  message and guidance already say what to change.

## R-8 `null` bound to a typed, non-nullable input (clarification Q4)

**Elsa 3** (3.5.3; the same lines in 3.6.3):
- `JintJavaScriptEvaluator` returns `ToObject()` and converts it with `ConvertTo(returnType)`. `ObjectConverter` returns
  `null` for a `null` value, whatever the target type.
- The input's memory block stores `null`. The activity reads it with `context.Get(input)`, which returns
  `value != null ? value.ConvertTo<T>() : default` (`ActivityExecutionContext.cs` L740 in 3.5.3).
- So a `null` result quietly becomes `default(T)`: `0` for `Int32`, `false` for `Boolean`, the zero member for an enum.
  A string stays `null`.

**Elsa 4** (`main`):
- A `null` binding result becomes JSON `null` (`JintResultMaterializer`).
- `RuntimeActivityInputMaterializer.ValidatePresence` then throws `VF-ACT-004` ("does not accept null or absence") for an
  input that is not nullable. `ActivityInputHydrator` has a second guard.
- Nullability is published from CLR metadata (`ClrAssemblyScanner`): a value type that is not `Nullable<T>` is not
  nullable, and a reference type is not nullable only when it is annotated not-null.
- There is no publish-time check, because the value is only known at run time.
- `ValueDurabilityPolicyTests.Snapshot_materialization_rejects_nullish_value_for_non_nullable_contract` pins the refusal.

| Input type | Elsa 3 | Elsa 4 |
|---|---|---|
| `Int32`, `Double`, `Boolean`, a non-nullable enum | `default(T)` | `VF-ACT-004` at run time |
| `string` annotated not-null | `null` | `VF-ACT-004` at run time |
| `string` without annotation, `object`, nullable types | `null` | `null` |

**What this means for this unit.** D-1 is still faithful at the binding: the expression gives `null`, as in Elsa 3. When
that `null` reaches a non-nullable value-type input, Elsa 4 now fails later, at the input, with `VF-ACT-004` instead of
the `undefined` error, where Elsa 3 used `default(T)`. This is not a regression: the definition failed at run time before
this unit too. The gap is not specific to the rewrite either: any `null` literal, `null` variable or JavaScript `null`
reaching such an input behaves the same. And it is data-dependent, so the converter cannot refuse it. Per clarification
Q4, it becomes its own Elsa 4 issue, drafted and shown to the maintainer before filing (D-9). The README's migration story
names it as a remaining difference (FR-010).
