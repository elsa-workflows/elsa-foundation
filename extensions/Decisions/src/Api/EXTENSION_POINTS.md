# Decisions API contributions

- Cross-domain permission contribution: internal `DecisionsPermissionContributor` implements Foundation Identity Core `IPermissionContributor`; its catalog aggregation belongs to Foundation Identity. Registered once by `DecisionsApiFeature`.
- Cross-domain authorization consumption: internal `HttpDecisionAccessPolicy` implements Decisions Design's scoped replacement `IDecisionAccessPolicy`, calling Foundation Identity Core `IPermissionAuthorizationService` after canonical normalized caller binding.
- API capability: `elsa.api.decisions`, version 1, relation `decisions` at `decisions`, owner `DecisionsApi`.
- HTTP: normal Minimal Endpoints catalog operations rooted at `/decisions`, each with explicit canonical permission metadata and server-side domain authorization.

No public contributor interface, event, independent fan-in, background task or recurring schedule is introduced by this module.
