# Research: current turn context

Baseline: reviewed main `ab46bc22a52aa6e456bca6ac0e803c80904de081`.

## Reuse module admission for current disclosure

**Decision**: Reuse installed context sources, native identity/authorization and host policy when qualifying retained context before provider use. Preserve accepted receipts and operation targets. Let the native authority experiments determine whether re-admission, an explicit refusal or a minimal additional owner hook is necessary.

**Rationale**: `AgentApi` admits context at acceptance. `AgentTurnServices.PrepareAsync` later uses `latest.Context` or `paused.Context` directly. Tool preflight already checks current permissions; that does not establish current context/result disclosure. These are source observations, not yet runtime failures.

**Alternatives**: Trusting stored context forever or client permission claims fails current authority. Provider-specific domain filters violate the Program. A universal resource authorization schema duplicates owning modules.

## Bound individual collection without changing global events

**Decision**: Qualify required/optional failure and noncompletion at existing contribution boundaries. Keep the named Sequential fan-in and one aggregator. Use explicit typed outcomes, deadlines/cancellation and isolated contribution buffers where needed; do not swallow global event failures.

**Rationale**: Client collection awaits sources sequentially and checks abort only before each call; its existing `required` descriptor is unused for unexpected failure. Backend aggregation also awaits sources directly. A noncooperating asynchronous contributor needs a bounded outcome, and late mutations must not reach admitted state.

**Alternatives**: An aggregate timeout alone loses which required evidence is missing. Globally changing event failure delivery exceeds scope and the constitution. Merely passing a cancellation token does not bound an uncooperative promise/task.

## Show admitted evidence, preserve opaque payloads

**Decision**: Use backend admission metadata for displayed context; preserve correlation/source and refresh the per-request active surface. Generic chips may show declared provenance, target/revision and limitations, while domain details remain module-owned.

**Rationale**: The UI currently sets locally collected attachments before backend acceptance; client response mapping ignores admitted message context. Generic chips inspect workflow-shaped selection/catalog fields. Session surface is only sent at creation. Component experiments must distinguish actual gaps from standard host remount behavior.

**Alternatives**: New domain-specific label cases deepen coupling. Displaying local content as admitted misrepresents exclusions. Updating an outstanding invocation's target when a route changes would violate its original binding.

## Preserve lifetime and delivery boundaries

**Decision**: Exercise same-tick shell/direct sends and late completion before selecting a queue or explicit rejection. Each request identity needs an observable disposition. Preserve existing generation guards and client receipt acknowledgements.

**Rationale**: The shell has one incoming-prompt state slot and the hook reads render-captured chat state. Standard sign-out and module reconciliation already unmount the shell; retained custom editor lifetimes need separate evidence rather than an assumed defect.

**Alternatives**: A second orchestration engine or speculative durable prompt queue exceeds this leaf. Silently replacing an incoming prompt fails explicit correlation.

## Qualification boundary

**Decision**: Fresh package-only consumers use verified M1 archives and fresh native state for baseline probes; integrated fixes rebuild supported packages and rerun the independent witness. Capture actual Copilot/Anthropic adapter input through the existing deterministic protocol fixture.

**Rationale**: This proves the actual transport/ownership path without credentials or live-provider charges. Completed proof roots remain immutable; native IAM changes require fresh normal authentication because an older token retains its permission claims.

**Alternatives**: In-process-only assertions cannot prove composed behavior. Reusing mutable published directories contaminates accepted evidence. A provider-protocol pass is not a live-provider observation.
