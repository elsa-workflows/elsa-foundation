# Extension points — Workflow Testing

Current implemented surface; [spec 1220](../../../../../specs/1220-workflow-activity-testing/contracts/testing.md) separately describes planned scenario/module contributions. Registration alone does not qualify an execution profile.

## Overridable contracts

| Contract (Core layer) | Signature | Default (feature layer) | Use and replacement |
|---|---|---|---|
| `IWorkflowTestAssertionEvaluator` | `TestCheckResult Evaluate(TestAssertion, TestSubjectObservation)` | internal `WorkflowTestAssertionEvaluator`, `Elsa.Workflows.Testing` | Evaluate deterministic authorized evidence. Replace its single scoped descriptor; never hide unavailable evidence as a pass. |
| `IWorkflowTestReportPolicy` | `int GetExitCode(TestRunReport)` | internal `WorkflowTestReportPolicy`, same feature | Shared Studio/CI aggregation. Replace one scoped descriptor. `WorkflowTestReportWriter` requires exactly one provider and rejects duplicates. |
| `IWorkflowTestReportWriter` | `void WriteJunit(TestRunReport, TextWriter)` | internal `WorkflowTestReportWriter`, same feature | Standard CI projection retaining original verdict/provenance. Replace one scoped descriptor; leave detailed JSON authoritative. |

`WorkflowsTestingFeature.ConfigureServices` registers defaults with `TryAddScoped`, preserving a host's deliberate replacement. These are single-provider seams, not ordered contributions. `WorkflowTestingCompositionValidator` checks the complete registration collection at shell Prepare order -200 and rejects missing/duplicate unkeyed replacements, including providers registered by later features. The report writer also rejects ambiguous policy resolution.

## Implementable contributor interfaces

None registered yet. Target/setup/step/assertion/effect/time fan-in contributions remain explicit implementation tasks; this catalog does not claim their design is already delivered.

## Events

None published by these deterministic services. No aggregating handlers or scheduled tasks.
