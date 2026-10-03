# Product requirements: Studio expression developer experience

## Executive summary

Workflow authors should get an immediately recognizable, trustworthy code-authoring experience whenever they select an installed supported text Expression Type for an activity input. The experience must combine readable syntax, location-scoped workflow knowledge and runtime-compatible language help while remaining usable during partial deployment or backend failure.

Program [#2310](https://github.com/elsa-workflows/elsa-foundation/issues/2310) completes the already implemented expression-intelligence baseline. It retains the existing editor and module contracts and closes the gaps between component evidence, default-host composition, runtime semantics and real authoring quality.

## Problem and opportunity

The underlying editor, language Contributions and Foundation tooling contracts exist, but that does not guarantee the default host loaded them, that the current workflow location reached the provider, or that local grammar/help matches the configured runtime. Historical fixture evidence used synthetic symbols and diagnostics. The missing normal-host proof and remaining JavaScript, Liquid, theme, signature and formatting gaps make the experience appear inconsistent and can suggest source that will not execute.

The opportunity is to turn the existing investment into a dependable product surface instead of replacing it.

## Users and actors

- **Workflow author**: writes and repairs activity-input expressions and needs relevant help without learning internal APIs by trial and error.
- **Module author**: contributes an Expression Type, editor adapter and/or runtime tooling provider and needs a clear conformance contract.
- **Host operator**: composes language modules, permissions and policy and needs degraded states to describe the actual deployment.
- **Support and release engineer**: needs reproducible current-head evidence that Studio and Foundation work together.

Canonical terms remain in the Studio [`CONTEXT.md`](https://github.com/elsa-workflows/elsa-foundation-studio/blob/main/CONTEXT.md); this PRD does not redefine them.

## Primary journeys

### Select and author a supported text expression

From a persisted workflow, the author selects JavaScript or Liquid for an activity input. The compact property editor immediately shows the correct syntax and location-scoped inputs, variables and predecessor outputs. Expansion preserves source, cursor and undo state.

### Discover language-specific help

The author receives JavaScript local/member/signature help or Liquid interpolation/filter/tag help. The offered grammar, APIs, tags and filters are executable by the configured runtime. Unknown dynamic shapes are described honestly.

### Repair and validate source

The author gets immediate local feedback plus runtime-owned diagnostics without source evaluation. Incomplete drafts remain editable. Consequential operations retain the existing authoritative Foundation gates.

### Continue through degraded composition

When an editor module or tooling provider is missing, unauthorized, incompatible or unavailable, source remains safe and editable where authorized. Studio identifies the exact degraded capability and does not silently claim the complete experience.

### Polish and accessibility

The author can use completion, hover, signatures, explicit formatting, keyboard exit and expanded editing in light, dark and dim presentation, narrow inspectors and supported assistive-technology flows without losing source or editor continuity.

## Functional requirements

1. Normally composed Studio and Foundation hosts must prove the persisted workflow journey through the real syntax picker, inspector, capability links and providers.
2. Every installed supported text Expression Type must resolve editor and tooling readiness explicitly; reference and structured Expression Types keep their specialized editors.
3. JavaScript grammar and advertised APIs must remain within the configured runtime surface. Local language help and authorized workflow help must coexist.
4. Liquid completion must distinguish value, filter and tag positions and use the runtime's effective binding-pure filter/tag metadata.
5. Metadata-only assistance must never evaluate authored source, retrieve runtime values or call runtime services.
6. Diagnostics may be definitive only when known metadata proves a mistake; dynamic unknowns remain non-definitive.
7. Syntax, previews, selections, diagnostics and help must be readable in light, dark and dim presentation using Studio design tokens.
8. Signatures must expose active parameters and overloads. Formatting is explicit, language-owned, behavior-preserving and undoable.
9. Compact/expanded editing, multiline paste, syntax switching, authorization changes and stale requests must preserve the established source/session invariants.

## Non-functional requirements

- Preserve the current permission, Host Policy, no-store, cancellation, revision and telemetry boundaries.
- Keep language/editor modules independently composable and keep editor-engine types out of the public Studio SDK.
- Apply WCAG 2.2 AA interaction expectations to the scoped editor journeys through automated accessibility checks plus the named keyboard and screen-reader scenarios. This program does not claim product-wide formal WCAG conformance.
- Keep normal-host integration proof current to the exact reviewed Studio and Foundation heads.
- Use focused affected-suite validation first and serialize heavy builds on the shared machine.

## Product principles

- Runtime truth outranks browser convenience.
- Working source remains accessible even when assistance degrades.
- Language modules own language semantics; Workflows owns workflow scope.
- Unknown is not invalid.
- A synthetic fixture is regression evidence, not deployment evidence.

## Constraints

- Retain CodeMirror behind the existing engine-neutral contract.
- Retain Studio ADRs [0002](https://github.com/elsa-workflows/elsa-foundation-studio/blob/main/docs/adr/0002-keep-expression-specific-support-in-expression-modules.md) and [0006](https://github.com/elsa-workflows/elsa-foundation-studio/blob/main/docs/adr/0006-keep-code-editing-as-internal-studio-infrastructure.md).
- Reconcile and extend Studio spec 094 and Foundation spec 143 instead of creating competing baselines.
- The deeper JavaScript language-service choice requires a bounded spike before adoption.

## Explicit non-goals

- Replacing the shared editor.
- Evaluating expressions or loading live runtime values for assistance.
- Advertising browser or Node APIs absent from runtime.
- Forcing reference or structured Expression Types into text code editors.
- Cross-expression rename, navigation or project-wide type analysis without a later approved scope change.
- A new performance-measurement program.

## Delivery stages and success

1. **Composed baseline**: the real persisted workflow journey passes for JavaScript and Liquid and proves degraded states.
2. **Conformance**: installed text syntax readiness and runtime JavaScript grammar/API parity pass.
3. **Language depth**: JavaScript locals/members/signatures and Liquid value/filter/tag help use runtime-owned metadata with useful diagnostics.
4. **Complete experience**: theme, preview, signature, formatting, keyboard, screen-reader and continuity acceptance passes in real Studio.

The program succeeds only when all four executable demonstrations pass on reviewed heads and the affected repository gates are green.

## Open product decisions

None for milestone 1. The only deferred technical decision is whether a deeper JavaScript language service earns adoption; its spike must prove worker isolation, runtime-specific declarations, bundle/loading cost, cancellation, accessibility and fallback before it can affect milestone 3.
