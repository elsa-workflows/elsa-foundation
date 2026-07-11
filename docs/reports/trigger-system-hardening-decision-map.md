# Trigger-System Hardening Decision Map

Program goal: [Runtime Execution Seam](../program-goals/runtime-execution-seam.md) — stewards: Joey plus the incoming runtime architect. Composition and connected-test follow-ups cross-link to [Feature Composition Readiness](../program-goals/feature-composition-readiness.md) and [Code Reality And Test Maturity](../program-goals/code-reality-and-test-maturity.md). No new bucket.

## trigger-contract: Resolve The Trigger Publication Contract

Blocked by: none
Status: resolved
Type: Research

### Question

What contract should connect authored trigger intent, executable classification, provider recognition, publication/indexing, diagnostics, compatibility, and shell composition for first-party Event, Timer, Cron, and HttpEndpoint triggers?

### Answer

1. **Canonical intent is layered.** Authoring metadata declares capability and authored start intent; compilation projects that intent into the Design-free `ExecutableNode`; a provider must then recognize the executable activity type and materialize zero or more normalized stimuli. Runtime indexing consumes only executable nodes and provider results. For CLR-backed activities, `[TriggerActivity]` is re-derived during compilation while persisted catalog `ExecutionType` remains legacy-compatible; non-CLR activities may use the catalog execution type as the compile-time fallback. No single layer is sufficient by itself.
2. **Missing provider fails publication, not startup.** A node classified as a trigger but recognized by no provider is a publication error before trigger-index replacement. `Recognized([])` remains a successful, intentionally non-starting result. Startup validates only internally inconsistent enabled feature graphs; design-only shells may expose/edit trigger definitions without runtime providers, but cannot publish them as runnable artifacts.
3. **Publication stays command-focused.** Unit A does not add a diagnostics payload to `PublishedWorkflowView`. Unit B may add a backward-compatible registration summary (counts/status only) if a caller needs immediate confirmation, while detailed diagnostics use a separate query contract.
4. **Durable bindings are the registered-state source.** Registered diagnostics project from the durable binding store plus the published artifact. Do not add a parallel persisted status document. Intentionally-non-starting is derived by re-running the same pure provider description over the artifact; invalid/provider-unavailable remains a publication or shell-health error, not durable published state. If durable failure history is later required, that is a separate operational/audit decision rather than trigger-index state.
5. **Expose a safe fixed envelope, then allowlisted display facets.** Provider-neutral fields are artifact/source identity, executable node id and activity type, provider id, recognition outcome, binding id, stimulus type/hash, and indexing state. Provider display metadata is allowlisted (event key; timer interval; cron expression/time zone; HTTP method/template and non-secret operational options). Never expose authorization material, request data, correlation values, live services, or arbitrary unfiltered metadata.
6. **Never mutate an existing catalog version in place.** Classification corrections for existing CLR activities belong in the compiler projection so same-version catalog hashes remain stable; authored contract changes require a new activity version. Existing executable shapes remain readable and runtime stays artifact-only; a corrected classification takes effect through republish, which produces the appropriate behavioral artifact hash, unless a separate explicit artifact migration is approved. Any persisted trigger-binding shape change follows the Groundwork per-kind version bump + upcaster + golden-fixture rule.
7. **Composition uses declarations plus tests.** A trigger activity feature declares transitive dependencies needed for its first-party runnable path; provider-specific runtime features keep transport/scheduling behavior out of generic runtime. Architecture tests verify declarations, runtime/design package boundaries, and the reference Server composition. Connected publish-to-dispatch tests prove behavior; tests do not substitute for dependency declarations.

Evidence: [PR #621](https://github.com/elsa-workflows/elsa-foundation/pull/621), [spec 089](../../specs/089-http-endpoint-parity/spec.md), [spec 089 research](../../specs/089-http-endpoint-parity/research.md), [serialization rules](../serialization.md), [ADR 0038](../adr/0038-artifact-hash-is-purely-behavioral-and-executables-are-content-addressed.md), `WorkflowTriggerBindingExtractor`, `WorkflowTriggerIndexer`, `ExecutableNodeCompiler`, and the first-party trigger providers.

### Smallest first work unit (Unit A)

Specify the existing layered contract and close only its semantic-validation gaps for Event, Timer, Cron, and HttpEndpoint:

- define one trigger-contract matrix covering authored intent, executable marker, recognizing provider, zero/one/many bindings, invalid identity, and intentional non-start;
- make provider recognition identifiable through a stable provider id in the preflight result, without adding the Unit B diagnostics API or a new persisted status model;
- introduce one preflight publication result that validates every classified node, descriptor, and required provider-owned publication projection before any trigger- or schedule-index replacement, preserving `Recognized([])`;
- treat a recognized start trigger whose provider-owned projection cannot materialize (including an exhausted Cron schedule) as a publication failure rather than a logged zero-registration success;
- prove legacy catalog rows compile and republish without same-version reconciliation hash drift;
- add focused publication and compatibility tests for all four first-party trigger families.

Out of scope: diagnostics endpoints/read models, persisted publication-status records, shell startup health reporting, CShells dependency changes, connected-host smoke-test expansion, Studio, route uniqueness generalization, multi-node route-table invalidation, and any `IStimulusRouter` or actor/mailbox redesign.

Boundary acceptance: Unit A is complete when every classified first-party trigger is either (a) recognized with valid materializable bindings and required provider-owned projections, (b) recognized as intentionally non-starting, or (c) rejected before trigger/schedule-index mutation, with compatibility proofs for existing catalog/executable data. Detailed observability remains Unit B; composition enforcement remains Unit C. Publication-wide transactionality or reordering of executable/source-reference persistence is not implied by this boundary.
