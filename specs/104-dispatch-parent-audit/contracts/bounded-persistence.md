# Contract: Bounded Dispatch Persistence

- Dispatch queries and outbox claims apply stable ordering and limit at the provider.
- Retention and cleanup traverse stable continuation pages and eventually inspect every eligible record.
- Conditional deletion succeeds only for the exact terminal snapshot previously inspected.
- In-memory and Groundwork implementations obey the same provider-neutral behavior.
