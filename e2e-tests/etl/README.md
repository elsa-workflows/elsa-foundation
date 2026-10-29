# Finite file ETL end-to-end journey

For the execution-model program's source-bound **actual process-loss recovery** of the delivered native ETL path, see [the native HTTP reference](../../specs/1017-direct-bpmn-hosting/evidence/pipeline/native-http-reference/README.md). `qualify_http_recovery.py` uses the normal Workbench/ETL APIs and this directory's loopback fixture; `verify_http_recovery.py` compares its retained whole stores and observations. It covers Sequence/Flowchart success and a real root RunEtl BusinessError without claiming BPMN/model integration.

The later [held-service retry journeys](../../specs/1017-direct-bpmn-hosting/evidence/pipeline/held-service-retry/README.md) qualify one authorized new physical worker/run after a terminal native/ETL fault and two process losses, directly and through all four native RunBpmn parents. `verify_held_service_retry.py` checks their original failed state, active semantic snapshot, pinned budget, exact result/parent consumption and whole stores. [Explicit FinalFault](../../specs/1017-direct-bpmn-hosting/evidence/pipeline/final-service-fault/README.md) separately proves terminal retry refusal. Remaining retry/cancellation/authorization and final upgrade/Studio acceptance is open.

`Test-FiniteFileEtl.ps1` exercises the finite CSV-to-JSONL path through an isolated, owned
Workbench process. It uses the normal HTTP APIs to inspect host bindings, create/validate/publish
an ETL definition, publish native workflow definitions, and execute the resulting artifacts. The
same immutable ETL selection is invoked from a Sequence and a Flowchart; each parent must finish
its `RunEtl` activity and run the following `WriteLine` activity.

Build `Essentials/apps/Elsa.Workbench/Elsa.Workbench.csproj` with the opt-in ETL module references
and assembly discovery enabled, then run from the repository root:

```powershell
pwsh ./e2e-tests/etl/Test-FiniteFileEtl.ps1
```

The script copies the ordinary Workbench configuration into a unique temporary content root and
adds ETL feature settings only to that copy. `_ServerLifecycle.ps1` selects a free loopback port,
starts the already-built Workbench, verifies readiness and process ownership, and stops only that
owned process. Its databases, input CSVs, immutable snapshots, output chunks and logs stay inside
the temporary root; a failed journey retains that root for diagnosis. The script never uses or
stops a separately running Workbench.

The host binds four roles: immutable `source` and `lookup` inputs with SHA-256 pins, plus atomic
`output` and `errors` destinations. The authored pipeline selects only those aliases. It parses
four source records, joins by typed `Int64` key, maps three typed output fields, filters for
`region == "Europe"`, and routes the invalid source key to the safe error output. Each native
journey must report a `businessError` terminal outcome with 2 accepted, 1 rejected and 1 filtered
record across two exhaustive partitions. The Flowchart connects both `RunEtl` outcomes to the
successor so the row-rejection result is handled explicitly.
The script also checks the retained source/lookup digests and lengths, settled partition cursors,
and the physical JSONL chunk contents (including the safe error descriptor).

The current runtime HTTP API exposes run details and partition progress, but does not expose
accepted manifest entries. This journey therefore verifies route chunk contents and run/partition
counters through the real Workbench APIs; it does not claim an HTTP manifest assertion. Manifest
ordering and acceptance are covered by the runtime store/native tests.

This is a real e2e journey when executed after Workbench is built with ETL enabled. Adding the
script alone, or passing only in-process tests, does not count as live e2e execution evidence.

## HTTP connector Workbench journey

`Test-FiniteHttpEtl.ps1` exercises the connector-capable v2 API and runtime against an owned local
HTTP server. Run it after building Workbench with ETL enabled:

```powershell
pwsh ./e2e-tests/etl/Test-FiniteHttpEtl.ps1
```

The journey creates, validates, and publishes HTTP-to-HTTP, HTTP-to-JSONL, and CSV-to-HTTP
definitions, then selects each immutable version from both a native Sequence and Flowchart. It
checks each terminal result and connector partition view, verifies mapped HTTP receiver bodies or
published JSONL records, and confirms both native workflows run the activity after `RunEtl`.
The HTTP source has two host-approved shards with two one-record token pages apiece. The first
request to shard B returns `429 Retry-After: 3`; the test observes its durable waiting state and
progress on the sibling partition, then checks monotonic loopback request timestamps before
accepting the later wire attempt. A separate `http-wait` source always returns `429 Retry-After: 30`,
so the journey can observe the persisted retry view and cancel through the runtime API before a
second physical request is eligible. Workbench remains running until two seconds after that
recorded deadline; the fixture must still have observed exactly one request.

`New-EtlHttpFixture` in `_EtlFixture.ps1` creates the ordinary private ETL content root and starts
`EtlHttpFixture.mjs` on an ephemeral port bound only to `127.0.0.1`. It adds three anonymous,
host-controlled HTTP revisions: `http-source` (`/source/shard-a/page` and
`/source/shard-b/page`), `http-wait` (`/wait/page`), and `http-output` (`/output/records`). Every
business path is explicitly allowlisted; the test pipeline contains aliases and never endpoint
URLs. Source pages contain one `{id,name}` item at `/items`; `/next` supplies a shard-specific opaque
token through the query parameter `pageToken`, and the next page returns `null`. The receiver
accepts JSON arrays and captures a bounded set of mapped records and request observations; its
`/__fixture/state` endpoint reports an explicit overflow flag which the REST journey rejects.
`Stop-EtlHttpFixture -Fixture $fixture` stops only the Node process it started. Failures retain the
private Workbench root and its HTTP fixture logs for diagnosis.

## Live Studio authoring journey

`Test-FiniteFileEtlStudio.ps1` runs the opt-in browser journey in
`Studio/tests/live/etl-journey.spec.ts`. Build the Workbench with ETL enabled and build
`Studio/src/apps/Elsa.Studio.Web` with the ETL Studio module before running:

```powershell
pwsh ./e2e-tests/etl/Test-FiniteFileEtlStudio.ps1
```

The optional `-Http` switch runs two real-browser journeys, covering completion and cancellation, with `http-source` and
`http-output` aliases from the owned loopback fixture:

```powershell
pwsh ./e2e-tests/etl/Test-FiniteFileEtlStudio.ps1 -Http
```

The runner starts the already-built Workbench and Studio on separate free loopback ports, each
with a private content root. It configures the exact Studio origin in the private Workbench CORS
and identity return-origin settings, points `Studio:BackendBaseUrl` at that Workbench, and keeps
Studio authentication enabled. The browser journey uses the seeded Development credentials by
default; pass `-Username` and `-Password` to use another local fixture account.

The default file fixture contains `source` (`id,name`: Ada and Grace) and `lookup`
(`id,category`: One and Two), plus the isolated `output` and `errors` destinations. Its copied
Studio shell omits `WeatherForecastSample` because the private Nuplane package feed is empty and
that optional feature is supplied only by a package. No repository shell or host configuration is
changed. The runner stops only the processes it started and retains the private content root,
host logs, journey log, SQLite state, output chunks, screenshots, and any failure trace for review.

The browser test creates, edits, validates, and publishes an ETL definition through Studio, selects
the exact immutable version in a native Sequence, runs it, and inspects the durable run and
partition progress. The runner does not build either host or install browsers; prepare those
before invoking it. The test is explicitly opted in by the runner and is not part of the ordinary
Studio mocked test suite.
