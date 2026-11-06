# DMN profile: `elsa-dmn-table-v1`

Proposed subset for [#3602](https://github.com/elsa-workflows/elsa-foundation/issues/3602). Normative basis: [OMG DMN 1.3](https://www.omg.org/spec/DMN/1.3/PDF), especially §10.3.2.10 and FEEL values/unary tests. This is not full-DMN or a conformance-level claim. Later DMN versions are not automatically admitted.

## Document and types

DMN MODEL namespace: `https://www.omg.org/spec/DMN/20191111/MODEL/`. Exactly one independent decision/row-oriented table. Optional inputData nodes describe external inputs; no dependent decisions, decision services, knowledge models, imports, contexts, invocations or boxed/literal decisions.

One to 32 input clauses, one to 16 output clauses, one to 1000 rows; exact row widths. IDs are present, globally unique and preserved. Input symbols and multiple-output names are unique and case-sensitive. Input expressions directly name an input using `[A-Za-z_][A-Za-z0-9_]*`, excluding every reserved FEEL terminal/literal name; no paths/member access/general expressions. For example an input named `true` is refused rather than exported as the boolean literal.

A single output requires its stable ID and type; its name may be omitted because the result is scalar. Canonical executable XML omits the unused single-output name. For common external-editor interchange, a present bounded nonempty single-output name is also accepted and retained in original XML; this is an explicit interoperability relaxation of DMN 1.3 §8.3.2 Table 34 (printed p.77), which forbids the name for a single output. Multiple outputs require distinct nonempty names, preserved exactly as result-object keys. This relaxation does not establish strict DMN document or conformance-level compliance.

The case-sensitive reserved-name set for this ASCII subset is: `and`, `between`, `context`, `else`, `every`, `external`, `false`, `for`, `function`, `if`, `in`, `instance`, `list`, `not`, `null`, `of`, `or`, `return`, `satisfies`, `some`, `then`, `true`. `True` and `trueValue` remain valid. This derives from §10.3.1.4 and the FEEL grammar's literal terminals; do not substitute the C# keyword set.

Types: number, string, boolean, with explicit null. Missing is an invocation error. No implicit conversions. Collection/context/custom/temporal types are unsupported. V1 lists mean unary alternatives and multi-hit results, not general list-valued inputs.

Default output entries, inputValues/outputValues constraints, priority/output-order policies and aggregation attributes are unsupported, never ignored. Absent hitPolicy means UNIQUE. Absent expression language uses FEEL; an explicit non-FEEL language is unsupported. Preserve orientation metadata; admit only row-oriented content.

## Closed grammar

Numbers are signed finite base-10 decimals, ≤34 significant digits and absolute scale ≤28. Exponents/nonfinite/out-of-range values are rejected, never rounded or converted through binary floating point. Comparison uses exact coefficient/scale arithmetic, not CLR decimal assumptions.

All API/Studio/case/history/adapter numeric values use a tagged scalar `{ "kind": "number", "value": "9007199254740993" }`, where value is exact invariant decimal text. Other scalar tags are string/boolean/null; rule-result objects/lists contain these scalars. Studio never parses a decision number into JavaScript Number or uses numeric form coercion. Core accepts exact numeric text; optional CLR integer/decimal conversion preserves exact value and rejects all Half/float/double inputs, since rounding may already have happened before arrival. Native/BPMN bindings preserve the tagged representation rather than silently converting it to a host floating-point value. Witnesses include values above 2^53 and full 34-digit precision across create/test/save/reload/history/returned results.

Strings are double-quoted Unicode scalar sequences. Supported escape spellings are exactly `\'`, `\"`, `\\`, `\n`, `\r`, `\t`, `\uHHHH` and `\UHHHHHH`; H is one ASCII hexadecimal digit, with exact digit counts and case-sensitive u/U prefixes. Decode once. Reject other escapes including JSON-only `\b`, `\f`, `\/`. Reject malformed Unicode, unescaped quotes, raw U+0000–U+001F controls, values above U+10FFFF and surrogate code points U+D800–U+DFFF. Supplementary values use literal Unicode or six-digit U escapes; FEEL surrogate escape pairs are unsupported. JSON transport is a separate encoding layer and may use a valid surrogate pair. Apply string limits after decoding; do not normalize case/Unicode. Equality compares decoded scalar sequences. The spellings derive from grammar 64 and §10.3.1.1; the stricter invalid-scalar/raw-control refusal is an explicit profile restriction.

Tokenize rather than split by punctuation: commas, brackets and `..` inside strings remain string content. Boolean/null spellings are lowercase. No host culture, clock, timezone, random, environment or function is consulted.

```text
test         := '-' | alternatives | 'not(' alternatives ')'
alternatives := atom (',' atom)*
atom         := literal | ('<' | '<=' | '>' | '>=') literal | range
range        := ('[' | '(' | ']') number '..' number (']' | ')' | '[')
literal      := number | string | boolean | 'null'
```

Square brackets adjoining a bound include it; parentheses/reversed brackets exclude it. Reject reversed endpoints. Relational comparisons/ranges apply only to numbers. A bare literal is the equality unary test; `not(literal)` expresses inequality. Leading `=`/`!=` shorthands are unsupported because they are not standard simple unary-test syntax. Equality uses typed FEEL values, so 1 and "1" differ. Verify escapes against external reference cases rather than infer C# semantics.

Output entries support scalar literals only. No arithmetic, input references, conditional expressions, functions, member access or interpolation in v1. Compile every cell, including rows that a particular invocation does not match. A wider grammar requires a reviewed new profile.

## Null and matching

Supply every declared input; omitted symbols return InvalidInput/MissingInput. Explicit null participates in tests. Reject extra keys to expose misspellings. Validate type/limits before matching.

Null equals null and differs from non-null values. Numeric ordering involving null is unknown. Alternatives use three-valued OR; negating unknown remains unknown. Only true matches a cell; all cells must be true for a row match.

The `-` wildcard follows §10.3.2.10: without inputValues it matches non-null input. Explicit `null` matches null. Blank cells are invalid, not silently rewritten to `-`. This normative distinction must be tested even where an investigated library differs.

Evaluate every row for the real matched set, including FIRST's later matched rows; do not invent outcomes for skipped tests.

## Hit policies and shape

| Policy | Result |
| --- | --- |
| UNIQUE | One matching row; multiple matches return HitPolicyViolation with rule identities. |
| ANY | All matched typed outputs must be equivalent; otherwise HitPolicyViolation. Equal matches yield one value and identify all contributing rules. |
| FIRST | First match in document order; later matches appear as matched, not selected. |
| RULE ORDER | All matches as a list in document order. |
| COLLECT, no aggregation | All matches, returned in deterministic document order; interoperability compares a multiset because DMN permits arbitrary order. |

One output column yields a scalar; multiple columns yield an object with exact output names. Multi-hit policies yield lists of row results. No match returns NoMatch with FEEL null, per §10.3.2.10; a matching literal-null row returns Success with null and rule identity. Defaults are unsupported. External tools returning an empty list instead of null are recorded as compatibility differences, not silently called equivalent.

## Bounds and outcomes

XML ≤1 MiB UTF-8, depth ≤64 and ≤100000 elements; disable DTD/external entity resolution. Input payload ≤256 KiB; cells/strings ≤4096 UTF-16 units; ≤128 alternatives per test. Dimensions above bound total cells. Compile traversal/token work has finite limits and cancellation checks. Limits are frozen in the artifact.

Meter actual cell/alternative/output/selection work. Default evaluation budget: 1000000 operations. Admission computes worst-case cost and rejects a document whose required cost exceeds configured publication ceilings. A caller may deliberately request a smaller budget and receive BudgetExceeded. Check cancellation between bounded operations; no partial successful output.

Outcomes: Success, NoMatch, InvalidInput, Unsupported, HitPolicyViolation, BudgetExceeded, Cancelled, Unavailable. Invalid/unsupported compilation returns located diagnostics and no executable. Infrastructure failures become domain exceptions, never business no-match. Bounded saved-case test batches treat BudgetExceeded and Cancelled as whole-request aborts, with no completed receipt; these two engine outcomes are not valid saved expected statuses.

## XML and external evidence

Retain original bytes/hash for unchanged export, including unsupported documents. Supported edits patch owned nodes and preserve covered namespaces, attributes, annotations and diagram/extension subtrees. Formatting/prefix changes after an edit may change source hash without changing executable meaning.

DMNDI/DC/DI and documented editor metadata may be inert. Unknown extensions are retained but not automatically certified inert: ambiguous/executable semantics block publication/evaluation until explicitly classified by the versioned profile. Refuse an edit that cannot preserve an unknown subtree safely; permitted unchanged download remains available.

Pin external editor/reference-evaluator versions. Matrix: approval boundaries; five policies; null/unknown/alternatives; escapes; numeric bounds; output/result shape; unsupported constructs; diagrams/extensions; no-match. Record standard-derived expectations and differential results, including disagreements. XML import/export alone is not evaluator conformance.
