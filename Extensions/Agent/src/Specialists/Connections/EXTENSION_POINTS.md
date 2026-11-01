# Connection-backed specialist model contributors

`SpecialistConnectionModelProvider` in [this package](README.md) implements the existing model
capability/client ports for explicit model profiles. A third-party
SDK package derives it to reuse scope, revision, allowed-host, schema and request authorization checks.
The concrete SDK client is transient and owned by the returned lease. Do not serialize credentials,
SDK sessions or clients into published descriptors or native execution state.

The shared HTTP guard invokes the supplied Core physical dispatch authority before authorization
and send, retaining a conservative reservation if later authorization/transport fails. An unconfirmed
reservation never permits a request. `SupportsPhysicalRequestReservations` defaults to false; a
derived owner opts in only after qualifying both its SDK and primary handler. The sealed built-in
Anthropic/Azure OpenAI owners opt in for their documented transport profiles. A custom handler that
resends below this guard needs its own reservation mechanism and qualification.

`SpecialistModelConnectionTypeProvider` is the immutable singleton contribution to the existing
Connections type registry. Keep it separate from the scoped model owner and resolver.

## Cross-domain contributions

`IConnectionTypeProvider` from Connections Core is implemented by the stateless type definition;
the optional vendor features register its exact configured connection type in the owning registry.

`SupportsStreaming` also defaults to false. An owner may opt in only after qualifying its SDK's raw
completion/refusal signals. The runtime separately bounds and filters projected updates before MAF
aggregation. The lease flag does not grant tools, provider-managed sessions or automatic stream retries.
