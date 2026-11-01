# Specialist model connections

The connection-backed contributors implement the existing [specialist model ports](../Core/EXTENSION_POINTS.md).
`Elsa.Agent.Specialists.Connections` supplies their common operational boundary without a provider SDK,
Design or Publishing dependency. Anthropic and Azure OpenAI remain separate optional packages.

Enable `Connections` with the host's chosen metadata and Secrets backends, then enable
`AnthropicSpecialists` and/or `AzureOpenAISpecialists`. Authoring-only hosts consume capabilities;
runtime-only hosts consume client factories. These features do not activate Weaver or select its harness.
The enclosing operation must already have an explicit ordinary persistence partition. Runtime native
provenance must agree with that partition and any non-null tenant; a null tenant never grants global access.

Create a connection of type `specialist-anthropic` or `specialist-azure-openai`, using the `api-key`
authentication scheme. Put the provider credential in the existing `apiKey` secret field. Configure:

| Setting | Meaning |
|---|---|
| `endpoint` | Explicit HTTPS origin, with no path, user info, query or fragment. Examples: `https://api.anthropic.com`, `https://my-resource.openai.azure.com`. |
| `allowedHosts` | Explicit comma-separated host policy, which must allow the endpoint. Unrestricted `any` policy is refused. |
| `modelId` | Exact model ID or Azure deployment name bound to this connection. |
| `maximumOutputTokens` | Declared positive output token ceiling, at most 1,000,000. |
| `structuredResults` | Explicit declaration that this model/deployment supports structured JSON results. |
| `temperatureSupported` | Explicit declaration of temperature support. When false, the specialist settings must be empty. |

The initial setting profile admits only optional `temperature` from 0 through 1. Unsupported settings
are refused before credential resolution. These are configured capability declarations, not live model
discovery or a guarantee that a deployment's backing model has not changed. A connection test validates
configuration and credential availability without inference; its message says that inference was not tested.
Actual deployment/provider qualification belongs to the Program support matrix.

A capability fingerprint binds the provider SDK/API profile, endpoint, model, declared support, token
ceiling and host policy. Operational revision changes with the same effective support can preserve a
publication. Runtime opening requires a positive revision on the same resolved snapshot. Immediately
before every SDK transport send, the contributor resolves scope, status, owner, revision and profile
again; a revoked or edited connection invalidates an already opened lease.

The default primary transport uses HTTPS, HTTP/1.1, no automatic redirects, cookies, proxy credentials
or `Expect: 100-continue`. SDK retries are disabled. This profile uses explicit API keys, so no ambient
credential chain or refreshing token callback is admitted. Physical request counting must include the
primary transport; counting outer agent calls alone does not prove a request budget. Custom transports
and authentication profiles need separate qualification.

The shared structured-result request profile is `elsa.specialist.structured-object/1`: an object root,
closed objects whose properties are all required, up to five nested levels and 100 total properties;
arrays have an item schema. Nullable types are explicit. Unknown keywords, optional object members,
`const`, unresolved references, boolean schemas and unconstrained objects are refused before inference.
The pinned SDKs can move numeric constraints into descriptions. Elsa retains the complete original
published schema and validates the final business result against it; an SDK success alone is not an
accepted turn. Streaming, durable acceptance, continuity and budget settlement belong to the specialist
runtime, rather than to this operational connection helper.

## Contributor extension

`SpecialistConnectionModelProvider` is the scoped shared base for a separately packaged SDK contributor.
Implement its exact provider/connection/protocol identities and `CreateClient`, disable SDK retries,
and use the supplied owned HTTP client and explicit sensitive API key. Change the protocol identity
when effective SDK/API/schema support changes. A custom primary handler must preserve the documented
controls and carry actual physical-request tests. Contributors extending beyond this finite profile
can instead implement the existing Core ports directly.

`SpecialistModelConnectionTypeProvider` supplies the corresponding immutable connection-type
definition. Register it as a singleton in the existing Connections type registry, separately from the
scoped model owner. Holding a scoped model owner in that registry creates a resolver/registry cycle.

Lease authorization binds the connection's opaque creation identity as well as its revision. Deleting
and recreating a name invalidates old leases even when version numbers repeat. Unknown legacy creation
identities are refused by this profile. Each transport send resolves the current backing credential
again, refusing revoked/expired/deleted or changed material independently of the metadata revision.
Revocation after the transport authority check cannot retract a request already in flight.
