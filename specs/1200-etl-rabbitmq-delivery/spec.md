# Feature Specification: RabbitMQ delivery with publisher confirms

**Feature Branch**: `agent/claude/2908-rabbitmq-delivery` (stacked on `agent/claude/2908-sql-watermark`)

**Created**: 2026-11-03

**Status**: Implemented — [PR #3725](https://github.com/elsa-workflows/elsa-foundation/pull/3725); [evidence](evidence/README.md)

**Input**: The delivery half of the fifth criterion of [#2908](https://github.com/elsa-workflows/elsa-foundation/issues/2908): "RabbitMQ JSON ingestion/delivery proves manual acknowledgements and publisher confirms around failure/replay boundaries." Ingestion with manual acknowledgements is the next slice. Part of [Program #2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892).

## Context

A v2 pipeline delivers its main route to a JSON Lines or CSV file, an HTTP endpoint or a SQL table. None of these is a message broker.

This slice adds a `rabbitmq` destination. Each accepted record of a batch is published to an authored exchange and routing key as one persistent JSON message. The runtime records the route only after the broker has confirmed every message of the batch (publisher confirms), so an accepted batch is never recorded as delivered while a message could still be lost.

RabbitMQ has no ledger the runtime could consult, so delivery is at least once. A batch whose publication is interrupted, or whose worker dies after the confirms and before the record, is published again in full. Every message carries a message id derived from the effect identity and the record's position, identical on every replay, so a consumer can discard duplicates.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Publish a pipeline's records to an exchange (Priority: P1)

An author points a pipeline's main route at a host-approved RabbitMQ binding, an exchange and a routing key. Each record arrives as one message.

**Independent Test**: run a pipeline against a real RabbitMQ container with a queue bound to the exchange. The queue must hold one message per record, in batch order.

**Acceptance Scenarios**:

1. **Given** an exchange with a queue bound by the routing key, **When** a run delivers records, **Then** the queue holds one message per record, whose body is the record's mapped JSON object, in the batch's order.
2. **Given** a message, **Then** it is persistent, has content type `application/json`, the message id `{effectId}:{index}`, and the headers `elsa-etl-run-id`, `elsa-etl-batch-id`, `elsa-etl-effect-id` and `elsa-etl-record-index`.
3. **Given** the default exchange (an empty exchange name), **When** the routing key names a queue, **Then** the messages land in that queue.

### User Story 2 — Never record an unconfirmed batch (Priority: P1)

**Acceptance Scenarios**:

1. **Given** a batch, **When** the broker has confirmed every message, **Then** the runtime records the route, and only then may the batch be accepted.
2. **Given** a message the broker cannot route (the publish is mandatory and no queue matches), **Then** the run fails with `etl.rabbitmq-unroutable` and the route is not recorded.
3. **Given** a message the broker negatively acknowledges (for example a queue at its length limit with `reject-publish`), **Then** the route is not recorded, the worker yields, and the batch is published again later.
4. **Given** a connection that cannot be opened or that closes during publication, **Then** the route is not recorded and the worker yields. A broker outage pauses the run rather than failing it.
5. **Given** a deterministic refusal (access refused, an exchange that does not exist, or failed authentication), **Then** the run fails with `etl.rabbitmq-publish-rejected` and does not retry.

### User Story 3 — Replay with stable identities (Priority: P1)

**Independent Test**: in real worker processes, kill the worker after the broker confirmed a batch and before the runtime recorded it. The replacement publishes the batch again; the second copy has the same message ids and headers as the first, and the run settles with every record accounted for.

**Acceptance Scenarios**:

1. **Given** a batch confirmed by the broker but not recorded, **When** a replacement worker delivers it, **Then** every message is published again with the same message id, headers and body.
2. **Given** a batch interrupted after some messages were confirmed, **When** it is retried, **Then** the whole batch is published again; the earlier messages arrive twice, with identical message ids.

### User Story 4 — Author and inspect a RabbitMQ destination (Priority: P2)

An author picks a RabbitMQ binding for the main route in Studio and names the exchange and routing key. An operator sees a run's recorded RabbitMQ deliveries.

**Independent Test**: component tests cover the authoring controls, and a fixture-backed browser test checks them with axe at 320 px and 1024 px.

### Edge Cases

- **Main route only.** The error route's records are delivered by the existing kinds; an error route of kind `rabbitmq` is refused with `destination.rabbitmq-route-unsupported`.
- **Names.** The exchange is empty (the default exchange) or matches `[A-Za-z0-9_.:-]{1,255}`; the routing key is 0 to 255 printable ASCII characters (0x21 to 0x7E). A violation is refused with `destination.rabbitmq-name-invalid`. The host binding must allow the exchange, compared exactly, or resolution fails with `etl.rabbitmq-exchange-not-allowed`.
- **Topology.** The runtime never declares exchanges, queues or bindings. They are the operator's.
- **Ordering.** Messages of one batch are published in order on one channel. Batches of different partitions or shards interleave in an unspecified order. A replayed batch repeats its messages after later ones may already have arrived.
- **Size.** A message body is one record's JSON object, already bounded by the pipeline's output byte limit.
- **Credentials.** The binding holds an AMQP URI, supplied through host configuration. Neither the artifact nor the persisted pins hold it. The pinned handle hashes the scope, alias, role, revision, host, port, virtual host, user name and allowed exchanges, never the password.
- **Packages.** `IEtlRabbitMqPublisher` is in `Elsa.Etl.Core`, with no driver. The shared writer and bindings are in `Elsa.Etl.Runtime`. `Elsa.Etl.RabbitMq` (RabbitMQ.Client) registers the publisher. Without it, binding resolution fails at admission with `etl.rabbitmq-unavailable`, and a worker that claims such a run fails it with the same code.
- **Mixed versions.** As for spec 1197, a worker without this slice cannot read a `rabbitmq` artifact; all workers in a scope must run this slice before such a pipeline is admitted.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001** `EtlRabbitMqDestination(Binding, Exchange, RoutingKey)` is a new destination of kind `rabbitmq`. `EtlConnectorKind` gains `RabbitMq`, appended, so existing identities are unchanged. Existing artifacts and identities are byte-identical.
- **FR-002** The compiler and the worker's executable reader enforce the main-route and name rules in Edge Cases.
- **FR-003** `EtlRuntimeFeature.RabbitMqBindings` holds `EtlHostRabbitMqBindingOptions`: scope, alias, revision, role `MainOutput`, AMQP URI and allowed exchanges. Resolution pins a handle without secrets.
- **FR-004** `IEtlRabbitMqPublisher`, implemented by the driver package, describes a connection's target without secrets and publishes a list of messages on one confirming channel, returning when every message is confirmed. It reports a return as unroutable and a negative acknowledgement as transient, and classifies other exceptions as transient or rejected.
- **FR-005** The route body of a `rabbitmq` route is the batch's mapped records as a JSON array, retained like an HTTP or SQL body. The effect identity and body hash are as for other routes.
- **FR-006** The runtime's writer turns one route body into messages (FR US1.2) and publishes them through the publisher. Infrastructure exceptions are wrapped at the boundary, as transient (the worker yields) or deterministic (an `EtlEffectException` with a code).
- **FR-007** After every message is confirmed, the output processor records the route through the route-publication transition with the expected reference keyed `EtlRuntimeIdentity.ConnectorRabbitMqEffectKey(effectId)`. Every kind switch that admits `Sql` for publication admits `RabbitMq`, sharing the recorded-publication effect mechanism value 2.
- **FR-008** Studio: the `rabbitmq` destination kind for the main route, with binding, exchange and routing key. Inspection labels recorded RabbitMQ deliveries.
- **FR-009** The ETL README, the Core README and the affected `EXTENSION_POINTS.md` files describe the destination, at-least-once delivery with stable message ids, the bindings, the package and the publisher contract.

## Success Criteria *(mandatory)*

- **SC-001** Against a real RabbitMQ container, the publisher delivers persistent messages with the US1.2 properties in order; an unroutable message is reported as unroutable, a negative acknowledgement as transient, a missing exchange as rejected and an unreachable broker as transient.
- **SC-002** In real worker processes, with a file-backed test publisher, a worker killed after the confirms and before the record is replaced, the batch is published again with identical message ids, and the run settles with every record accounted for.
- **SC-003** Through the worker and store, a transient failure yields and a later attempt delivers; an unroutable message fails the run without recording the route.
- **SC-004** Every behavioural requirement has a test that fails when its guard is mutated, recorded as a bite-proof, except the guards the evidence README names.
- **SC-005** An actual Workbench journey delivers to a real RabbitMQ container from a native Flowchart, and the queue holds one message per record.
- **SC-006** Existing pipelines keep their bytes and identities. The ETL suite passes, and the new container suite passes in CI's container shard.

## Assumptions and boundaries

- **Deferred:** RabbitMQ ingestion with manual acknowledgements (the next slice); error routes of kind `rabbitmq`; declaring topology; per-record routing keys; broker-side deduplication; a RabbitMQ connection type in the Connections domain.
- **Not changed:** the ETL runtime and design stores stay on SQLite.
