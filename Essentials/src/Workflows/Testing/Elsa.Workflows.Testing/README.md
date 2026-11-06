# Workflow testing

The optional `WorkflowsTesting` feature currently composes deterministic assertion evaluation, shared CI exit policy and JUnit rendering over the [public data contract](../Core/README.md). The saved runner, durable library, API, Studio and packaged CI journey are being implemented under [spec 1220](../../../../../specs/1220-workflow-activity-testing/spec.md). These services alone do not establish a runnable or accepted testing product.

Execution status and assertion verdict are separate. Expected business faults may satisfy checks; a completed execution with wrong output fails. Missing/withheld observations do not become null. Interaction checks require a settled observation interval, including attempted calls. Numeric comparison preserves exact decimal coefficient/exponent values instead of rounding through machine floating point.

Exit policy requires final results, asserted checks and settled cleanup. Empty/all-skipped/unasserted, unavailable/incomplete and required-skipped selections cannot pass. JSON retains detailed state; JUnit is a projection with exact case/candidate/dependency properties. A configured report policy is honored by the renderer.

## Registrations

Scoped `IWorkflowTestAssertionEvaluator`, `IWorkflowTestReportPolicy` and `IWorkflowTestReportWriter` defaults, plus a shell Prepare initializer at order -200 rejecting missing/duplicate replacement registrations after all features configure. See [extension points](EXTENSION_POINTS.md). No event handlers, fan-in contributors or scheduled tasks are registered by this implementation stage. No runtime target/effect/time profile is advertised by these registrations.
