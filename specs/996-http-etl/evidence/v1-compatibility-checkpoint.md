# Retained v1 compatibility checkpoint

Issue [#3151](https://github.com/elsa-workflows/elsa-foundation/issues/3151), task T003. Production baseline: `b555bd8460270476e49496e519c69b65180e9505`. This is partial evidence, not completed successor-worker/native recovery.

## What is retained

The unchanged producer implementation publishes the ETL v1 artifact and a native Sequence workflow through the existing persisted authoring/publication fixtures. A real worker prepares input, reserves a batch and publishes JSONL output, then stops before acceptance. After worker termination and host shutdown, capture reads the durable partition expiry, binding pins and prepared identities from the stopped database. The fixture retains those databases, executable/artifact bytes, immutable inputs, owed receipt and actual output with length/SHA-256 checksums.

Capture is opt-in and absent from normal test discovery. Its manifest distinguishes the production baseline, capture checkout HEAD and the exact working-tree harness bytes. Catalog origin is a stable test identity supplied to the existing version factory before publication; identities are computed by the real producer rather than rewritten in a retained database. Existing fixture callers retain their default catalog origin.

## Proof and limits

Normal replay never invokes the producer. It copies retained data before migration or mutation, verifies the originals afterward, and checks:

- v1 canonical content hash and native pinned selection/artifact identity;
- retained native executable deserialization through the real runtime artifact store, without design/compiler services;
- runtime schema migration, prepared-input reconstruction and exact owed batch identity;
- successor claim using the stopped database's actual expiry, accepting the retained effect once with preserved bindings/prepared state, settled accounting and manifest identity.

This does **not** run a successor file worker or resume the surrounding native workflow from the retained old state. Host binding handles incorporate absolute configured paths; moving fixture data cannot recreate the old binding identity. No pins or identity-bearing rows are rewritten to conceal that limitation. T003 remains unchecked. A path-correct frozen old producer/new consumer journey or another valid portable mechanism is still required before delivery.

## Review corrections

Root review corrected replay that read original snapshot/output roots, a clock based on wall time, missing exact binding/prepared assertions, and incomplete harness provenance. A stricter replay failed on the receipt's stale lease expiry because heartbeat renewal had persisted a later expiry. Capture now reads after worker/host shutdown. A broader binary scan then found machine-specific CLR catalog origins in workflow.db; capture now supplies stable source identity through the existing fixture path and checks every retained payload for host-path prefixes. Failed/obsolete candidates were preserved separately; passing later checks do not erase those failures.

Root integration verification: focused build/test command `dotnet test Extensions/Etl/tests/Tests/Elsa.Etl.Tests.csproj --filter 'FullyQualifiedName~EtlV1CompatibilityFixtureTests|FullyQualifiedName~EtlPublishedWorkflowJourneyTests' --logger 'console;verbosity=minimal'` passed **5/5**, with no failures or skips. This includes the three retained-fixture tests and existing published Sequence/Flowchart journeys. The final delegated capture passed **1/1**; root independently checked all ten payload hashes, six harness hashes, three SQLite integrity checks and the expanded unchanged-producer comparison. Normal test discovery did not contain the opt-in capture test. Whitespace checks passed. Full ETL/architecture/e2e/maps gates remain for the integrated implementation; no production code changed here.

No HTTP production behavior is implemented by this checkpoint.
