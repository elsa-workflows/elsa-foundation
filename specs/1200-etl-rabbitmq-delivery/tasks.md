# Tasks: RabbitMQ delivery with publisher confirms

**Input**: [spec.md](spec.md), [plan.md](plan.md)

## Phase 1 — Model and compiler
- [ ] T001 `EtlRabbitMqDestination`, `EtlConnectorKind.RabbitMq` (appended), `ConnectorRabbitMqEffectKey`, PublicAPI. Test: existing artifact bytes and identities unchanged.
- [ ] T002 Compiler and executable-reader rules: main route only, exchange and routing-key names. Tests.

## Phase 2 — Contract and writer
- [ ] T003 `IEtlRabbitMqPublisher` with `EtlRabbitMqTarget`, `EtlRabbitMqMessage` and `EtlRabbitMqErrorKind`. An in-memory test publisher.
- [ ] T004 `EtlRabbitMqRouteWriter`: messages with stable ids and headers, publication, outcome and exception mapping. Tests.

## Phase 3 — Bindings, dispatch and publication
- [ ] T005 `EtlHostRabbitMqBindingOptions`, `HostRabbitMqBindings` (secret-free handle, allowed exchanges, publisher resolution), `HostConnectorBindings`, `EtlRuntimeFeature.RabbitMqBindings`.
- [ ] T006 Output processor dispatch and recording; execution injects the publisher; every publication site admits `RabbitMq`. Tests.
- [ ] T007 Journeys through the worker and store (delivery, transient yield, unroutable failure, missing publisher) and real worker processes killed between the confirms and the record.

## Phase 4 — Driver package and container suite
- [ ] T008 `Elsa.Etl.RabbitMq` (publisher, feature, README, PublicAPI).
- [ ] T009 The container suite on a real broker (properties and order, unroutable, nack, missing exchange, unreachable). CI tiers.

## Phase 5 — Studio, docs and evidence
- [ ] T010 Studio authoring and inspection, with Vitest and Playwright (axe).
- [ ] T011 READMEs, `EXTENSION_POINTS.md`, solution filters.
- [ ] T012 Bite-proofs, Workbench journey, evidence README, the ETL, architecture and container suites.
