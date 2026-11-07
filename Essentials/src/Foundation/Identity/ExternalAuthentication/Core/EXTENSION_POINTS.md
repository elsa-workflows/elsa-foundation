# External authentication contracts

Optional managed-connection contracts within Foundation Identity. This Core depends on Identity.Core and
Secrets.Core; base Identity.Core has no dependency on managed connections. Canonical identity/authentication
terms: [authentication architecture](../../../../../../docs/reference/authentication-architecture.md).

| Contract | Kind | Consumer and invariant |
|---|---|---|
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

Register replacements before `AddFoundationExternalAuthentication`; duplicate competing registrations fail
through the shared replacement-contract diagnostics. The managed defaults compose the installed protocol, IAM and session adapters for readiness and
recovery. Missing dependencies do not count as ready/recovery. A removal/replacement of an
enabled effective path requires a proved remaining path or local recovery; configuration validity alone is insufficient.

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
