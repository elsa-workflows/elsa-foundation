# Specialist model contributions

Owner contract package: `Elsa.Agent.Specialists.Core`. Runtime owner: `Elsa.Agent.Specialists`.

## Model providers

[`ISpecialistModelCapabilitiesSource`](Contracts/ISpecialistModelCapabilitiesSource.cs) owns authoring
metadata; [`ISpecialistModelClientFactory`](Contracts/ISpecialistModelClientFactory.cs) owns runtime
client creation. Register scoped implementations through DI, keyed by their exact `ProviderKey`. Each
operation requires exactly one matching owner of its own port; duplicates and missing owners are refusals.
A provider can implement both ports, while authoring-only and runtime-only hosts register only what they consume.
This registry is independent of Weaver's selected assistant provider.

`GetCapabilitiesAsync` reads authorized current model metadata without inference. The capabilities
fingerprint identifies the effective settings/schema/token support. The supplied settings schema must
use the [bounded JSON profile](../JsonSchema/README.md); unsupported or invalid settings refuse
publication. Treat unsupported or unknown structured-result support as unavailable.

`OpenAsync` resolves the logical operational connection at point of use, checks current access and
revocation against the supplied native provenance, and returns an owned transient client lease. The
identity is provenance for verification, not a caller-provided authorization grant. Credentials and
SDK objects never enter the descriptor or journal material. The lease's capability fingerprint must
match publication and its connection version must be positive. The runtime owns model selection,
response schema, maximum output tokens and tool admission; it clones the supplied options.

No production model adapter ships with the initial kernel. The separately qualified deterministic
model contribution establishes the extension boundary; Anthropic/Azure OpenAI, their physical-request
retry behavior and live connections remain subsequent Program work.

## Internal storage

The initial journal store is internal to the runtime and explicitly process-local. It is not an
external durable-persistence contract or an alternative to existing Connections/Secrets ownership.
The first durable provider must establish its real conditional-write boundary before that seam is
exposed to package authors.
