# Connection-backed specialist model contributors

`SpecialistConnectionModelProvider` in [this package](README.md) implements the existing model
capability/client ports and operational connection-type port for explicit model profiles. A third-party
SDK package derives it to reuse scope, revision, allowed-host, schema and request authorization checks.
The concrete SDK client is transient and owned by the returned lease. Do not serialize credentials,
SDK sessions or clients into published descriptors or native execution state.
