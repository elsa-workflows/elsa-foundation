# Specialist runtime

`Elsa.Agent.Specialists` owns `SpecialistsRuntime`. Compose it with a model contribution and an
explicit journal profile. Its feature depends on ordinary Background Activities, Serialization,
Secrets and the shared specialist JSON validator. Configure the existing Secrets key ring; there is
no implicit ephemeral encryption key or memory-journal fallback.

## Cross-domain contributions

| Contract | Contribution |
|---|---|
| `IActivityActivationStrategy` | Scoped custom activation of `elsa.specialist/1`, without CLR hydration of dynamic business members. |
| `IRuntimeActivityConsumerCapability` | Declares the installed specialist descriptor/schema consumer. |
| `IStartupTask` | Registers the actual facade and background worker under their frozen runtime aliases. |

Both activity classes opt out of standalone CLR catalog discovery with `Activity(IsDiscoverable=false)`.
Published user specialists remain discoverable through their normal versioned provider definitions.
The opt-out changes reconciliation, not runtime alias registration or existing artifact execution.

The native entry checkpoint stages a protected request and opaque handle alongside child scheduling.
The worker checks the committed parent, child slot, scheduling handle, scoped workflow and immutable
artifact/contract/input fingerprints. Only its admitted fence may dispatch and accept the turn. Its
native completion carries an opaque receipt; the parent verifies that receipt and contributes the
accepted `JsonElement` through the native typed completion checkpoint. Journal acceptance and process
application are distinct boundaries.

Current execution is fresh-only, with a new MAF session for every turn and no tool or streaming
admission. A definition's continuity capability does not implicitly reuse history. Validate business
inputs and outputs against both the exact schema and declared native conversion. Withheld,
encryption-required and secret-reference business inputs are refused before model dispatch.

`SpecialistsInMemoryJournal` is an explicit development/test profile, retaining at most 10,000 turns
for one host lifetime. Requests/results are protected with purpose/scope/provenance checks inside
the authenticated envelope. Repeated operations retain their deadline; altered arguments conflict;
stale fences and late acceptance are refused. Missing state after restart is a fault, never permission
for a fresh model request. This profile establishes no durable recovery or distributed ownership.

The [model provider port](../Core/EXTENSION_POINTS.md) owns actual credential resolution/current
authorization and physical SDK/HTTP retry guarantees. Durable sessions, journal/cancellation/process
settlement, provider adapters, tools, teams and the final support matrix remain Program #3519 work.
