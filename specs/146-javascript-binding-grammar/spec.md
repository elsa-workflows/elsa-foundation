# Feature Specification: JavaScript Binding Grammar Selection

**Feature Branch**: `146-javascript-binding-grammar`
**Created**: 2026-08-05
**Status**: Draft
**Input**: Let a host admit Elsa 3 style JavaScript binding expressions, evaluated with Elsa 3's own Script semantics and selected by deployment configuration, without letting deployment configuration change the behavior of an already-published executable.

Decision of record: [ADR 0062](../../docs/adr/0062-javascript-binding-grammar-is-pinned-at-publish.md).
Constrained by [ADR 0038](../../docs/adr/0038-artifact-hash-is-purely-behavioral-and-executables-are-content-addressed.md).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Author an Elsa 3 style binding on a permitting host (Priority: P1)

An operator migrating from Elsa 3 configures the host to accept Script-grammar bindings. An author
types ``return `Hello World!` `` into an activity input, sees no validation error, and the workflow
runs and prints the value.

**Why this priority**: This is the migration blocker. Every Elsa 3 author meets it on their first
expression, and today the only signal is a raw parser message.

**Independent Test**: On a host configured for `script`, author both a `return`-bearing binding and a
completion-value binding; verify each validates clean, publishes, and evaluates as it does in Elsa 3.

**Acceptance Scenarios**:

1. **Given** a host defaulting to `script`, **When** an author saves a draft containing
   `return <expr>;` in a JavaScript binding, **Then** draft validation reports no syntax diagnostic.
2. **Given** that draft, **When** it is published, **Then** each compiled JavaScript
   `ExpressionDefinition` records `{"grammar":"script"}` in `Options`.
3. **Given** that executable, **When** the binding evaluates, **Then** the value returned by the
   body is the binding's value.
4. **Given** a `script`-grammar body with no `return`, such as `const total = a + b; total`,
   **When** it evaluates, **Then** its value is the completion value of the last expression
   statement, matching Elsa 3.
5. **Given** a host defaulting to `expression`, **When** the same source is authored, **Then**
   validation reports a syntax diagnostic and publication fails closed, as today.

---

### User Story 2 - Promote an executable across environments unchanged (Priority: P1)

An operator promotes a published executable from staging, where Script-grammar bindings are permitted,
into production, where they are not. The executable keeps behaving exactly as it did in staging.

**Why this priority**: This is the invariant ADR 0038 exists to protect, and the reason the setting
is captured at publish rather than read at runtime.

**Independent Test**: Publish under `script`, evaluate, then evaluate the same executable on a
host configured for `expression`; verify identical results and no configuration read on the
evaluation path.

**Acceptance Scenarios**:

1. **Given** an executable whose expressions record `script`, **When** it runs on a host
   defaulting to `expression`, **Then** its bindings still evaluate under `script`.
2. **Given** the same authored source compiled under each grammar, **When** their artifact hashes are
   compared, **Then** they differ.
3. **Given** any published executable, **When** its bindings evaluate, **Then** no host
   configuration value influences the evaluation mode.
4. **Given** an executable recording no grammar key, **When** it evaluates, **Then** it uses
   `expression`, so executables published before this feature are unaffected.

---

### User Story 3 - Understand why a binding is rejected (Priority: P2)

An author on an `expression` host types an Elsa 3 style body and is told what is wrong and what to do
about it, rather than being handed a parser message.

**Why this priority**: The current diagnostic is accurate and unactionable, which is what turns a
one-character fix into a support request.

**Independent Test**: On an `expression` host, validate ``return `x`;``; verify the diagnostic names
the grammar and the remedy.

**Acceptance Scenarios**:

1. **Given** a source that fails to parse as an expression but parses as a Script, **When** it is
   validated, **Then** the diagnostic states that bindings are expression-only on this host and
   names the remedy.
2. **Given** a source that parses as neither, **When** it is validated, **Then** the underlying
   parser diagnostic is reported unchanged.
3. **Given** any syntax diagnostic, **When** it is returned, **Then** its range and its message
   agree on the reported position.

---

### User Story 4 - See which grammar an expression uses (Priority: P3)

An author inspecting a binding can tell which dialect it is bound to, so that two identical-looking
sources behaving differently is explainable.

**Why this priority**: Necessary once two dialects exist, but it blocks nothing; the authoring and
promotion paths are correct without it.

**Independent Test**: Read the authoring context for a binding on each host configuration; verify the
active grammar is reported.

**Acceptance Scenarios**:

1. **Given** an expression authoring context request, **When** the response is returned, **Then** it
   names the grammar the draft would compile to.
2. **Given** a publication review of a definition whose grammar differs from its previously published
   executable, **When** the review is presented, **Then** the grammar change is surfaced as a
   behavior change without a source change.

### Edge Cases

- An empty or whitespace-only source is unchanged by this feature; it remains whatever the existing
  contract makes it under either grammar.
- A source valid under both grammars — any bare expression — must evaluate identically under both.
- An Elsa 3 expression relying on non-strict semantics is out of reach unless the strict-mode open
  question below resolves toward non-strict; it fails under either grammar today.
- A `script` body whose last statement is an expression without `return` yields that completion
  value, as in Elsa 3. A body that produces no value at all still fails the existing
  "cannot return undefined" check.
- An `Options` bag carrying an unrecognized key must still be rejected; only `grammar` is admitted.
- A `grammar` value outside the known set must be rejected at deserialization, not defaulted.
- A draft authored under one host default and published against another must not silently disagree;
  publication records the grammar in force at publish.
- `RunJavaScript` script bodies are unaffected — they already evaluate as function bodies through a
  separate evaluator and carry no binding grammar.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST define exactly two JavaScript binding grammars, `expression` and
  `script`, with `expression` as the default when unspecified.
- **FR-002**: `ExpressionDefinition.Options` MUST carry the grammar for JavaScript bindings under the
  key `grammar`; an absent key MUST resolve to `expression`.
- **FR-003**: The recorded grammar MUST participate in `ExpressionDefinition` fingerprinting and
  therefore in the `ArtifactHash`, with no change to the fingerprint algorithm.
- **FR-004**: `JintPortableJavaScriptEvaluator` MUST select its evaluation mode from the recorded
  grammar alone and MUST NOT read host configuration on the evaluation path.
- **FR-005**: The evaluator MUST evaluate `expression` as `"use strict"; (<source>)`, and MUST
  evaluate `script` by parsing the authored source as an ECMAScript Script with
  `AllowReturnOutsideFunction = true` and evaluating it directly.
- **FR-005a**: Under `script` the authored source MUST NOT be wrapped, prefixed, or rewritten, so
  that both `return <expr>;` and completion-value bodies behave as they do in Elsa 3.
- **FR-006**: The evaluator's existing rejection of a non-empty `Options` bag MUST be relaxed to admit
  the `grammar` key and MUST continue to reject every other key.
- **FR-007**: The capability profile MUST remain `binding-pure-v1` for both grammars, and
  `ExpressionEvaluationCapabilities` MUST be unchanged.
- **FR-008**: A host setting MUST select the grammar stamped onto newly compiled JavaScript bindings,
  exposed as a `[ManifestSetting]` and defaulting to `expression`. Granularity is host-level; the
  setting MUST NOT be expressible per workflow definition in this unit.
- **FR-009**: The host setting MUST be reachable by both the Jint evaluator feature and the
  JavaScript tooling-provider feature without either taking a dependency on the other.
- **FR-010**: The publish compiler MUST record the grammar in force at publish onto every JavaScript
  binding it compiles.
- **FR-011**: `JavaScriptExpressionToolingProvider` MUST validate a draft in the grammar that draft
  would compile to, parsing as an expression under `expression` and as a Script with
  `AllowReturnOutsideFunction = true` under `script`.
- **FR-012**: When a source fails under `expression` but parses as a Script, the diagnostic MUST
  identify the grammar as the cause and name the remedy, retaining the stable `JavaScript/Syntax`
  code.
- **FR-013**: A syntax diagnostic's message position and its `Range` MUST agree; the existing
  disagreement between the 1-based message text and the 0-based range MUST be resolved.
- **FR-014**: The expression authoring context MUST report the grammar a draft would compile to.
- **FR-015**: Publication review MUST surface a grammar change relative to the previously published
  executable.
- **FR-016**: Executables published before this feature MUST evaluate unchanged, via FR-002's default.
- **FR-017**: The implementation MUST add conformance tests for both grammars including
  completion-value bodies, grammar-differentiated
  fingerprints, promotion across mismatched host settings, options-key admission and rejection,
  validator/runtime agreement under each grammar, and the legacy default.
- **FR-018**: Existing tests pinning `expression` behavior — including
  `ExpressionToolingProviderContractTests` — MUST remain green under the default configuration.

### Key Entities

- **Binding Grammar**: The dialect a JavaScript binding is interpreted in; one of `expression` or
  `script`; recorded per expression and part of Execution Material.
- **Grammar Host Setting**: Deployment configuration selecting the grammar stamped onto newly
  compiled bindings. An authoring-time default, never a runtime input.
- **Expression Definition Options**: The existing per-expression evaluator options bag, now carrying
  at most the `grammar` key for JavaScript.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of promotion tests prove a published executable evaluates identically on hosts
  configured for either grammar.
- **SC-002**: 100% of evaluation-path tests prove no host configuration read occurs during binding
  evaluation.
- **SC-003**: Identical source compiled under each grammar yields different artifact hashes in 100%
  of comparison cases.
- **SC-004**: 100% of validator cases agree with the runtime under the same grammar — no source is
  approved by validation and rejected by Jint, or the reverse.
- **SC-005**: Every executable published before this feature evaluates unchanged, verified across the
  existing binding-evaluation suite with no fixture edits.
- **SC-006**: An Elsa 3 author on a `script` host authors, publishes, and runs both a `return`-bearing
  and a completion-value binding with zero source edits.
- **SC-007**: On an `expression` host, 100% of Script-parseable rejections produce a grammar-naming
  diagnostic rather than a bare parser message.

## Assumptions

- The deterministic binding sandbox is unchanged: `args` and `variables` stay read-only and ambient
  capabilities stay stripped under both grammars, which is what keeps the capability grant identical.
- Elsa 3 evaluates the author's source verbatim; matching it means copying its parser option, not
  reproducing a wrapper it never had.
- Publish-time compilation is the only point at which an `ExpressionDefinition` is minted for an
  executable; no other path needs to stamp the grammar.
- Existing draft-validation, test-run, and publication gates are the integration seams; this feature
  adds no new gate.
- Studio owns rendering of the grammar indicator; Foundation owns reporting it in the authoring
  context.
- Liquid and other expression types are unaffected; grammar selection is JavaScript-specific.

## Out of Scope

- Changing `RunJavaScript` or the script evaluator, which already accept statement bodies.
- Foundation's `Elsa3ExpressionRewriter` — its inability to parse `return`-bearing sources and its
  missing coverage are tracked as follow-up in ADR 0062 and specified separately.
- A general expression-dialect framework for other languages.
- Studio editor UX for the grammar indicator.
- Retro-stamping or migrating already-published executables.

## Open Questions

- Should Foundation's Elsa 3 importer stamp `script` on imported bindings, or lower toward
  `expression` where it safely can? Stamping migrates every source unchanged; lowering avoids pinning
  imported workflows to the legacy dialect indefinitely, but cannot restructure a completion-value
  body. ADR 0062 does not settle this.
- Does `script` evaluate strict or non-strict? Elsa 3 is non-strict, so full fidelity implies
  dropping `"use strict"` for this grammar, which is a real reduction in the sandbox story. Strict
  keeps the sandbox intact but leaves sloppy-mode Elsa 3 expressions failing.

Resolved during drafting: the host setting stays host-level; per-definition granularity waits for
demand.
