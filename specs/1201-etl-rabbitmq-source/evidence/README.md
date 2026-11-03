# Spec 1201 evidence: RabbitMQ ingestion with manual acknowledgements

Source: the branch `agent/claude/2908-rabbitmq-source`, stacked on `agent/claude/2908-rabbitmq-delivery` (spec 1200).

## Behaviour tests

| Requirement | Test |
|---|---|
| FR-001, FR-002: model and authoring rules | `A_rabbitmq_source_compiles_round_trips_and_leaves_existing_artifacts_unchanged`, `A_queue_or_maximum_the_broker_or_runtime_cannot_take_is_refused`, `A_queue_name_longer_than_a_protocol_short_string_is_refused`, and the worker's re-check `Reader_refuses_a_rabbitmq_source_the_compiler_would_refuse`. |
| FR-003: bindings | `A_source_binding_allows_its_queues_exactly_and_needs_the_consumer`: the queue is allowed exactly, a source needs the consumer, and the allowed queues are part of the pinned handle. |
| FR-005: the snapshot | `Each_message_body_becomes_exactly_one_snapshot_line` (raw, compacted multi-line JSON, and invalid or blank bodies as a rejected line), `Preparation_writes_the_received_messages_and_hands_their_receipt_over_unacknowledged`, `A_retried_preparation_never_reuses_a_predecessor_snapshot`, and `A_failed_or_transient_receive_prepares_nothing_and_a_too_large_message_is_left_queued`. |
| FR-006, US1, US2: acknowledgement after the record, through the worker and store | `A_queue_is_drained_into_records_and_acknowledged_only_after_the_prepared_run_is_recorded` (the log reads receive, record, acknowledge; the invalid body is one rejected record), `A_run_takes_at_most_its_maximum_and_leaves_the_rest_queued_and_an_empty_queue_succeeds`, and `A_prepared_run_the_store_refuses_to_record_never_acknowledges_its_messages` (the messages are requeued). A decorator store notes the preparation record in the test consumer's log. |
| SC-001: a real broker | Container suite `EtlRabbitMqConsumerProviderTests` (3 tests, beside spec 1200's 10) on RabbitMQ 4.1: received messages are unacknowledged and return to the queue when the receipt closes unacknowledged; acknowledged ones are gone; the maximum and the byte bound leave the rest queued, and a first message over the bound is left queued and flagged; an empty queue returns at once; a missing queue is rejected and an unreachable broker transient. |
| FR-007: Studio | Vitest `connector-pipeline-editor` (the source kind, its approvals, queue, maximum and fields, and the finite run) and Playwright `etl-connector-authoring` (saved source, no horizontal scroll and no axe violations at 320 and 1024 px). |

At `9b4c621db`, the ETL suite passed 1295 of 1295, the container suite 13 of 13 and the architecture suite 614 of 614 (after `tools/architecture/restore-ci-project-graph.sh`). Studio ETL Vitest passed 123 of 123 and the Playwright spec 18 of 18 on the tree committed as `a4bcd0b52`.

## Bite-proofs

[`bite-proofs.json`](bite-proofs.json) has 17 entries, all recorded at `9b4c621db`, and all bite: the compiler's queue and maximum rules; the worker's queue and maximum re-checks; the binding's allowed queue, its need for the consumer and the queues in its handle; the snapshot writer's line-break and blank-body handling; the per-attempt snapshot identity; the preparer requeuing on failure; the execution acknowledging at all and only after the record is applied or replayed; the JSON Lines processing view; and, on the real broker, the driver's manual acknowledgement, its multiple acknowledgement and its too-large flag.

**Not bite-proven or not tested:**
- **The crash window after the record and before the acknowledgement.** Its outcome, the broker redelivering the messages to a later run, follows from the broker's own semantics, which the container suite proves for an unacknowledged receipt; no test kills a worker inside that window.
- **A worker process killed before the record.** Equivalent to a receipt closed unacknowledged, which the container suite proves on the broker and the journeys prove through the runtime; no test kills a worker process holding a real broker connection.
- **Guards tested but not bite-proven:** the driver rejecting a message that does not fit back to the queue (closing the channel would requeue it as well), the record codec's and runtime API's acceptance of a `RabbitMq` source pin, the acknowledgement failure being logged rather than failing the run, and the Studio controls.

## Actual Workbench journey

[`rabbitmq_source_workbench_journey.py`](rabbitmq_source_workbench_journey.py) starts its own RabbitMQ 4.1 container and its own Workbench on ephemeral loopback ports and publishes five messages, one not JSON. A RabbitMQ → JSON Lines definition with a maximum of three messages runs twice from native Flowcharts:
- **first:** takes three messages, delivers Ada and Bram and routes the invalid body to errors, through the BusinessError port; two messages stay queued and none is left unacknowledged;
- **second:** takes Cleo and Dee through the Done port; the queue is then empty with nothing unacknowledged.

All eight checks pass. The summary is [`workbench-journey-summary.json`](workbench-journey-summary.json), recorded at `d747a18c9` with a clean tree. Queue counts are read live with `rabbitmqctl`; the management API's statistics lag by seconds.

## Limits

These are stated in the ETL README:
- **At least once:** messages whose acknowledgement does not reach the broker are read again by a later run; records carry no message id.
- **Finite:** a run drains what is queued, up to its maximum; a continuous source is deferred.
- **Two runs reading one queue** take different messages.
