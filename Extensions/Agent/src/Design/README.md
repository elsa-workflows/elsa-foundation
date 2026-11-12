# Agent Design

`AgentDesignFeature` registers the scoped agent activity provider and its authoring,
dependency-discovery and immutable-compilation contributions. See the [contribution catalog](EXTENSION_POINTS.md)
for schemas, lifetimes and ownership boundaries.

## Cross-domain contributions

`AgentActivityProvider` implements Activities Design's `IActivityProvider` and optional
`IActivityProviderDiagnosticCatalog`, plus Publishing's compiler and dependency-discoverer contracts.
Its registered static public diagnostic guidance lets publication, validation and Test Run explain
configuration problems without forwarding private runtime diagnostic content. The exact contract,
registration mechanism and consuming registries are in the [Activities catalog](../../../../Essentials/src/Activities/Design/Api/EXTENSION_POINTS.md).
