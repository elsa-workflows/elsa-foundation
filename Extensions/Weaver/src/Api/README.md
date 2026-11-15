# Weaver API

`WeaverApiFeature` maps the generic assistant endpoints and registers the HTTP principal/authorization adapters
and host support context source. Shared lifecycle and contribution meanings remain in the
[Weaver catalog](../Elsa.Weaver/EXTENSION_POINTS.md).

## Cross-domain contributions

`WeaverEffectsApiCapabilitySource` implements API Capabilities' existing
[`IApiCapabilitySource`](../../../../Essentials/src/Api/Capabilities/Elsa.Api.Capabilities/EXTENSION_POINTS.md#iapicapabilitysource).
`AddWeaverApi` registers it once through `AddApiCapabilitySource`. It contributes `elsa.api.weaver.effects`
contract1 only when an effects variation is selected, with the existing bootstrap and session-proposal collection links.
The source reuses Weaver's internal selected-feature admission and its shared schema checker on every read.
Held or unobserved requirements produce the current caller-neutral dormancy reason; memory needs no observer.
Infrastructure failures are wrapped in `WeaverContributionException` with their original cause and fail document
assembly; cancellation and already localized domain exceptions propagate. No tools, principal or resource store
are resolved for this projection. The legacy `elsa.api.weaver` capability retains its independent declaration.

The same-domain friend access supports this built-in API projection, without exposing another public contribution
contract. Module authors continue using the existing public tool/context and shared dormancy contracts. The
[spec1203 preview](../../../../specs/1203-weaver-module-support/contracts/privilege-policy.md#shared-admission)
still requires actual held/finalized composed and independent-package evidence before contract freeze.
