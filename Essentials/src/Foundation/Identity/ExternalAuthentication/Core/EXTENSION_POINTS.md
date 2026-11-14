# External authentication contracts

Optional managed-connection contracts within Foundation Identity. This Core depends on Identity.Core and
Secrets.Core; base Identity.Core has no dependency on managed connections. Canonical identity/authentication
terms: [authentication architecture](../../../../../../docs/reference/authentication-architecture.md).

| Contract | Kind | Consumer and invariant |
|---|---|---|
| `IExternalLinkAuthorizer` | Replacement | `CaptureAsync(ClaimsPrincipal actor, string userId, string? expectedUserRevision, CancellationToken)` captures the unchanged active target and live IAM revision guards before link administration. Default: internal `ExternalLinkAuthorizer` in `Elsa.Foundation.Identity.ExternalAuthentication`, installed by `AddFoundationExternalAuthentication`. Replace to specialize host link-management/delegation policy; preserve canonical eligibility, current permissions, ownership and coordinated revision checks. Depends on canonical identity/ownership authorities and revision-aware IAM stores; consumed by link-management transport and `IExternalLinkRecovery`. |
| `IExternalLinkRecovery` | Replacement | `CaptureAsync(ClaimsPrincipal actor, ExternalLinkAuthorization authorization, ExternalIdentityKey excluded, CancellationToken)` proves remaining user and administrator access while excluding the removed link. Default: internal `ExternalLinkRecovery` in `Elsa.Foundation.Identity.ExternalAuthentication`, installed by `AddFoundationExternalAuthentication`. Replace for a host recovery policy with equivalent read-only evidence and revision guards. Depends on the captured authorization, IAM stores, canonical eligibility/current permissions, local-login and protected external recovery authorities; consumed by coordinated link removal. |
| `IExternalAdmissionService` | Replacement | Assesses without writes; normal admission rechecks the effective connection and delegates to atomic IAM admission. Inputs come only from a validating server protocol adapter. |
| `IExternalProvisioningPolicy` | Named contribution | Integrator supplies a read-only policy under an explicit unique name. It selects existing users or declared Elsa default roles; it cannot import provider permissions or expand the approved role boundary. |
| `IExternalAuthenticationProtocol` | Named contribution | Installed protocol validates metadata and identity evidence for both normal admission and preview. Inputs/results stay server-side; no raw upstream credentials are retained. Broker owns browser/purpose/revision-bound one-time state. |
| `IExternalAuthenticationFlowService` | Replacement | Coordinates protocol, protected one-time state and admission. Uses a trusted public callback base, pins connection material and secret generation, and rechecks them before code exchange. Preview requires the initiating authorized canonical administrator and never calls normal admission. |
| `IAuthenticationArtifactStore` | Replacement, optional authority | Atomic durable create/consume with tenant, purpose, browser and administrator bindings. No default in-memory authority is installed. Only an explicitly Development host may use a non-durable replacement. A consume result must already be committed; caller-owned transactions cannot defer or roll it back. |
| `IConnectionRegistry` | Replacement | Backend authentication/admin reads a live effective view with provenance. Deployment is baseline; same-key data overrides only with explicit intent and deployment permission. |
| `IConnectionCatalogStore` | Replacement | Complete bounded catalog read/CAS. A null revision is create-only. Never split the final-path mutation into independent per-record writes. |
| `IConnectionWriter` | Replacement | Management authorizes before global access, validates policy/lifecycle and performs one revision-checked catalog save. |
| `IConnectionWriteAuthorizer` | Replacement | Host-tenant provider-management permission and existing-Elsa-role delegation. Destructive retirement needs management; activation/policy changes also need delegation. |
| `IConnectionReadiness` | Replacement | Installed protocol, required settings, admission/role references and required secret resolution. This describes configuration readiness, not observed health or eligible recovery identities. |
| `IConnectionRecovery` | Replacement | Recent protected normal administrator sign-in evidence, bound to the issued session/account version and record/material/issuer/callback/credential generation; rechecks current eligibility, permission, exact link and bounded metadata/JWKS. Configuration alone never counts. |
| `ILocalLoginRecovery` | Replacement | Current canonical actor first, then a bounded local user page scan; requires an enabled password method, password, active identity/membership, framework eligibility and current provider-management permission. |
| `IConnectionSignInEvidence` | Replacement | Records successful normal external administrator sign-ins in a private protected observation envelope. Preview and ordinary reader sign-ins never establish recovery evidence. |

At the flow-coordination stage, `AddFoundationExternalAuthentication` installs catalog/registry, admission policies,
writer/delegation, protected observation, readiness/recovery, link authorization and the flow service. Protocol implementations are optional contributions (the managed OIDC adapter is available separately). Artifact and session stores are optional persistence contributions.
Canonical session enforcement belongs to the selected Identity adapter; HTTP entry points are provided by
the optional protocol API. Missing dependencies
never count as a usable path. Register replacements before Foundation and preserve duplicate-contract diagnostics.
Readiness describes installed configuration; recovery additionally requires current eligible authority and
protected successful normal-sign-in evidence. A preview cannot supply that evidence.

`ConnectionRecord` is a full source document. `Id` identifies a management record; immutable `Key` identifies
long-lived provider links. Source/shadowing comes from the registry, not a writable source field. Secrets are
`SecretReference` objects. Admission/default role IDs reference existing Elsa roles. Integrator policies may assign saved roles at initial
provisioning; later sign-ins use saved memberships. Recurring external group-to-role mappings are excluded
under the owner decision in spec 1212; upstream role names confer no automatic authority.

`ExternalAdmissionContext` is server-only input. Client preview/login requests carry a flow handle; the validating
protocol adapter supplies issuer, subject and claims after verification. An assessment is prospective: it creates
no user, link, cookie, token or session. Custom provisioning policies must honor this read-only contract.
The IAM authority's atomic admission operation receives the existing-user and tenant-membership snapshots so
changes during matching cause a conflict instead of attaching a more privileged identity.
