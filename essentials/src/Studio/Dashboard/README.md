# Saved questions and dashboards

Dashboard defines validated module-owned queries and saved definition resources alongside Studio Home. This slice supplies native query/resource management and four-provider EF definition persistence; The native API is now supplied; dataset sources, Studio pages and conversational authoring follow in the [delivery stack](../../../../specs/1217-ai-dashboards/delivery-slices.md).

## Native composition

`StudioDashboardManagement` registers one scoped `IDashboardQueryService`, structural and visualization validation, bounded read concurrency, resource authorization and definition validation. The [definition store](Persistence/EntityFrameworkCore/README.md) adds conditional durable definitions and history. Dataset modules contribute declaration, validation, execution and drill through the [named extension points](Management/EXTENSION_POINTS.md). No dataset-specific branch belongs in generic management.

A host must supply its trusted actor context, audience directory and durable resource store for the native API. [StudioDashboardApi](Api/README.md) diagnoses missing or duplicate required registrations. An absent dependency does not grant access. A source authorizes records, fields and aggregate operations before computation, ordering or pagination. Missing or duplicate dataset ownership fails closed.

No module source or HTTP route is enabled by adding this package alone. Home keeps its existing widgets and layout preferences. Native queries do not invoke a provider; the optional authoring implementation will delegate to Weaver through the SDK-free Core contract.

## Qualification

See the [query contract](../../../../specs/1217-ai-dashboards/contracts/query.md) and [resource contract](../../../../specs/1217-ai-dashboards/contracts/resources.md). Query and composition tests verify generic semantics with controlled module contributions. Actual Workflow/Acme math, native HTTP persistence/sharing, composed Studio and live Copilot are separate later gates. Historical evidence is not qualification of this slice.
