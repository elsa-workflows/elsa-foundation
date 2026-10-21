# ETL Design API extension points

Owner project: `Elsa.Etl.Design.Api`
Domain: `Elsa.Etl`

## Authoring operation boundary

`IEtlDesignOperations` is the scoped operation seam dispatched by the nine NativeEndpoints endpoint classes.
The default `EtlDesignOperations` resolves tenant identity from a validated normalized principal and partition
from the host's ordinary persistence scope. Replacements must preserve permission, scope, bounded pagination,
optimistic revision and exact publication retry semantics in the [finite ETL contract](../../../../../specs/255-finite-file-etl/contracts/journey.md).

`EtlDesignApiFeature` registers the capability, permission contribution, signed cursor codec, operation seam and
owner-local JSON/error contracts. It requires `EtlDesign` and `ApiCapabilities`. Hosts supply Foundation Identity,
the ordinary persistence scope and an `IEtlDefinitionStore`/`IEtlDefinitionCatalog` provider. The SQLite design
feature supplies both stores. No native workflow engine or actor provider is required by this API.

Binding discovery uses the optional `IEtlBindingCatalog` Core port. The default runtime host binding adapter
returns current aliases and roles in exact scope without paths, revisions, digests or handles. An authoring host
can provide the same safe metadata contract without running ETL workers. An absent provider returns unavailable.
Validation reports unavailable alias/role pairs as structured diagnostics. New publication rechecks approval;
exact replay of a previously published revision/version/hash remains valid if approval later changes. This is
not an atomic transaction between host configuration and publication: runtime admission independently pins
and validates the actual retained binding revisions.

Cursor signing is internal to this feature. Configure `CursorSigningKey` consistently across replicas for stable
pagination; its process-local default deliberately does not promise continuity across restarts. The key is a
secret host setting, not an authored value or response field.
