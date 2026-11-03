# Implementation Plan: RabbitMQ delivery with publisher confirms

**Branch**: `agent/claude/2908-rabbitmq-delivery` | **Spec**: [spec.md](spec.md)

## Summary

The plan adds a main-route `rabbitmq` destination in the shape of spec 1197's `sql` destination:
- **Contract.** `IEtlRabbitMqPublisher` lives in Core, with no driver.
- **Writer.** A shared writer in the runtime turns a retained route body into messages with stable ids and publishes them through the contract.
- **Driver.** `Elsa.Etl.RabbitMq` implements the contract with RabbitMQ.Client 7 on a channel with publisher confirmation tracking.
- **Recording.** The route-publication transition records a confirmed batch like a SQL delivery.

## Technical context

- .NET 10; RabbitMQ.Client 7.2 in the driver package only.
- Tests use xUnit with Testcontainers.RabbitMq 4.15 (a new container suite), plus Vitest and Playwright.
- The unit tests and worker processes use an in-memory or file-backed test publisher, so the protocol is tested without a broker. The container suite proves the real broker.

## Constitution check

- **Packages and dependencies** (§2.1, §2.7, §2.20): the contract carries no external dependency; the driver stays in its own package, which references only `Elsa.Etl.Core`.
- **Wrapping** (§2.23.5): driver exceptions are classified by the driver package and wrapped at the writer boundary.
- **Compatibility:** existing artifacts and identities are byte-identical (`RabbitMq` is appended); RabbitMQ shares the recorded-publication `EffectMechanism` value 2; no migration.
- **Extension points.** The Core `EXTENSION_POINTS.md` gains `IEtlRabbitMqPublisher`, a single-implementation adapter; the driver package gets a README.
- **Tests.** Behaviour tests cover the compiler, bindings, writer, publication sites and journeys. The driver feature's registration is plain, so the §2.23.1 composition test covers it. The new container project is listed in `test-tiers.json` and `containerSuites`.
- **Naming.** `EtlRabbitMqDestination`, `IEtlRabbitMqPublisher`, `EtlHostRabbitMqBindingOptions` and `EtlRabbitMqFeature`, each within the component cap.

## Changes by project

- **Core:** `EtlRabbitMqDestination` and its JSON kind; `EtlConnectorKind.RabbitMq`; `IEtlRabbitMqPublisher` with `EtlRabbitMqTarget`, `EtlRabbitMqMessage` and `EtlRabbitMqErrorKind`; `EtlRuntimeIdentity.ConnectorRabbitMqEffectKey`; PublicAPI.
- **Design:** the compiler's `rabbitmq` rules and size budget.
- **Runtime:** `EtlHostRabbitMqBindingOptions`, `HostRabbitMqBindings`, `HostConnectorBindings` and `EtlRuntimeFeature.RabbitMqBindings`; `Output/EtlRabbitMqRouteWriter`; the output processor's route format and dispatch; execution injects the publisher; the executable reader admits `rabbitmq`; the API's effect state.
- **Persistence:** the acceptance, output and inspector stores and the record codec admit `RabbitMq` wherever they admit `Sql` for publication.
- **Driver package:** `src/RabbitMq/Elsa.Etl.RabbitMq.csproj` with `EtlRabbitMqFeature` and `RabbitMqEtlPublisher`.
- **Studio:** `etlTypes.ts`, the destination picker and the exchange and routing-key controls, with tests; an inspection label.
- **Tests:** compiler, bindings, writer (test publisher), journeys, worker-process kill between the confirms and the record (file-backed publisher in the worker host); the container suite `tests/RabbitMq/ProviderTests`.
- **CI and docs:** `test-tiers.json`, solution filters, READMEs, `EXTENSION_POINTS.md`, the evidence README, bite-proofs and a Workbench journey.
