# ETL Core extension points

Owner project: `Elsa.Etl.Core`
Domain: `Elsa.Etl`

## Safe binding discovery

`IEtlBindingCatalog` lists only current binding aliases and roles in an exact `EtlScope`. Its exclusive ordinal
alias/role boundary and finite limit allow authoring hosts to discover permitted choices without disclosing
paths, revision handles, digests or credentials. The default `HostFileBindings` adapter lives in the runtime
implementation; another host may implement this metadata-only contract without starting runtime workers.

`ContainsBinding(scope, alias, role)` checks current approval for one exact role in that same scope without
opening a file. Authoring validation and new publication use it; an exact replay of an existing immutable
publication does not depend on today's catalog. This check does not reserve a revision or replace runtime admission.

This port does not resolve file paths, admit runs, select input revisions or provide arbitrary storage access.
The finite runtime owns those operations and preserves the existing pinned revision/publication rules.

## Connector binding approval

`IEtlConnectorBindingCatalog` is an additive v2 catalog exposing alias, role and connector kind. It does not
replace the file-only v1 catalog. A current alias/role pair is unique across file and HTTP kinds within one
scope. `IEtlConnectorBindingResolver` resolves a complete binding set for a validated `EtlPipelineV2`, or a
stable value-free failure code; it performs no source GET or destination POST. Connections metadata resolution
can involve storage I/O. New publication checks approval again; immutable replay uses the retained publication.

The default runtime adapter snapshots `EtlRuntimeFeature.HttpBindings`. Its opaque revision handles commit
the exact scope, endpoint/path policy, shard seeds, and resolved connection identity and non-secret settings.
Exact retained revision resolution never substitutes the current alias revision. Authenticated resolution
requires the retained tenant and public Connections Core providers; anonymous bindings compose without them.
No credential is read during validation or pinning. The short-lived connection is disposed after each resolution;
request execution must obtain a new exact-pin resolution and authenticate each request separately. Credential
source references are not exposed by this public port, so changes to those references remain part of permitted
credential rotation. The [HTTP profile](../../../../specs/996-http-etl/contracts/http-profile.md) defines the
transport and publication boundaries. Binding ports do not themselves execute work; the shared worker consumes their exact pins.

## Runtime inspection bounds

`IEtlRuntimeStore.MaximumPageSize` advertises the positive maximum accepted `take` of each list call. A consumer
may compose multiple bounded, exclusive keyset pages to serve its own bounded response; it must not silently
increase the provider's cap or interpret an expired claim as worker termination. The runtime API uses this
capability for at most 201 retained rows including lookahead. Manifest materialization streams pages under the
same limit. See the [finite ETL contract](../../../../specs/255-finite-file-etl/contracts/journey.md).

`IEtlConnectorRuntimeInspector` is a separate optional read-only capability for consistent retained connector
snapshots and bounded partition pages. It has the same positive cap and short-page/exhaustion contract.
Run snapshots contain internal invocation data; public consumers must project approved fields. Partition
projections carry typed source progress and safe receipt/retry metadata. Counts represent settled route
bodies, and neither expiry nor acknowledgement proves physical termination or accepted progress. The SQLite
provider reads a consistent transaction and aggregates indexed delivery-mechanism columns (HTTP or file) without materializing the
historical output documents. See the [inspection checkpoint](../../../../specs/996-http-etl/evidence/connector-inspection-checkpoint.md).

## Connector artifact format

`EtlPipelineV2` is a versioned data contract, not a connector-provider registration surface. The supported
source, pagination and destination variants are explicit. `EtlArtifactV2Json` provides bounded, strict
serialization independently of `EtlPipeline` / `EtlArtifactJson` v1. Model declarations contain host binding
aliases and selectors, not endpoint or credential values. Artifact validity and host runtime admission are
separate decisions; a successfully decoded document is not proof that the configured runtime can execute it.

The runtime applies independent `EtlRuntimeFeature.HttpLimits` and live-attempt capacity ceilings, and checks
mapping/filter/lookup semantics without design services. A compiler-valid artifact can exceed a particular
host's ceilings. A missing lookup does not reserve a synthetic lookup allowance. The existing file-only store
contract still rejects v2 admissions. An optional `IEtlConnectorRuntimeStore` supplies typed connector transitions;
the shared worker discovers it and uses the same physical attempt pool, heartbeat and terminal delivery. A selected
file-only replacement/decorator remains usable and receives a durable v2-unavailable refusal. See the
[worker checkpoint](../../../../specs/996-http-etl/evidence/http-worker-checkpoint.md). A connector provider's
`CommitConnectorPreparationAsync` must settle a zero-record file run (CSV or JSON Lines) as success in the same
transaction, because no partition exists to complete it; an identical replay returns `Replay`
([spec 1190](../../../../specs/1190-etl-jsonl-input/spec.md) FR-011). A provider must also accept `EtlConnectorKind.Csv` route
outputs as file routes ([spec 1191](../../../../specs/1191-etl-csv-delivery/spec.md)): `RecordConnectorFilePublicationAsync`
matches a CSV publication against `EtlRuntimeIdentity.ConnectorFileOutputKey(effectId, kind)` (a `.csv` key, refusing the
`.jsonl` one), and a CSV route counts as a file publication for delivery, acceptance and inspection state.

`EtlPipelineV2.Continuous` is an optional, init-only run policy; a null policy is never serialized, so finite artifacts
keep their bytes and identities. `EtlContinuousRunPolicy.FindViolation` is the one rule every reader applies (HTTP
source with token or next-link pagination, interval 1–86,400 s). For continuous runs a connector provider implements two
members whose default bodies refuse ([spec 1192](../../../../specs/1192-etl-continuous-http/spec.md)):
`RecordConnectorHttpCaughtUpAsync` validates the live latest in-flight attempt under the lease fence and its receipt
window, then clears the request journal, releases its input and source-metadata reservations, retains no page and
schedules the partition's next poll (at once when completion was requested after the attempt started); when completion
was requested at or before the attempt started it changes nothing and returns `Final`, and the caller retains the
response as a final page whose unmoved cursor is normalized away. `RequestConnectorRunCompletionAsync` records a durable
request and makes only idle partitions (cleared journal, no owed batch) due at once; retry waits keep their schedule.
A provider's audits must accept an idle partition's eligibility time without a waiting journal for continuous runs only.
A provider that keeps the refusing defaults still admits a continuous artifact, whose first caught-up response then
fails the run with `etl.http.source-transition-refused`; such a provider should not be offered continuous pipelines.
`AdvanceEmptyHttpPageAsync` always delays a continuous shard's next request by one interval after a moving empty page.
`RequestConnectorRunCompletionAsync` reports `NotContinuous` for a finite run whether or not it has settled; `Terminal`
is only for a settled continuous run. `EtlConnectorRunInspection.Continuous` / `CompletionRequestedAt` and
`EtlConnectorPartitionInspection.NextPollAt` are init-only, so existing inspector implementations keep compiling;
`NextPollAt` is set only for an idle partition and equals its `NotBefore`.

A continuous provider also keeps each shard's state bounded ([spec 1196](../../../../specs/1196-etl-continuous-compaction/spec.md)).
`RecordConnectorHttpCaughtUpAsync` increments the shard's poll generation (not on the `Final` path), and a claim copies
it onto `EtlConnectorPartitionLease.PollGeneration`, which is 0 for every finite shard. Page-cache keys use the
`ConnectorHttpPageId` overload with that generation; generation 0 is the finite identity. Every claim in a continuous
run deletes, for every shard of the run, the rows below the predecessor of its current request before the claim commits
(budgets are per run, so compacting only the claimed shard is not enough), keeps lifetime counts
through a compacted summary, and audits retained history from that base. A provider must not apply the authored
`MaxPagesPerPartition` to a continuous shard's page sequence.

Connector providers distinguish temporary outstanding-capacity pressure (`BudgetExceeded`) from a transition
that cannot fit the retained-input/output/metadata limits (`PermanentBudgetExceeded`). The worker yields for
temporary pressure and settles a safe fenced diagnostic for a proven permanent shortage. Terminal failure keeps
unaccepted descriptors and accounting as evidence; it never fabricates acceptance to clear counters. Providers
must retain the request/page history needed to classify irreversible growth and must not mistake another live
partition's releasable reservation for permanent exhaustion.
