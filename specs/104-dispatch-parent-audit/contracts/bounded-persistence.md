# Contract: Bounded Dispatch Persistence

- Dispatch queries and outbox claims apply stable ordering and limit at the provider.
- Dispatch physical routes compile within SQL Server's index limits; provider admission is
  executable without a database connection.
- Workflow test-scope IDs are limited to 128 UTF-16 code units and runtime post-commit intent kinds
  to 230 so persisted values cannot exceed their portable composite-index projections.
- Retention and cleanup traverse stable continuation pages and eventually inspect every eligible record.
- Conditional deletion succeeds only for the exact terminal snapshot previously inspected.
- In-memory and Groundwork implementations obey the same provider-neutral behavior.
