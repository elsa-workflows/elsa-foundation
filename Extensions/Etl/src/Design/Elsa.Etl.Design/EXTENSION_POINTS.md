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
