# Contract: G1 Tests

G1 is complete only when focused automated tests cover the kernel contract.

## Manifest Validation Tests

- Valid sample manifest succeeds.
- Empty manifest fails.
- Missing workload classification fails.
- Missing schema version fails.
- Query referencing undeclared index fails.
- Provider-specific required physical shape fails.

## Planner Contract Tests

- Same valid sample manifest produces relational plan.
- Same valid sample manifest produces document plan.
- Relational plan includes declared indexes and schema-history operation.
- Document plan includes declared indexes and schema-history operation.
- Unsupported workload blocks planning.

## Provider Capability Tests

- Compatible capability report allows planning.
- Unsupported required index capability blocks compatibility.
- Unsupported concurrency mode blocks compatibility.
- Supported fallback emits warning without changing manifest intent.

## Architecture Boundary Tests

- `Groundwork.Core` has no `ProjectReference` to `Elsa.*`.
- `Groundwork.Relational` references only `Groundwork.Core`.
- `Groundwork.Documents` references only `Groundwork.Core`.
- No generic Groundwork public contract namespace contains `Elsa`.
