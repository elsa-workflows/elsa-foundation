# Tasks: RabbitMQ ingestion with manual acknowledgements

**Input**: [spec.md](spec.md), [plan.md](plan.md)

- [ ] T001 `EtlRabbitMqSource`, its JSON kind, PublicAPI. Compiler and reader rules. Tests.
- [ ] T002 `IEtlRabbitMqConsumer` and `IEtlRabbitMqReceipt`; a test consumer that records acknowledgements.
- [ ] T003 Bindings: `AllowedQueues`, source resolution, the pinned handle.
- [ ] T004 Preparer: receive, write the snapshot under a per-attempt identity, process it as JSON Lines; every source-pin site admits `RabbitMq`.
- [ ] T005 Execution: acknowledge only after the preparation record is applied or replayed; dispose otherwise. Journeys.
- [ ] T006 Driver `RabbitMqEtlConsumer`; the container suite on a real broker.
- [ ] T007 Studio authoring with Vitest and Playwright.
- [ ] T008 READMEs, `EXTENSION_POINTS.md`, bite-proofs, the Workbench journey, the evidence README and the suites.
