# Runtime Efficiency

- **Status:** Active (2026-10-10). Merges [Runtime Database Access](runtime-db-access.md) and
  [Runtime Throughput](runtime-throughput.md).
- **Area:** Workflow runtime: database access per activity, persistence modes, concurrent correctness.
- **Steward:** Sipke.
- **Program:** [#2531](https://github.com/elsa-workflows/elsa-foundation/issues/2531); persistence-mode epic
  [#2564](https://github.com/elsa-workflows/elsa-foundation/issues/2564). Decisions: [ADR 0080 D2](../adr/0080-elsa-4-simplification-decisions.md#d2--persistence-modes-balanced-by-default).

Deterministic EF command-count budgets are the yardstick (ADR 0080 D2 amends ADR 0073 D7). The former boundary
against a default cadence flip is lifted: Balanced becomes the default.
