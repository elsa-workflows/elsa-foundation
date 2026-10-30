# Package consumer recipes

These are source-derived composition recipes to implement and verify, not successful build claims. Both consumers are materialized outside the repository, with PackageReference-only product dependencies and no resource-package reference at baseline.

## Backend

Use public `AddCShellsAspNetCore`, explicit feature assembly discovery, configuration provider/web routing and `WithAuthenticationAndAuthorization`. Compose real native endpoints/services; the existing Composition probe's synthetic SessionBackend is unsuitable.

| Direct package group | Explicit feature selections |
|---|---|
| Elsa.Primitives.Hosting, Elsa.Locking.FileSystem, Elsa.Events, Elsa.Mediator, Elsa.Tasks, Elsa.Serialization.SystemText | Primitives, FileSystemDistributedLocking, Events, Mediator, Tasks, Serialization |
| Elsa.Expressions, Elsa.Expressions.Api, Elsa.Api.Capabilities | Expressions, ExpressionsApi, ApiCapabilities |
| Elsa.Foundation.Identity, Elsa.Foundation.Identity.Api, Elsa.Foundation.Identity.AspNetCoreIdentity, Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore, Elsa.Foundation.Identity.OpenIddict | FoundationIdentityAbstractions, FoundationIdentityApi, FoundationIdentityAspNetCoreIdentity, FoundationIdentityAspNetCoreIdentityEntityFrameworkCore, FoundationIdentityOpenIddict |
| Elsa.Studio.Preferences.Api, Elsa.Studio.Preferences.Persistence.EntityFrameworkCore | StudioPreferences, StudioPreferencesApi, StudioPreferencesEntityFrameworkCore |
| Elsa.Workflows.Design.Api, Elsa.Workflows.Design.Validations, Elsa.Workflows.Design.Persistence.EntityFrameworkCore | WorkflowsDesignApi, WorkflowDesignValidations, WorkflowsDesignEntityFrameworkCore |
| Elsa.Activities.Design.Api, Elsa.Activities.Design.Persistence.EntityFrameworkCore, Elsa.Activities.Design.Reconciliation, Elsa.Activities.Design.Reconciliation.Clr, Elsa.Activities.Flowchart, Elsa.Activities.Primitives | ActivitiesDesignApi, ActivitiesDesignEntityFrameworkCore, ActivitiesDesignReconciliation, ClrActivityReconciliation, ActivitiesFlowchart, ActivitiesPrimitives |
| Elsa.Agent, Elsa.Agent.Api, Elsa.Agent.Workflows | FoundationAgentAbstractions, FoundationAgentApi, FoundationWorkflowsAgent |
| Elsa.Modularity.Api | ModularityApi and its public host registration/mapping |
| Elsa.Agent.GitHubCopilot, Elsa.Agent.Anthropic | Exactly one of GitHubCopilotAgent / AnthropicAgent per protocol profile, plus separate fixture factory replacement |

Include public native CShells/Nuplane loading/admin packages, Elsa.Cluster.Readability for normal EF schema-readability/startup validation, the selected SQLite engine and OpenIddict.EntityFrameworkCore. Transitive source-free closure must be verified by actual restore; do not declare this table a tested closure. No publication/runtime engine is needed for the bounded draft position/validation/Save journey. A demonstrated missing prerequisite is refined here and retested.

Use explicit isolated SQLite connection strings for IAM, Preferences, activity catalog and workflow design. The selected EF features own migrations; enable normal schema readability/startup validation. The Identity EF feature owns IAM wiring and the real initial administrator seeder; do not independently register a second IAM provider. Generate fixture-only credentials, retain them privately and keep them out of receipts/provider traces.

The consumer owns a small vendor TokenDb using ordinary DbContext with SQLite and OpenIddict mappings; root `AddOpenIddict().AddCore(...UseEntityFrameworkCore().UseDbContext<TokenDb>())` supplies stores. Initialize this separate token database before app start. Enable the unchanged first-party OpenIddict behavior in isolated development mode with ephemeral keys. Do not import/copy Workbench-private vendor helpers.

Authenticate through POST `/_elsa/identity/login`, obtain the real cookie, then GET `/_elsa/identity/token` with that cookie to obtain the ordinary first-party bearer. Use it for backend and protected Studio package endpoints. No fixture-issued token or anonymous Studio administration substitutes for this flow. Native principal/permission services remain in place.

Root authentication/authorization services retain CShells delegation; map shells before explicit authentication/authorization middleware, as required by normal shell-scoped schemes. Configure CORS for the exact owned Studio origin, including needed native response headers. Keep normal Identity shell middleware and allowed return-origin configuration.

Use public native AddNuplane/loading and WithNuplaneFeatureDiscovery with Enabled=true, ChangedOrPending refresh and AutoReload=true. Before baseline, explicitly share the host's actual Agent.Core, Identity.Core, Preferences.Core and CShells contract identities; declare actual package versions as host-provided, with no source-build exemption. Audit assembly identity/location and scoped contributor resolution after installation.

Each host has its own explicit directory feed, coordinated enrolled immutable-store state, installs root and lock file under its owned content root. Enrollment/reconciliation uses public native operations, retaining intended archive hash and actual serving-generation change. Enable a resource feature only after its package is admitted; operator configuration may change, compiled host bytes may not. Baseline references contain neither resource package nor its concrete types.

## Studio

Start from the public package-host sample: Elsa.Studio.Hosting and Hosting.Nuplane, Dashboard, Workflows and the existing Weaver.Workflows integration package. Add their public feature assemblies through ConfigureShells; do not copy the reference Web implementation. Use AddElsaStudioNuplane and its normal coordinated native store/profile and management mappings.

Set one fixed Studio HostId matching the backend resource module's trusted host setting; configure the real backend URL, authentication enabled and native bearer session introspection. Keep the management credential server-side. Product SDK/shared React, Dashboard, Workflows and Weaver assets are produced and packaged before baseline; external host build/publish does not invoke frontend tooling.

Use the same explicit owned feed/store/lock/enrollment rules as above. Native upload/delete/reconcile responses are acknowledgements: verify installed membership, admitted module manifest/compatibility, shared runtime asset hashes and serving generation separately. The resource Studio package enters only through this native feed. The incompatible variant changes RequiredSdkVersion in its own metadata; restore original compatible archive bytes before final positive repetition.
