# Research: Expression Code Intelligence Foundation

## D1 — Design context is language-neutral and provider-owned projection is language-specific

**Decision**: `Elsa.Workflows.Design` builds a metadata-only `ExpressionAuthoringContext`; `Elsa.Expressions.Core` defines a provider contract resolved by expression type; JavaScript and Liquid providers project the same context into their own symbols, completion items, hovers, and semantic diagnostics.

**Rationale**: Workflow inputs, lexical variables, activity-result references, expected type, permissions, and host policy are design facts. JavaScript syntax, Liquid tags/filters, and future language interpretation are not. A shared set of “Elsa globals” would either leak workflow concepts into every language or falsely claim identical semantics.

**Alternatives considered**: One universal global catalog (rejected: language semantics differ); Studio-only symbols (rejected: client cannot authoritatively filter/pin them); a provider builds its own workflow traversal (rejected: duplicates policy and scope logic).

## D2 — `Elsa.Expressions.Core` owns a small cancellable tooling seam

**Decision**: Add stable models for authoring document identity, tooling version, symbol/value shape/signature/documentation, diagnostics, and explicit outcome states. Add `IExpressionToolingProvider` and a registry/resolver in `Elsa.Expressions.Core`.

**Rationale**: Existing `IExpressionDescriptor` only identifies editable expression types and has an untyped property dictionary. The new seam must be reusable by every expression type without making the Core package depend on Workflow Design or an editor implementation.

**Alternatives considered**: Extend `IExpressionDescriptor` with tooling methods (rejected: mixes static descriptor metadata with scoped work); use a generic language-server protocol (rejected: unnecessary transport commitment); put contracts in API (rejected: other in-process consumers need the seam).

## D3 — Context assembly filters before provider invocation

**Decision**: A Design-owned context service resolves draft location, expected result type, lexical scope, source-owned activity contracts, expression descriptor, caller permissions, and host policy. It creates a bounded snapshot and removes inaccessible symbols before calling a language provider.

**Rationale**: Existing authoring services already validate an activity/input location against submitted workflow state and propagate cancellation. Centralizing this filtering makes no-disclosure testable and prevents a provider from accidentally learning a symbol that it later fails to render.

**Alternatives considered**: Return redacted symbols (rejected: disclosure remains possible); allow providers to enforce permissions themselves (rejected: duplicated unsafe policy); return all symbols eagerly (rejected: scale and latency).

## D4 — No evaluation or runtime data belongs on the tooling path

**Decision**: Context contains names, documentation, signature/value-shape, declared type aliases, hierarchy, stable identifiers, and design revision metadata only. Providers parse/analyze supplied source but cannot receive a runtime evaluator, `IServiceProvider`, workflow execution context, live value, or mutation callback.

**Rationale**: Code intelligence must not become a security or determinism bypass. The existing portable expression evaluator intentionally receives immutable declared parameters; the authoring path is even narrower because it has no parameter values.

**Alternatives considered**: Sample/evaluate expressions for inferred output (rejected: side effects and data disclosure); query last run values (rejected: runtime/design boundary); pass services for custom completion (rejected: uncontrolled host access).

## D5 — Additive capability links and endpoints preserve feature composition

**Decision**: `Elsa.Workflows.Design.Api` advertises the expression-tooling capability with canonical, versioned links and exposes its context and language operations because location, draft access, authorization, and endpoint composition belong to Design. The capability is omitted unless the context service and at least one language provider are both available. Existing `GET` expression descriptor list remains unchanged.

**Rationale**: Existing endpoints are permissioned and capabilities are independent of caller authorization. Studio can discover support without inferring feature names and retains generic editing if links are absent.

**Alternatives considered**: Replace the descriptor endpoint (rejected: compatibility break); one combined API that accepts unvalidated graph details (rejected: bypasses Design authority); force all shells to compose tooling (rejected: deployment flexibility).

## D6 — Revisioned, explicit outcomes prevent stale or ambiguous UI behavior

**Decision**: Every request contains tooling-contract version plus authoring-document and context revisions. Responses carry one of `success`, `supported-empty`, `unavailable`, `unauthorized`, `incompatible`, `stale`, or `canceled`, together with the evaluated revision when applicable.

**Rationale**: An empty catalog is useful and different from an unavailable provider. A debounced editor must distinguish a response for an older draft from a current answer. Cancellation has to reach providers rather than producing cacheable partial output.

**Alternatives considered**: HTTP status alone (rejected: cannot express supported-empty and stale semantics); best-effort partial results (rejected: ambiguous/no caching); server-side session state (rejected: new persistence/lifecycle burden).

## D7 — Reuse the current draft-validation gate with strict versus shielded modes

**Decision**: Register a full-draft expression validator on the existing inline validation event/gate. Read/ad-hoc diagnostics use the shielded `TryDeriveValidationErrorsAsync` behavior. Test-run and publication/promotion call strict validation at the consequential boundary; known errors reject. Test run can only continue after an explicit unavailable-validator confirmation; publication/promotion fail closed.

**Rationale**: The repo already distinguishes a fault-tolerant validation read from a strict mutation gate. Reusing it keeps diagnostics unified and preserves drafts that have a broken validator while preventing publication of unknown validity.

**Alternatives considered**: Persist editor diagnostics (rejected: stale duplicates); block every draft mutation (rejected: poor authoring); let every run proceed on validation outage (rejected: unsafe); make publication warn-and-proceed (rejected: violates fail-closed decision).

## D8 — Bounded catalogs and sanitized documentation are mandatory

**Decision**: Context responses page/search symbols after policy filtering and inline value-shape members to a bounded depth of four. Lazy member retrieval is not advertised in v1. Documentation uses a small sanitized Markdown/text subset. API responses are `Cache-Control: no-store`; logging/telemetry records only classification/count/timing, never source, prefixes, symbol names, documentation, or diagnostic text.

**Rationale**: Large flows can have many symbols, and contextual metadata can itself be sensitive. The UI needs deliberate degradation rather than an accidental oversized payload or privacy leak.

**Alternatives considered**: Cache full contexts in a shared server cache (rejected: privacy/invalidation); emit raw module docs (rejected: unsafe markup); log failed source for diagnosis (rejected: source disclosure).
