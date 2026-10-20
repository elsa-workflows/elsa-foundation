# Tasks: Elsa 3 converter: undefined results and strict mode

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md),
[contracts/javascript-strict-and-undefined.md](contracts/javascript-strict-and-undefined.md), [quickstart.md](quickstart.md)

**Tests**: required. Every story changes logic-bearing conversion behaviour (framework §2.23). US1 is proven by evaluating
converted expressions in the real Elsa 4 evaluator (`PortableJavaScript`). US2–US4 need a refusal test per refused
construct and a conversion test per counterpart that still converts. Write each story's tests first and run them on
unchanged code to confirm they fail for the expected reason. That is working practice, not a TDD cadence declaration (the
Elsa constitution declares none).

All production changes are in `Extensions/Elsa3/src/Mapping/Services/Elsa3ExpressionRewriter.cs`, so the stories run
in sequence. Build and test only `Extensions/Elsa3/tests/Mapping/Tests/Elsa3.Mapping.Tests.csproj` (shared machine).

## Phase 1: Setup

- [x] T001 Create the worktree `../w3012` on `agent/claude/3012-elsa3-undefined-strict` from `origin/main`, and claim #3012 (done during specify)
- [x] T002 Merge `origin/main` into the branch (no rebase), then build `Extensions/Elsa3/tests/Mapping/Tests/Elsa3.Mapping.Tests.csproj` and run it once to record the green baseline

## Phase 2: Foundational

- [x] T003 Add two message/guidance factories to the rewriter, both under `Elsa3ImportDiagnosticCodes.UnsupportedShape` (`VF-IMP-007`), with the guidance text exactly as in the contract, in `Extensions/Elsa3/src/Mapping/Services/Elsa3ExpressionRewriter.cs`:
  - a private static `StrictModeIssue(string reason)` for the strict-parse refusal;
  - private constants or helpers for the undeclared-write, `this`, `arguments`-write and top-level `arguments` refusals.

## Phase 3: User Story 1 - A converted expression whose value is undefined gives null (P1)

**Goal**: in Expression mode, every value that leaves the converted expression is `(<expr>) ?? null` (D-1, D-2).

**Independent test**: `Elsa3UndefinedResultTests` passes. Each converted expression evaluates in `PortableJavaScript`
to Elsa 3's result.

- [x] T004 [US1] Create `Extensions/Elsa3/tests/Mapping/Tests/Elsa3UndefinedResultTests.cs`. Follow `Elsa3TopLevelReturnTests`: convert with `ConvertTextAsync`, evaluate with `PortableJavaScript.EvaluateAsync`, compare `GetRawText()`. Add a theory over single expressions, statement forms and top-level returns:
  - a missing property: `({}).discount`, and `getVariable('order').discount` with `args.order = {}`, giving `null`;
  - a function that falls through: `(function () {})()`, and `function f() {}\nf()`, giving `null`;
  - `void 0`, giving `null`;
  - `a || b` where both are falsy-undefined (`({}).a || ({}).b`), giving `null`, and `0 || 'x'`, giving `"x"`;
  - a comma expression `(1, ({}).a)`, giving `null`, and `1, 2`, giving `2`;
  - explicit `null`, giving `null`;
  - `0`, `""` and `false`, kept as is;
  - an object and an array, unchanged;
  - `if (true) { return ({}).a }\nreturn 1`, giving `null`;
  - `var x = ({}).a;\nx`, giving `null`;
  - `return 'a' // trailing comment`, and `({}).a // trailing comment`, giving `"a"` and `null`.
- [x] T005 [US1] In `Elsa3UndefinedResultTests.cs`, add facts that pin the source shape:
  - `getVariable('o').x` becomes exactly `(args.o.x) ?? null`. This covers the same-offset edit order of D-2.
  - A statement form ends with `return (<e>) ?? null`.
  - A nested `function (x) { return x * 2 }` keeps its `return` unchanged.
  - A RunJavaScript body (Script mode) gets no `?? null`.
- [x] T006 [US1] Run T004–T005 on unchanged code. Confirm that the `undefined` cases fail with "A JavaScript binding must produce a value", and that the shape facts fail.
- [x] T007 [US1] Implement D-1/D-2 in `RewriteJavaScript` in `Extensions/Elsa3/src/Mapping/Services/Elsa3ExpressionRewriter.cs`:
  - **Single-expression path:** insert `(` at `last.Expression.Start` and `) ?? null` at `last.Expression.End`.
  - **Statement path:** change the inserted `return ` to `return (`, and insert `) ?? null` at `last.Expression.End`.
  - **Top-level-return path:** in Expression mode only, add both edits around every `ReturnStatement.Argument`, and around a trailing `ExpressionStatement`. The bare `return;` → `return null;` edit stays.
  - **Order:** add all wrap edits after `analyzer.Edits`.
  - Update the XML doc on `Rewrite`/`RewriteJavaScript` to say where `?? null` is applied.
- [x] T008 [US1] Update the exact expected sources that now carry `?? null`, changing nothing else:
  - `Extensions/Elsa3/tests/Mapping/Tests/Elsa3TopLevelReturnTests.cs`;
  - `Extensions/Elsa3/tests/Mapping/Tests/Elsa3JavaScriptConversionTests.cs`;
  - `Extensions/Elsa3/tests/Mapping/Tests/Elsa3GeneratedAccessorTests.cs`;
  - `Extensions/Elsa3/tests/Mapping/Tests/Elsa3JavaScriptHostFunctionTests.cs`;
  - `Extensions/Elsa3/tests/Mapping/Tests/Elsa3MemoryReferenceImportTests.cs`, only where the asserted source is JavaScript.

  Script-mode expectations (`Text(Input(node, "script"))`) stay unchanged. Then run the whole Mapping test project.

## Phase 4: User Story 3 - An assignment to an undeclared name is refused (P1)

**Goal**: a write to a name no enclosing scope declares is refused with `VF-IMP-007` (D-4).

**Independent test**: the undeclared-write tests in `Elsa3StrictModeTests` pass, and each declared counterpart converts.

- [x] T009 [US3] Create `Extensions/Elsa3/tests/Mapping/Tests/Elsa3StrictModeTests.cs`, with a summary that names spec 261. Add a refusal theory, in both Expression mode (`ConvertTextAsync`) and Script mode (a RunJavaScript body, as in `Elsa3JavaScriptConversionTests`). Assert `VF-IMP-007` and that the message names the variable. Cases:
  - `total = 1; total`;
  - `total += 1; 1`;
  - `total++; 1`;
  - `[a, b] = [1, 2]; a`;
  - `({ a } = { a: 1 }); a`;
  - `for (k in { x: 1 }) {} 1`;
  - `for (k of [1]) {} 1`;
  - `(function () { total = 1; })(); 1`;
  - `undefined = 1; 1`.
- [x] T010 [US3] In `Elsa3StrictModeTests.cs`, add a conversion theory for the declared counterparts:
  - `var total; total = 1; total`;
  - `let total = 0; total += 1; total`;
  - `total = 1; var total; total` (hoisting);
  - `function f(total) { total = 2; return total } f(1)`;
  - `try { throw 1 } catch (e) { e = 2 } 1`;
  - `for (var k in { x: 1 }) {} k`;
  - `const o = {}; o.total = 1; o.total`, a property write.
- [x] T011 [US3] Run T009–T010 on the US1 code and confirm the refusal cases convert today, which is the gap.
- [x] T012 [US3] Implement D-4 in `JavaScriptAnalyzer.VisitNode` in `Extensions/Elsa3/src/Mapping/Services/Elsa3ExpressionRewriter.cs`, after the ambient-mutation check:
  - For `AssignmentExpression`, collect the identifiers its `Left` binds. Use the `CollectBindings` shape for `ArrayPattern`/`ObjectPattern`/`AssignmentPattern`/`RestElement`; an `Identifier` binds itself, and member targets are skipped.
  - For `UpdateExpression`, take its `Identifier` argument.
  - For a `ForInStatement`/`ForOfStatement` whose `Left` is not a `VariableDeclaration`, collect from `Left`.
  - Add one issue per distinct name that is not `IsDeclared`, then keep visiting the children.

## Phase 5: User Story 2 - Sloppy-only JavaScript is refused (P1)

**Goal**: the output is strict-parsed exactly as Elsa 4 sees it (D-3), and `this`/`arguments` cases are refused (D-5).

**Independent test**: the strict-mode tests in `Elsa3StrictModeTests` pass; sources valid in both modes convert unchanged.

- [ ] T013 [US2] In `Elsa3StrictModeTests.cs`, add a strict-parse refusal theory, in Expression and Script mode. Assert `VF-IMP-007` and that the message carries the parser's reason. Cases:
  - `with ({ a: 1 }) { a }`;
  - `010 + 1`;
  - `'\\010'`;
  - `(function (a, a) { return a })(1, 2)`;
  - `var x = 1; delete x; x`;
  - `var static = 1; static`;
  - `var yield = 1; yield`;
  - `var arguments = 1; arguments`, where strict mode forbids `arguments` as a binding name.

  Add one fact that a sloppy parse failure (`return (`) still reports the existing "could not be parsed" refusal and not the strict-mode one.
- [ ] T014 [US2] In `Elsa3StrictModeTests.cs`, add `this`/`arguments` tests. Assert `VF-IMP-007` for the refusals:
  - **Refused:**
    - `this.x`;
    - `(() => this)()`;
    - `arguments.length`;
    - `(function (a) { arguments[0] = 2; return a })(1)`;
    - `(function (a) { a = 2; return arguments[0] })(1)`;
    - `(function (a) { const f = () => arguments[0]; a = 2; return f() })(1)`.
  - **Converting:**
    - `({ v: 1, f() { return this.v } }).f()`;
    - `(function () { return this })`, a function value with `this` inside;
    - `(function () { return arguments.length })(1, 2)`;
    - `(function (a) { a = 2; return a })(1)`.
- [ ] T015 [US2] Run T013–T014 on the current code and confirm the gaps.
- [ ] T016 [US2] Implement D-3 in `RewriteJavaScript` in `Extensions/Elsa3/src/Mapping/Services/Elsa3ExpressionRewriter.cs`. Route every `Success` through one private `StrictCheck(ExpressionDefinition definition, Elsa3JavaScriptMode mode)`:
  - **Expression mode:** `new Parser().ParseExpression(source, strict: true)`.
  - **Script mode:** `new Parser(new ParserOptions { AllowReturnOutsideFunction = true }).ParseScript("(() => { " + source + " })()", strict: true)`.
  - **On `ParseErrorException`:** return `Failure(StrictModeIssue(exception.Description))`.
  - Keep `CompletionValueLost` on success.
- [ ] T017 [US2] Implement D-5 in `JavaScriptAnalyzer` in `Extensions/Elsa3/src/Mapping/Services/Elsa3ExpressionRewriter.cs`:
  - In `Visit`, keep a stack of enclosing non-arrow function contexts. Push for an `IFunction` that is not an `ArrowFunctionExpression`, and for a `ClassBody`.
  - Each context records its parameter names, whether its body (including nested arrows) reads `arguments`, and its pending writes to parameters or `arguments[…]`.
  - Refuse `ThisExpression`, and an `Identifier` `arguments` that is not `IsDeclared`, when the stack is empty.
  - When a context pops, refuse its pending writes if it read `arguments`.

## Phase 6: User Story 4 - A RunJavaScript body that ends in a line comment still runs (P2)

**Goal**: D-6.

**Independent test**: a script ending in `// comment` converts and runs in Elsa 4's script evaluator.

- [ ] T018 [US4] In `Elsa3StrictModeTests.cs`, add a fact. A RunJavaScript body `var x = 1; x + 1 // Expected output: 2` converts, its script ends with `\n`, and Elsa 4's script evaluator runs it to `2`. Run it through `JintJavaScriptScriptEvaluator` with a helper beside `PortableJavaScript` in `Extensions/Elsa3/tests/Mapping/Tests/Support/`, or through the existing script test path if there is one. Also add a fact that a body without a trailing comment is byte-identical to before.
- [ ] T019 [US4] Implement D-6 in the two Script-mode `Success` paths of `RewriteJavaScript` in `Extensions/Elsa3/src/Mapping/Services/Elsa3ExpressionRewriter.cs`. Append `\n` when the last line of the output contains `//`, before `StrictCheck` runs.

## Phase 7: Polish & cross-cutting

- [ ] T020 Update "Migrating Elsa 3 JavaScript" in `Extensions/Elsa3/src/Mapping/README.md` (FR-010):
  - **What converts:** add an `undefined` result row (the result becomes `null`).
  - **What is refused:** add rows for sloppy-only syntax, an undeclared write, top-level `this`/`arguments` and `arguments` aliasing, all `VF-IMP-007`, with the contract's guidance.
  - **Known differences:** replace that paragraph with the remaining ones: `this` in a plainly called function; a nested `undefined` key dropped from an object result; a `null` result bound to a non-nullable input (Elsa 3 used the default, Elsa 4 refuses with `VF-ACT-004`); frozen values.
- [ ] T021 Add a "Behaviour" note to `specs/247-elsa3-js-host-functions/contracts/javascript-conversion.md` pointing to spec 261's contract, only if that contract lists the Expression-mode output shapes. Otherwise skip.
- [ ] T022 Run `Extensions/Elsa3/tests/Mapping/Tests/Elsa3.Mapping.Tests.csproj` in full. Confirm that the build adds no new warnings in `Extensions/Elsa3/src/Mapping`.
- [ ] T023 Throwaway JTest check per `quickstart.md`. Record the numbers in a "Measured after" section in `specs/261-elsa3-undefined-strict/spec.md` (SC-004, SC-005, FR-012), delete the probe, and check that `git status` is clean.
- [ ] T024 Draft the three Elsa 4 issues (FR-014): nested-`undefined` key loss, the one-line script wrapper, and `null` into a non-nullable input (research R-8). Show the texts to the maintainer. File each as `needs-triage` only after approval, then link the first and third from the README.
- [ ] T025 Re-check #3012 and its open PRs for competing work. Merge `origin/main` again if it moved, and regenerate `docs/maps/dependency-map.json`/solution filters only if `-- check` says they changed (CRLF caveat: use `git diff --stat`).
- [ ] T026 Set `specs/261-elsa3-undefined-strict/spec.md` **Status** to Implemented in this PR, per `docs/reference/spec-lifecycle.md` and the maintainer's rule. Open the PR from `agent/claude/3012-elsa3-undefined-strict`, linked to #3012, with the corpus numbers. Post gate evidence as a PR comment, and request review with `@elsa-workflows-bot review`.

## Dependencies

- **Setup and foundational first:** T002, then T003, before any story.
- **Story order:** US1, US3, US2, US4. They share one source file, so they run in sequence. US2's strict check (T016) must
  come after US4's newline (T019) in the code path, but the tasks can land in either order. Without T019, the
  trailing-comment scripts are refused by T016, which is correct behaviour on its own.
- **Polish after all stories:** T020–T022. T023 needs every story. T024 can be drafted any time after T007, and its filing
  waits for approval. T026 comes last.

## Parallel opportunities

- **Test files:** T004/T005 and T009/T010 are in different test files. Each can be written while the other story's code is
  in progress.
- **Docs and drafts:** T020, T021 and T024 are documentation or drafts and touch no code.
- **Production code:** none can run in parallel; it is a single file.

## Implementation strategy

- **MVP:** US1. It fixes the 16 corpus run-time failures.
- **Then the refusals:** US3 and US2, which are guards for workflows outside the corpus. Then US4.
- **Finish:** README, the full test project, the JTest after-measurement, the issue drafts, and one PR that sets Status to
  Implemented.
