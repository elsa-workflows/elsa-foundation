# Workflow testing contracts

Optional production contracts for saved workflow/activity scenarios, shared by Studio, headless clients and module authors. This is separate from the developer-only `Elsa.Testing` grouping. The package contains authored and captured data shapes; validators, execution, effects, persistence and report policy belong to feature implementations.

The version-one design and remaining delivery gates are in [spec 1220](../../../../../specs/1220-workflow-activity-testing/spec.md). Adding the data contract alone does not advertise executable support. Runtime uses its own generic trusted test-entry/context ports and does not depend on this package or source authoring. Current replacement services are cataloged in the [composition feature](../Elsa.Workflows.Testing/EXTENSION_POINTS.md).
