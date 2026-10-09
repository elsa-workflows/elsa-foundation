# External input reference across process restart

Spec198 T019 is accepted for this bounded proof. On 8 October 2026 the focused test passed 1/1, followed by the complete HTTP integration project at 40/40, with zero failed or skipped tests. Root reviewed the changed fixture, actual TRX results, conserved source hashes, stage exits, gate messages and an independent read-only database/provider-operation check. The [machine-readable evidence](external-input-restart.json) pins both runs and retained failed attempts.

The production source is unchanged from `b46f5587a9f187ed27912eb99d36fa5d361b13ec`; this increment changes test infrastructure only. The candidate annotation remains conditional on the separate [fused recovery correction #2497](https://github.com/elsa-workflows/elsa-foundation/issues/2497), final review and delivery gates.

## Demonstrated boundary

The fixture publishes a distinct HttpEndpoint / SetVariable / WriteHttpResponse workflow through the normal publisher, with Body using a stable test-only `IExternalPayloadStore` implementation. The captured historical External artifact remains immutable. The externally stored payload is Alice Smith and the pinned input envelope retains its type, storage profile, locator and metadata.

All three stages read back Coalesced mode with cap 2 and fusion disabled. At the held ActivityStarted boundary, the parent independently reads durable Running state, the exact external Body reference and bytes, the matching Pending InvokeActivity outbox, and the live execution-owner lease/fence. After those assertions it kills the process without releasing the barrier. It checks the same durable state again, waits for actual lease expiry, and starts a fresh process against the same owned database, keys and payload store. It does not resend the HTTP request or inject recovery work.

Normal resumption reads the same locator and payload hash through a distinct provider instance in the fresh process, delivers the pending continuation, and completes the same execution with the original request/artifact and committed HTTP 200, text/plain, Alice Smith instruction. Root independently confirmed Completed state, the exact Delivered outbox row, zero target scheduler rows and incidents, and distinct writing/reading process and provider identities. A consistent SQLite backup preserves the final readback state without modifying the original database.

The complete 40-test project also executes the existing inline-input process-loss test and publication/count controls. Its passing result does not repair earlier failed attempts or prove the separate fusion boundary.

## Failed fixture attempts and limits

Attempts 1–2 stopped on compilation problems. Attempt 3 selected a non-durable Scheduled cut at cap 1; attempt 4 exposed incomplete propagation of cap 2 to the child stages. Attempt 5 incorrectly filtered encoded relational identities using a raw ID. Attempt 6 incorrectly decoded the raw IntentKind projection. Attempt 7 applied the protected outer-document string converter to embedded ordinary scheduler JSON. Those failures are retained separately. The final reader uses encoded relational identity predicates, the raw IntentKind token, protected outer ContentJson deserialization, and default nested JsonElement deserialization matching the production dispatcher. This corrects test readers, not runtime recovery.

The provider is test-only and uses the supported external-payload contract. This proof does not promise the original HTTP connection survives process loss, demonstrate all external storage providers or all crash points, resolve #2497, measure latency, or establish final PostgreSQL query savings. It proves persisted-input recovery into an equivalent committed response and terminal output at the specified boundary.
