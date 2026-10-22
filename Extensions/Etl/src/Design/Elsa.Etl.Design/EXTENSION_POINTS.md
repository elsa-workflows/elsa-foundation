# ETL Design extension points

Owner project: `Elsa.Etl.Design`
Domain: `Elsa.Etl`

## Read-side boundary contracts

### `IEtlDefinitionCatalog`

- **Kind:** Scoped read-side store contract.
- **Purpose:** Lists ETL draft metadata and immutable publication metadata without returning pipeline or artifact content.
- **Scope:** Every query is confined to the supplied tenant and persistence partition. Definition pages use an exclusive,
  stable definition-key cursor. Version pages use an exclusive lower bound and inclusive caller-frozen ceiling.
- **Bounds:** Each page accepts 1 through 1000 rows. Implementations must apply ordering and the limit in storage before
  materializing results.
- **Default implementation:** `EfEtlDefinitionCatalog` in `Elsa.Etl.Persistence.EntityFrameworkCore`.
- **Related contract:** `IEtlDefinitionStore` owns draft reads and writes plus individual published artifact reads and
  publication writes. Catalog implementations must not expose the stored JSON payloads.

## Artifact compiler capabilities

### `IEtlArtifactCompiler` and `IEtlArtifactCompilerV2`

- **Kind:** Scoped design compiler capabilities, implemented by the built-in `EtlArtifactCompiler`.
- **Selection:** `IEtlArtifactCompiler` accepts the retained v1 file pipeline. The additive
  `IEtlArtifactCompilerV2` accepts connector-capable v2 declarations. Implementing v1 does not require
  implementing v2. Consumers must resolve the capability matching the requested document format and
  report an unavailable capability explicitly; they must not decode a v2 document as v1.
- **Boundary:** Compilation validates authored declarations and snapshots canonical bytes into an
  `EtlArtifact`. It does not resolve credentials, admit work, or establish runtime/provider support.
- **Composition:** `EtlDesignFeature` registers both interfaces against the same scoped built-in compiler.
  Hosts replacing a compiler capability must preserve one selected implementation for that interface.
- **Compatibility:** V1 canonical serialization and identity remain unchanged. V2 selects its own codec;
  artifact identity includes the format version. Shared scalar/schema/mapping validation does not turn
  HTTP records into CSV records.

## Draft format capabilities

### `IEtlDefinitionStore` and `IEtlDefinitionStoreV2`

- **Kind:** Scoped authoring persistence capabilities. The built-in EF provider registers both against
  the same scoped store; v1 providers are not required to implement the additive v2 capability.
- **Selection:** The original store reads/writes v1 drafts and owns publication/read operations for
  immutable artifact envelopes. The v2 capability reads/writes connector drafts. Catalog summaries
  expose `FormatVersion` without returning document content; it defaults to 1 for existing consumers.
- **Compatibility:** Definition identity, revision and publication numbering are shared. An explicit
  v2 update at the current revision can upgrade a v1 draft without changing old publications. A v1
  update cannot downgrade a v2 draft. A read through the wrong capability is refused; a write conflict
  may have no typed draft when the stored format differs.
- **Publication:** The provider requires the compiler capability matching the artifact format and
  verifies canonical content, provenance, draft revision and next publication version before committing.
- **Retained data:** SQLite adds discriminator columns defaulted to 1, preserving old payload bytes.
  This additive migration implements the retained-data upgrade required by spec996 rather than
  regenerating the initial schema. Automated downgrade is refused because removing the discriminator
  would erase format information. Rollback must retain readers for accepted v2 data or restore a
  verified v1 backup; it must not reinterpret v2 work as v1.
- **Boundary:** Persisting and pinning v2 artifacts does not establish HTTP runtime/provider support.
