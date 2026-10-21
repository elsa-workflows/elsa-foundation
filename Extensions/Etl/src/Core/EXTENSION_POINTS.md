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
provider reads a consistent transaction and aggregates indexed scalar route kinds without materializing the
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
[worker checkpoint](../../../../specs/996-http-etl/evidence/http-worker-checkpoint.md).

Connector providers distinguish temporary outstanding-capacity pressure (`BudgetExceeded`) from a transition
that cannot fit the retained-input/output/metadata limits (`PermanentBudgetExceeded`). The worker yields for
temporary pressure and settles a safe fenced diagnostic for a proven permanent shortage. Terminal failure keeps
unaccepted descriptors and accounting as evidence; it never fabricates acceptance to clear counters. Providers
must retain the request/page history needed to classify irreversible growth and must not mistake another live
partition's releasable reservation for permanent exhaustion.
