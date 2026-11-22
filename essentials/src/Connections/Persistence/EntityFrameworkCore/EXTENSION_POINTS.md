# Connections EF extension points

| Surface | Purpose |
| --- | --- |
| `ConnectionsEntityFrameworkCoreFeature` | Shell feature selecting provider, connection, schema and pooling; depends on Connections. |
| `ConnectionsEntityFrameworkCoreRegistration.AddConnectionsEntityFrameworkCore` | Registers the default module-owned context, scoped repository and shared migration lifecycle. |
| `ConnectionsEntityFrameworkCoreOptions` | Explicit provider/connection/schema/pooling choices for that registration. |
| `ConnectionsModelBuilderExtensions.AddConnectionsModel` | Installs the connection mappings in any consumer-owned `DbContext`; the host owns migrations. |
| `ConnectionsEntityFrameworkCoreRegistration.AddConnectionsRepository<TContext>` | Replaces the repository over a registered `DbContext`, without requiring an Elsa base class. |
| `ConnectionsDbContext` and four provider contexts | Optional module-owned models and their provider-specific migrations. |

Repository consumers use `IConnectionRepository` and `ConnectionRepositoryCapabilities` from
[Connections Core](../../Core/EXTENSION_POINTS.md). No EF type crosses that boundary. The entity
types, query projections, key derivation and repository implementation stay internal. EF-generated
migration entry points are retained in the package's public API baseline.

Save reserves the revision compare-and-swap with a row write, then compares the original ordinal incarnation
inside that transaction before replacing payload and secret references. Delete/recreate cannot reuse a stale save
merely because its numeric revision repeats. An incarnation conflict rolls the reserved revision back too.

See the [registration and ownership examples](README.md), shared
[EF policy](../../../Persistence/EntityFramework/EXTENSION_POINTS.md), and
[connection manager contracts and known limits](../../Elsa.Connections/EXTENSION_POINTS.md).
