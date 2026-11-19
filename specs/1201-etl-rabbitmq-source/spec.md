# Feature Specification: RabbitMQ ingestion with manual acknowledgements

**Feature Branch**: `agent/claude/2908-rabbitmq-source` (stacked on `agent/claude/2908-rabbitmq-delivery`)

**Created**: 2026-11-03

**Status**: Implemented; [evidence](evidence/README.md)

**Input**: The ingestion half of the fifth criterion of [#2908](https://github.com/elsa-workflows/elsa-foundation/issues/2908): "RabbitMQ JSON ingestion/delivery proves manual acknowledgements and publisher confirms around failure/replay boundaries." Delivery with publisher confirms is [spec 1200](../1200-etl-rabbitmq-delivery/spec.md). Part of [Program #2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892).

## Context

A v2 pipeline reads a CSV or JSON Lines file, an HTTP JSON feed or a SQL table. None of these is a message queue.

This slice adds a finite `rabbitmq` source. Preparation drains up to an authored number of messages from a host-approved queue without acknowledging them, retains their JSON bodies as a JSON Lines input snapshot, and acknowledges them only after the runtime has durably recorded that snapshot as the run's prepared input. The run then processes the snapshot like a JSON Lines source.

- A worker that dies, or a preparation that fails, before the record leaves the messages unacknowledged; the broker requeues them when the connection closes, and the next preparation attempt reads them again.
- A worker that dies, or a connection that is lost, after the messages were received and before the broker received the acknowledgement, can leave the run prepared with messages the broker then requeues; a later run reads them again. Ingestion is therefore at least once. The window runs from the receipt to the acknowledgement: the runtime cannot tell that a receipt's connection was lost before it records the snapshot.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Load a queue's messages (Priority: P1)

An author points a pipeline's source at a host-approved RabbitMQ binding and a queue, declares the JSON fields to read, and sets the most messages one run takes. A run reads the queue's current messages, up to that bound, and delivers their records.

**Independent Test**: against a real RabbitMQ container, a run over a queue holding JSON messages delivers one record per message and leaves the queue empty.

**Acceptance Scenarios**:

1. **Given** a queue with JSON object messages, **When** a run prepares, **Then** the snapshot holds one line per message in queue order, each record is processed with the declared fields, and the messages are acknowledged after the snapshot is recorded.
2. **Given** more messages than the run's maximum, **When** it prepares, **Then** it takes at most that many, and fewer if its drain time bound is spent first, and leaves the rest in the queue for the next run.
3. **Given** an empty queue, **When** a run prepares, **Then** it settles as success with no records.
4. **Given** a message whose body is not valid JSON, or valid JSON but not an object, **When** its batch is built, **Then** the record is routed to errors as `etl.jsonl.record-invalid` or `etl.jsonl.record-not-object`, and the message is still acknowledged with the others.

### User Story 2 — Never lose a message to a failure (Priority: P1)

**Acceptance Scenarios**:

1. **Given** a preparation that fails or whose worker dies before the snapshot is recorded, **Then** no message is acknowledged, the broker requeues them, and the next attempt reads them again.
2. **Given** a snapshot that is recorded, **Then** its messages are acknowledged; until the acknowledgement reaches the broker they may be redelivered to a later run.
3. **Given** a broker that cannot be reached, or closes the connection while messages are being received, **Then** preparation records nothing and is retried after its lease lapses (a connection lost after the messages were received is the at-least-once window above); a deterministic refusal (access refused, a missing queue, a stream queue, which `basic.get` cannot read, failed authentication) fails the run with `etl.rabbitmq-read-rejected`.

### User Story 3 — Author and inspect a RabbitMQ source (Priority: P2)

An author picks a RabbitMQ source binding in Studio, names the queue, declares the fields and sets the message maximum.

**Independent Test**: component tests and a fixture-backed browser test with axe at 320 px and 1024 px.

### Edge Cases

- **Names.** The queue is 1 to 255 characters of letters, digits, `_`, `-`, `.` or `:` (`source.rabbitmq-name-invalid`). The host binding must allow the queue, compared exactly (`etl.rabbitmq-queue-not-allowed`). The runtime never declares queues.
- **Maximum.** `MaxMessages` is 1 to 100,000 (`source.rabbitmq-max-messages-invalid`). A run also stops taking messages after five minutes, having taken at least one, because the broker closes a channel whose deliveries stay unacknowledged past its `consumer_timeout` (30 minutes by default), and a channel closed for that reason is retried. The snapshot is also bounded by `MaxSnapshotBytes`: a run stops taking messages at the first one that would exceed it, and that message stays in the queue. It stops in the same way at the first message that would take the run's retained input (the snapshot, its index and the lookup, which is published first) past `http.maxRetainedInputBytes`, which the store would otherwise refuse, so a backlog larger than that budget drains over several runs. A single message larger than the bound fails preparation with `etl.snapshot-too-large`.
- **Bodies.** A body without a line break is written to the snapshot as received; a valid JSON body spread over several lines has each line break replaced by a space, which is insignificant whitespace there; a leading byte order mark is dropped, and a body that still starts with one after that is rejected as a record error; any other body with a line break, or a blank body, is written so that the JSON Lines reader rejects it as a record error. Records carry no message properties or headers.
- **Fields.** The declared fields follow the JSON Lines source's rules (spec 1190): JSON pointers, scalar types, required flags.
- **Requeue limits.** A quorum queue counts each return; RabbitMQ 4 drops a message after 20 by default, so messages a pipeline keeps failing on are lost unless the queue has a dead-letter exchange or a higher delivery limit. The README states this.
- **One reader per queue.** Two runs reading the same queue at once each take different messages; the runtime does not serialise them.
- **Bindings.** A RabbitMQ binding with role `Source` lists `AllowedQueues`. Its pinned handle hashes the target and the allowed queues and exchanges, never the password; a destination binding's handle keeps spec 1200's shape, so runs pinned under spec 1200 still resolve. Without `Elsa.Etl.RabbitMq`, admission refuses the run with `etl.rabbitmq-unavailable`.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001** `EtlRabbitMqSource(Binding, Queue, Fields, MaxMessages)` is a new source of kind `rabbitmq`. Its connector kind is `EtlConnectorKind.RabbitMq`. Existing artifacts and identities are byte-identical.
- **FR-002** The compiler and the worker's executable reader enforce the queue name, the maximum and the JSON Lines field rules.
- **FR-003** `EtlHostRabbitMqBindingOptions` gains `AllowedQueues`; source bindings are resolved for role `Source` and pin a handle without secrets.
- **FR-004** `IEtlRabbitMqConsumer`, implemented by the driver package, receives up to a number of messages from a queue without acknowledging them, handing each body to the runtime as it arrives and stopping at the first body the runtime refuses, which stays queued, or once its drain time bound is spent after at least one message; it returns a receipt that acknowledges them all or, when disposed unacknowledged, lets the broker requeue them. It classifies failures as transient or rejected.
- **FR-005** Preparation writes each received body to a JSON Lines snapshot as it arrives, refusing the first that would pass `MaxSnapshotBytes` or take the run's retained input (snapshot, index and lookup) past `http.maxRetainedInputBytes`, under an identity unique to the attempt, so a retried preparation never reuses a snapshot whose messages were requeued, and records it as the prepared input like a SQL snapshot.
- **FR-006** The connector execution acknowledges the receipt only after the store applies, or replays, the preparation record. Any other outcome, failure or cancellation disposes it unacknowledged.
- **FR-007** Studio offers the `rabbitmq` source with binding, queue, fields and maximum. The ETL README, the driver README and the Core `EXTENSION_POINTS.md` describe the source and the acknowledgement order, and the ETL README its at-least-once window.

## Success Criteria *(mandatory)*

- **SC-001** On a real RabbitMQ broker: received messages stay unacknowledged until the receipt acknowledges them; a receipt disposed unacknowledged, or a connection that drops, returns them to the queue; the maximum, the byte bound and the drain time bound leave the remainder queued; a missing queue is rejected and an unreachable broker transient.
- **SC-002** Through the worker and store with a test consumer: a run delivers the messages' records and acknowledges after the record; a preparation whose record is refused, or that fails, never acknowledges; an empty queue settles as success.
- **SC-003** Every behavioural requirement has a test that fails when its guard is mutated, recorded as a bite-proof, except the guards the evidence README names.
- **SC-004** An actual Workbench journey drains a real queue into a JSON Lines file from a native Flowchart, and the queue is empty afterwards.
- **SC-005** Existing pipelines keep their bytes and identities; the ETL, architecture and container suites pass.

## Assumptions and boundaries

- **Deferred:** a continuous RabbitMQ source (consuming as messages arrive), message properties and headers as fields, per-run exclusive consumption, dead-lettering of invalid messages (they are acknowledged and routed to the error route), and a RabbitMQ connection type in the Connections domain.
- **Not changed:** the processing of a retained JSON Lines snapshot.
