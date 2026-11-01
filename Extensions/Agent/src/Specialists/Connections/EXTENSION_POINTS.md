# Connection-backed specialist model contributors

`SpecialistConnectionModelProvider` in [this package](README.md) implements the existing model
capability/client ports for explicit model profiles. A third-party
SDK package derives it to reuse scope, revision, allowed-host, schema and request authorization checks.
The concrete SDK client is transient and owned by the returned lease. Do not serialize credentials,
SDK sessions or clients into published descriptors or native execution state.

`SpecialistModelConnectionTypeProvider` is the immutable singleton contribution to the existing
Connections type registry. Keep it separate from the scoped model owner and resolver.

## Cross-domain contributions

`IConnectionTypeProvider` from Connections Core is implemented by the stateless type definition;
the optional vendor features register its exact configured connection type in the owning registry.
