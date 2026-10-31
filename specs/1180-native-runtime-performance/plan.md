# Implementation plan

The owner approved this sequence with “Do it” after reviewing the assessment and recommendations. Each change remains independently reviewable and measurable.

1. Guard the default zero-input materialization path and retain the normal snapshot writer. Add counting-store regressions plus custom-materializer and cancellation controls before changing production code.
2. Use weak, node-identity-owned preparation in the runtime implementation for invariant binding-set validation, ordered inputs and fingerprints. Keep runtime value/type-registry work per invocation and preserve validation ordering where observable.
3. Index the existing session overlay for ordered successor lookup. Do not add a new durable baseline cache without a proved invalidation/authorization protocol. This reduces reconstruction while retaining current durable reads and existing session lifetime rules.
4. Inspect EF binding reuse opportunities and qualify any chosen change separately; do not switch pooling defaults from the narrow experiment alone.
5. Run affected complete test projects, relevant rebuilt REST e2e, maps and architecture gates. Compare frozen baseline and changed Release probes using the previous harness, isolated stores and quiet-machine intervals. Record unmeasured cases explicitly.

## Constitution check

Framework §2.1/§2.6: reuse existing runtime and persistence projects; no cross-domain dependency or new extension point. §2.5.1: caches contain only immutable preparation and do not retain scoped services. §2.21/§2.23 and ADR0080 D1: preserve existing behavioral tests and add focused risk-based controls, including negative controls. Elsa §E2.6: execution stays artifact-only; no design dependency. No changes to fencing, checkpoint/outbox atomicity, durable schemas, public contracts or draft constitutional meaning. Post-design review must confirm these boundaries.
