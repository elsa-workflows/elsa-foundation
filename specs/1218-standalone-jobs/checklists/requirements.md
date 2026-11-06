# Specification quality: Standalone durable jobs

Draft review record, 6 November 2026. Feature: [spec.md](../spec.md).

- [x] User goals and independent standalone/native/BPMN/operator journeys describe product value.
- [x] Mandatory scenarios, requirements, entities, success criteria and assumptions are present.
- [x] Requirements are testable and map to the original #3586 scope and acceptance matrix.
- [x] Confirmed owner selections are preserved, including attempt cutoff and recurring catch-up.
- [x] Accepted jobs, physical attempts, retained results and model consumption are distinct.
- [x] Failure, race, version, scope, retention and duplicate-delivery edges are identified.
- [x] Dependency boundaries, exclusions and provider/deployment evidence limits are explicit.
- [x] Independent cross-artifact design review is complete and findings resolved at44c22f34f.
- [x] ADR0096's scoped domain adoption and initial design Constitution Check are recorded; concrete implementation/package checks remain pending.

The product names .NET/native/BPMN and package isolation because #3586 explicitly requires those
consumer shapes. Algorithms, concrete API/schema and provider design live in the plan/contracts.
Implementation is underway; checked content-quality items are not delivery evidence.
