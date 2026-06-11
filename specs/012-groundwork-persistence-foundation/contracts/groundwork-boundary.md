# Contract: Groundwork Boundary

Groundwork is a generic persistence framework. Elsa is the first validation consumer.

## Generic Groundwork Responsibilities

- Define storage manifests and storage units.
- Validate workload classification and manifest compatibility.
- Define provider capability reports.
- Define materialization plan and history contracts.
- Define portable document storage semantics.
- Define declared index and portable query semantics.
- Define provider diagnostics.
- Provide provider packages for supported databases.

## Application Integration Responsibilities

- Discover application/module manifests.
- Map application concepts onto generic storage units.
- Register Groundwork services in the application host.
- Run startup materialization in the application lifecycle.
- Expose application-specific diagnostics.
- Implement domain repositories and command/query services.
- Decide when application workloads are eligible for Groundwork.

## Elsa Bridge Responsibilities

- Keep Elsa concepts under Elsa packages.
- Map Secrets, catalogs, runtime entity definitions, runtime entity instances, and selected metadata stores onto Groundwork manifests.
- Keep workflow runtime checkpoint and operational workloads benchmark-gated.
- Preserve existing Elsa behavior while Groundwork-backed stores are opt-in.

## Boundary Rules

1. A `Groundwork.*` package must not reference an `Elsa.*` package.
2. A generic Groundwork manifest concept must not be named after an Elsa domain concept.
3. A provider package must be provider-suffixed.
4. Provider-specific storage shape is produced by a provider planner, not declared directly by application modules.
5. Unindexed portable queries fail clearly.
6. Runtime queues, execution logs, outbox records, timers, and locks are specialized unless a later slice proves otherwise.
