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

## Runtime inspection bounds

`IEtlRuntimeStore.MaximumPageSize` advertises the positive maximum accepted `take` of each list call. A consumer
may compose multiple bounded, exclusive keyset pages to serve its own bounded response; it must not silently
increase the provider's cap or interpret an expired claim as worker termination. The runtime API uses this
capability for at most 201 retained rows including lookahead. Manifest materialization streams pages under the
same limit. See the [finite ETL contract](../../../../specs/255-finite-file-etl/contracts/journey.md).

## Connector artifact format

`EtlPipelineV2` is a versioned data contract, not a connector-provider registration surface. The supported
source, pagination and destination variants are explicit. `EtlArtifactV2Json` provides bounded, strict
serialization independently of `EtlPipeline` / `EtlArtifactJson` v1. Model declarations contain host binding
aliases and selectors, not endpoint or credential values. Artifact validity and host runtime admission are
separate decisions; a successfully decoded document is not proof that the configured runtime can execute it.

The runtime applies independent `EtlRuntimeFeature.HttpLimits` and live-attempt capacity ceilings, and checks
mapping/filter/lookup semantics without design services. A compiler-valid artifact can exceed a particular
host's ceilings. A missing lookup does not reserve a synthetic lookup allowance. The existing file store still
rejects v2 admissions; known canonical v2 invocations can be retained for cancellation or refusal and terminal
delivery. This checkpoint does not yet supply a connector worker. See the
[runtime reader checkpoint](../../../../specs/996-http-etl/evidence/v2-runtime-reader-checkpoint.md).
