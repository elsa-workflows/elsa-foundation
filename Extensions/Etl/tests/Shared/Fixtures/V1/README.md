# Retained ETL V1 compatibility evidence

This directory contains producer-era output captured through the unchanged ETL V1 implementation at production-source baseline `b555bd8460270476e49496e519c69b65180e9505`. Before capture, `capture-v1-compat-fixtures.sh` compares all tracked `**/src/**`, `**/packages.lock.json`, `**/*.props`, and `**/*.targets` paths, plus the exact root `global.json` and `NuGet.Config` paths, with that baseline. The harness itself is run from the working tree; `captureBaseCommit` records the checkout's HEAD, which does not contain those uncommitted harness files. The manifest records SHA-256/length provenance for each retained payload and the capture script, test, helpers, and test-project file used by the harness.

The capture test uses the existing persisted ETL design/publication fixture, native workflow publisher, runtime EF store, prepared snapshot store, and a real WorkerHost process paused after output publication but before batch acceptance. Its CLR catalog source identity is the stable fixture value `fixture://elsa-etl/published-workflow/v1`; assembly scanning and native publication still use the existing production paths. It retains the published ETL artifact, native workflow database/executable, runtime SQLite database, immutable snapshots, actual output chunk, and the worker's acceptance receipt. The fixture's run has a prepared source and an unsettled owed reservation.

After crashing the worker and stopping the host, capture reopens the stopped runtime database through the normal store and records the durable partition expiry, bindings, and prepared-run identities before copying the database. Replay sets its test clock to that persisted expiry, so successor claim behavior does not depend on the machine's clock when the fixture is replayed.

The runtime database stores path-derived host-binding handles but no plaintext host paths. The capture scans every retained payload before and after writing the manifest, rejecting `/Users/`, `/private/var/`, `/tmp/`, and the runtime/publication temp roots. The fixture retains original host-binding handles unchanged; portable replay copies its database and content-addressed objects to a temporary directory and exercises store migration/accounting from copied snapshots and output objects. It does not retarget the pins or run the file worker against new host paths. Full successor-worker/native resume proof remains outstanding until a portable host-binding mechanism exists or a separately frozen pre-change producer is run in the replay root.

To capture to the checked fixture location, run:

```bash
bash Extensions/Etl/tests/Shared/capture-v1-compat-fixtures.sh
```

The script refuses to overwrite an existing capture. To produce a candidate elsewhere, pass an empty destination directory as its only argument. Compare its manifest and payloads before replacing retained evidence.
