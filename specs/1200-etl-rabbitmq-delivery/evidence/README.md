# Spec 1200 evidence: RabbitMQ delivery with publisher confirms

Source: the branch `agent/claude/2908-rabbitmq-delivery`, stacked on `agent/claude/2908-sql-watermark` (spec 1199).

## Behaviour tests

| Requirement | Test |
|---|---|
| FR-001: model and compatibility | `A_rabbitmq_destination_on_the_main_route_compiles_and_round_trips` and `Existing_pipelines_keep_their_bytes_and_the_connector_kinds_keep_their_values` (`RabbitMq` is appended as kind 4, and an artifact without the destination carries no `rabbitmq` kind; no pre-change artifact hash is pinned). |
| FR-002: compiler and worker rules | `An_exchange_or_routing_key_the_broker_cannot_take_is_refused`, `Names_longer_than_a_protocol_short_string_are_refused`, `Only_the_main_route_can_publish_to_rabbitmq`, and the worker's re-check `Reader_refuses_a_rabbitmq_destination_the_compiler_would_refuse`. |
| FR-003: bindings | `A_binding_pins_a_secret_free_handle_and_refuses_an_exchange_it_does_not_allow`: the handle ignores the password and changes with the host, port, virtual host, user and allowed exchanges; the exchange is allowed exactly; an invalid URI, two current revisions and a missing publisher are refused. |
| FR-004, FR-006: the writer | `The_writer_publishes_one_message_per_record_with_stable_ids_and_headers` (a replay republishes identical messages), `A_negative_acknowledgement_or_a_transient_failure_yields_and_a_refusal_fails_with_a_stable_code`, `A_retained_body_that_is_not_an_array_of_objects_is_refused_before_publishing`. |
| FR-007: no second publication of a recorded route | `A_recorded_rabbitmq_route_is_not_published_again_when_its_batch_is_dispatched_again`: a batch dispatched again after its route was confirmed and recorded, as while an error route retries, publishes nothing. |
| FR-005, FR-007, US1, US2: through the worker and store | `A_csv_pipeline_publishes_each_accepted_record_as_one_confirmed_message`, `A_broker_that_fails_or_refuses_a_message_pauses_the_run_until_every_message_is_confirmed`, `An_unroutable_message_fails_the_run_without_recording_the_route`, `A_rabbitmq_destination_without_its_package_is_refused_at_admission`. |
| US3, SC-002: real worker processes | `A_worker_killed_after_the_broker_confirmed_a_batch_is_replaced_and_the_batch_is_published_again_with_the_same_ids`: the predecessor is killed at the `BeforePublicationRecord` gate after its batch was confirmed; the replacement publishes it again with the same id, headers and body, and the run settles with both accepted records. The worker processes publish through a file-backed test publisher. |
| SC-001: a real broker | Container suite `Elsa.Etl.RabbitMq.ProviderTests` (7 tests) on RabbitMQ 4.1: confirmed messages land in order with their id, content type, persistent delivery mode and headers; the default exchange routes by queue name; a return is unroutable; a negative acknowledgement from a `reject-publish` queue at its length limit yields; a missing exchange and refused credentials are rejections; an unreachable broker yields; the target excludes the password. |
| FR-008: Studio | Vitest `connector-pipeline-editor` (the exchange and routing-key controls, RabbitMQ approvals only, main route only) and `runInspection` (the route label); Playwright `etl-connector-authoring` (saved route, no horizontal scroll and no axe violations at 320 and 1024 px). |

At `069d41663` (review round 2), the ETL suite passed 1269 of 1269 and the architecture suite 614 of 614 (after `tools/architecture/restore-ci-project-graph.sh`). At `5fc2122e4` (review round 1), the container suite passed 7 of 7; the driver has not changed since. At `78f18b250`, Studio ETL Vitest passed 122 of 122; Studio has not changed since. The Playwright spec passed 16 of 16 on the tree committed as `0c2e5570a`.

## Bite-proofs

[`bite-proofs.json`](bite-proofs.json) has 28 entries, and all bite. Four, recorded at `069d41663` (review round 2), cover the host, port and allowed exchanges in the pinned handle and the routing key's length. Six, recorded at `5fc2122e4` (review round 1), cover dispatch skipping a route the owed batch already records, the missing-publisher refusal, the handle excluding the AMQP URI, two current revisions being ambiguous, the writer failing an unroutable batch, and the driver treating an unreachable broker as transient. The other 18, recorded at `78f18b250`, cover the compiler's main-route, exchange and routing-key rules and the exchange's length; the worker's error-route and name re-checks; the binding's allowed exchange and the virtual host and user in its handle; the writer's negative acknowledgement and transient yields and its per-record message id; dispatch recording only after every confirm; the store's `.amqp` publication key; and, on the real broker, the driver's 404 rejection, return-as-unroutable mapping, mandatory flag and persistent delivery mode.

**Not bite-proven or not tested:**
- **Guards tested but not bite-proven:** the default-exchange routing, the content type and headers set by the driver, the binding's revision check (shared with the SQL bindings' pattern), the cross-catalog ambiguity check in `HostConnectorBindings` (untested for RabbitMQ), and the Studio controls.
- **Untested:** the driver's classification of failed authentication through `BrokerUnreachableException`'s inner exception on every broker version, and of reply codes 403, 406 and 530; only refused credentials and a missing exchange are exercised.
- **A broker restart during a publication.** The unreachable broker and the negative acknowledgement are proven; a connection closed mid-batch is classified like an unreachable broker but not exercised.

## Actual Workbench journey

[`rabbitmq_workbench_journey.py`](rabbitmq_workbench_journey.py) starts its own RabbitMQ 4.1 container (the management API only declares the topology and reads the queue) and its own Workbench on ephemeral loopback ports. Two JSON Lines → RabbitMQ definitions run from native Flowcharts:
- **delivered:** four records reach the queue bound to `etl.people` by `people.loaded`, through the Done port, each persistent JSON with the message id `{effectId}:0` and the run's id in its headers;
- **unroutable:** a routing key no queue matches settles the run as a business error with `etl.rabbitmq-unroutable`, through the BusinessError port, and nothing reaches the queue.

All eight checks pass. The summary is [`workbench-journey-summary.json`](workbench-journey-summary.json), recorded at `bea009757` with a clean tree. The first run, at `78f18b250`, passed the same checks. The unroutable run keeps one outstanding record after it settles; a SQL run settled by a rejected delivery does the same, so the journey checks accounting on the delivered run only, and the defect is #3724.

## Limits

These are stated in the ETL README:
- **At least once.** A replayed batch is published again in full; consumers deduplicate by message id.
- **Ordering.** Messages of one batch keep their order; batches of different partitions interleave, and a replayed batch can arrive after later ones.
- **Main route only**, and the runtime never declares topology.
