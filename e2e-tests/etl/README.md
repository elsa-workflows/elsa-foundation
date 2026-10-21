# Finite file ETL end-to-end journey

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

## Live Studio authoring journey

`Test-FiniteFileEtlStudio.ps1` runs the opt-in browser journey in
`Studio/tests/live/etl-journey.spec.ts`. Build the Workbench with ETL enabled and build
`Studio/src/apps/Elsa.Studio.Web` with the ETL Studio module before running:

```powershell
pwsh ./e2e-tests/etl/Test-FiniteFileEtlStudio.ps1
```

The runner starts the already-built Workbench and Studio on separate free loopback ports, each
with a private content root. It configures the exact Studio origin in the private Workbench CORS
and identity return-origin settings, points `Studio:BackendBaseUrl` at that Workbench, and keeps
Studio authentication enabled. The browser journey uses the seeded Development credentials by
default; pass `-Username` and `-Password` to use another local fixture account.

The private fixture contains only `source` (`id,name`: Ada and Grace) and `lookup`
(`id,category`: One and Two), plus the isolated `output` and `errors` destinations. Its copied
Studio shell omits `WeatherForecastSample` because the private Nuplane package feed is empty and
that optional feature is supplied only by a package. No repository shell or host configuration is
changed. The runner stops only the two processes it started and retains the private content root,
host logs, journey log, SQLite state, output chunks, screenshots, and any failure trace for review.

The browser test creates, edits, validates, and publishes an ETL definition through Studio, selects
the exact immutable version in a native Sequence, runs it, and inspects the durable run and
partition progress. The runner does not build either host or install browsers; prepare those
before invoking it. The test is explicitly opted in by the runner and is not part of the ordinary
Studio mocked test suite.
