# Workflow inspection authorization contracts

This contracts-only package exposes the existing request-scoped `IActivityInspectionContextAsync` to authorized module readers without requiring a reference to the Runtime API implementation. It depends on Runtime models and Foundation's replacement-contract declaration; the baseline Runtime Core remains independent of Identity and Dashboard.

The normal HTTP implementation remains `HttpContextActivityExecutionInspectionAuthorizationContext` in `Elsa.Workflows.Runtime.Api`. It retains the same trusted subject/tenant, separate structure/value/payload grants, permission/resource evaluation and fail-closed behavior. Custom hosts implement the same explicit replacement contract; there is no production allow-all fallback.

This pre-GA assembly/namespace move changes public contract identity. Consumers import `Elsa.Workflows.Runtime.Inspection.Core` and reference this package; no compatibility alias or second authorization policy is introduced.

See [extension points](EXTENSION_POINTS.md) and the [Runtime API catalog](../../Api/EXTENSION_POINTS.md).
