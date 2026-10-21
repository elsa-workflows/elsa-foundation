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
