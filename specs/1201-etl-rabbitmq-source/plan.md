# Implementation Plan: RabbitMQ ingestion with manual acknowledgements

**Branch**: `agent/claude/2908-rabbitmq-source` | **Spec**: [spec.md](spec.md)

## Summary

A finite `rabbitmq` source in the shape of spec 1198's SQL source: preparation fills a JSON Lines snapshot and the run processes it as a JSON Lines source. What is new is the acknowledgement: the driver receives messages without acknowledging them, and the connector execution acknowledges them only after the preparation record is committed.

## Constitution check

- **Packages** (§2.1, §2.7, §2.20): `IEtlRabbitMqConsumer` is in Core without a driver; `Elsa.Etl.RabbitMq` implements it beside the publisher.
- **Wrapping** (§2.23.5): driver exceptions are classified by the driver and wrapped at the preparer boundary, as the SQL reader's are.
- **Compatibility:** `EtlConnectorKind.RabbitMq` (spec 1200) is reused for the source pin; no new persisted shape; existing artifacts keep their bytes.
- **Extension points:** the Core `EXTENSION_POINTS.md` gains `IEtlRabbitMqConsumer` and the acknowledgement order.
- **Tests:** compiler and reader rules, bindings, the preparer and execution ordering with a test consumer, the container suite on a real broker, a Workbench journey, Studio Vitest and Playwright.

## Changes by project

- **Core:** `EtlRabbitMqSource` and its JSON kind; `IEtlRabbitMqConsumer` and `IEtlRabbitMqReceipt`; PublicAPI.
- **Design:** the compiler's source rules and size budget.
- **Runtime:** `AllowedQueues`; source resolution in `HostRabbitMqBindings` and `HostConnectorBindings`; the preparer's snapshot writer and its per-attempt identity; the receipt handed to execution and acknowledged after the commit; the executable reader; the JSON Lines processing view of the source.
- **Persistence:** the record codec and stores admit a `RabbitMq` source pin wherever they admit a SQL one.
- **Driver:** `RabbitMqEtlConsumer` (basic.get without acknowledgement on one channel; multiple acknowledgement up to the last taken tag; channel close requeues).
- **Studio:** the source kind, queue, fields and maximum, with tests.
- **Tests and docs:** as in spec 1200, plus the evidence README, bite-proofs and a Workbench journey.
