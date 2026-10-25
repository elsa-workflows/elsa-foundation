# Reference Docs

Reference docs preserve worked examples, historical case studies, and explanatory walkthroughs that support the constitutions without being the enforceable gate surface.

Use the constitution files in `.specify/memory/` for gates, allowed exceptions, draft/provisional state, ratification metadata, and governance. Review-only gates are listed in [docs/contributing/review-checklist.md](../contributing/review-checklist.md). Use these reference docs when a gate points to an example or when a future session needs the longer reasoning trail.

## Contents

- [Elsa worked examples](elsa-worked-examples.md) - Elsa-specific examples, the elsa-core case study, and the long form of Elsa §E2.2-§E2.9 and §E5 moved out of `constitution.md` by #2578.
- [Framework examples](framework-examples.md) - synthetic and application-instance examples formerly embedded in `constitution-framework.md`.
- [Architecture rationale](architecture-rationale.md) - explanatory context, rejected alternatives, and decision rationale moved out of the constitutions.
- [Refactoring methodology](refactoring-methodology.md) - the three-step method behind framework §2.18.
- [Elsa domain tree](elsa-domain-tree.md) - the purpose of each root-level Elsa domain pinned in Elsa §E2.1.
- [Elsa type naming](elsa-naming.md) - the full text, senses and origins of the Elsa §E6 naming rules R1-R8.
- [Agent preferences](agent-preferences.md) - committed catalog of local agent preference file rules and templates; personal selections belong in `.agent-prefs/`.
- [Git operating models](git-operating-models.md) - maintainer organization-branch and contributor fork workflows; no publication-time model selection is required.
- [Spec lifecycle](spec-lifecycle.md) - how specs under `specs/` are numbered, the closed status vocabulary, when a spec reaches a terminal status, and why specs are never moved or deleted.
- [First-user prompt options](first-user-prompts.md) - simple prompts for new architects and engineers entering the workspace.
- [Developer solution filters](developer-solution-filters.md) - the generated Core and Workbench `.slnf` views, plus the on-demand integration filter, for faster inner-loop work without weakening the full solution gate.
- [NuGet lock files](nuget-lock-files.md) - the committed `packages.lock.json` files, locked restores in CI, and how to update the lock files on a package bump.
- [Identity generators](identity-generators.md) - swappable `IIdentityGenerator` strategies (UUIDv7, short 64-bit, Snowflake, GUID) for short/sortable entity ids.
- [Activity Definition authoring composition](activity-definition-authoring-composition.md) - host opt-in/opt-out guidance for provider-neutral authoring, Activity Graph design, and Activity Graph runtime features.
- [Authentication architecture](authentication-architecture.md) - integrator guide to the auth stack: the two-plane (IAM vs protocol) architecture, the module taxonomy, OpenIddict-as-IdP vs Oidc-as-relying-party, cookies vs JWTs, the composition matrix per deployment scenario, how Studio consumes it, security posture, and per-IdP recipes.
- [Identity configuration](identity-configuration.md) - operator-facing configuration for the first-party auth stack (features, keys, cookie/CSRF hardening, dev seeding) with a production go-live checklist.
- [HTTP workflow authoring](http-workflow-authoring.md) - create, publish, and send a real request to an HTTP workflow in Studio; explains Sync and Async responses and legacy metadata behavior.
- [Elsa 3 migration readiness: reusable workflows](elsa3-migration-readiness.md) - what to check and change in Elsa 3 reusable workflows and their callers before the collection import, each check with the diagnostic code it prevents, and how to read the apply result (spec 246).
- [Elsa 3.9 to Elsa 4 permission mapping](elsa3-permission-mapping.md) - the `{resource}:{verb}` permission shape and matching rules, the Elsa 4 permission vocabulary, and the mapping from every built-in Elsa 3.9 permission (ADR 0085 D2, D8).
- [Generic OAuth2 connector](elsa-integrations-oauth2.md) - Elsa Integrations `#2727` scheme (`oauth2`) for the foundation `http` connection: callback path, CAS refresh, secret masking, and `Elsa.Integrations.Sdk` v1.
