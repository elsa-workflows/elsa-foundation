# Data model and state transitions

No new persisted entity or public IAM contract is required.

| Entity | Fields and source | Admission and lifetime |
|---|---|---|
| Activated bridge configuration | Opt-in, exact registered JWT scheme, distinct normalized type, Authority/effective Audience, static ProviderId/TenantId, metadata policy; host-owned options. | Final options/handler/event validation before serving; trust configuration frozen until fresh activation. Secret credentials are not evidence fields. |
| Host-owned persistence context | Existing Runtime.Core accessor, one ordinary scoped partition equal to the static configured tenant. | Initialized before store resolution; activation and lookup check agreement. Conflicting/global/privileged/across-scope context refuses without Bind/overwrite. |
| Captured callback set | Ordinary event delegates from the selected named bearer options before guarded installation. | Foreign EventsType/subclass refuses. One owned guard contains allowed callbacks; no later principal-changing callback after normalization. |
| Validated external principal | Real validated JWT principal/token after permitted ordinary callback. | Not trusted for Elsa policies; internal claims filtered before mapping. Never persisted by this bridge. |
| Mapping snapshot | Existing ClaimMappingRule list loaded for static namespace. | Per request, passed once to normalizer; next request reloads. No new global cache or transactional snapshot promise. |
| Normalization result | Existing ClaimsNormalizationResult principal, roles, permissions. | Exact single authenticated identity/type/marker/namespace admission; invalid output refuses. |
| Authentication result | Successful ticket only after all guards; otherwise fixed refusal or propagated cancellation. | Request-local; bridge makes no bearer-request user/link mutation. |

State order: configured/unvalidated → activated guarded handler → real validated token → filtered principal + owned rules → admitted normalized principal → successful ticket → existing permission evaluation → HTTP/runtime effect.

Any invalid token stops before owned mapping. Configuration failure stops before request service. Mapping/normalization refusal stops before ticket publication. Cancellation stops at the observed boundary. Ordinary permission denial occurs only after authentication succeeds. Operational authorization errors retain their existing propagation. The actual handler/HTTP proof must distinguish these states; direct event unit tests alone cannot establish them.
