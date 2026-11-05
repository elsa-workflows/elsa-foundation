# Managed external authentication

`FoundationIdentityExternalAuthentication` is an optional Identity feature for host-wide provider configuration.
It shares the existing Foundation Identity permission/token authority. Its contracts live in sibling `Core`;
managed OIDC belongs to the optional ExternalAuthentication/Oidc adapter; existing Identity.Oidc retains direct bearer support. The dependency envelope includes Secrets.Core
and persistence scope contracts without adding them to every base Identity.Core consumer.

Current implementation: live deployment/catalog selection, explicit complete overrides, catalog persistence/CAS,
role-delegation checks, lifecycle commands, the final-path guard, policy assessment/atomic admission and protected one-time flow coordination. The optional managed OIDC adapter implements protocol validation; the optional [API](../Api/README.md) connects normal cookies and administrator preview. The optional API also implements source/revision/permission-protected management, bounded lookups/testing,
link administration and selected-session revocation. The optional Studio module exposes those capabilities.
Composition and qualified provider, browser and process/store profiles are recorded in the
[qualification evidence](../../../../../../specs/1212-external-authentication/evidence.md).
This module alone does not compose a complete external sign-in deployment.

## Configuration and composition

Enable the normal `FoundationIdentityAbstractions` feature and the session/protocol adapters required by the
host. `AddFoundationExternalAuthentication` is the programmatic registration entry point.

- `TenantId` is the deployment's identity/administration tenant (`default` initially). Connections have no editable tenant field.
- `Connections` is the read-only deployment baseline. A record's deployment ID derives from its logical key.
- `AllowDatabaseManagement` defaults to false; disabled management never reads optional persisted catalog rows.
- `AllowDeploymentOverrides` defaults to false; `AllowedOverrideKeys` may constrain the permitted keys.
- `AllowedRoleIds` may further constrain default Elsa roles for database policy editing. The editor must also be authorized to delegate every role permission.
- `RecoveryEvidenceLifetime` defaults to 24 hours (one minute to thirty days). Successful normal administrator sign-ins provide recovery evidence for that interval.
- `RecoveryScanLimit` defaults to 1,000 local accounts (maximum 10,000). Recovery checks consider the canonical actor first, then bounded pages; unexamined accounts never count optimistically.
- `PublicBaseUrl` is the trusted public backend URL, including its path base. Callback construction never uses an incoming Host header. `CallbackPath` defaults to `/identity/external` below that base.
- `AllowedReturnUrlOrigins` explicitly permits additional HTTPS frontend origins; local return paths and the configured backend origin are accepted. The callback rechecks the policy. Loopback HTTP requires Development.
- `FlowLifetime` defaults to ten minutes (maximum fifteen); `PreviewResultLifetime` defaults to five minutes (maximum ten). Both must be at least one second.

The default catalog adapter uses the existing `IRevisionAwareProviderConfigurationStore`, one global
`external-authentication` record of kind `external-connections/v1`. Compose a provider-configuration backend
when database management is enabled. Reads/writes use their own explicit global scopes; writes are privileged
for this fixed catalog purpose after writer authorization. Caller tenant scope is not rebound. No EF package
or new provider schema is required here. Catalog bounds: 256 records and a one-megacharacter serialized payload;
settings/references/policy collections are bounded separately.

Baseline and same-key ordinary database drafts coexist without shadowing. Explicit override promotion selects
one full database document; no settings, secrets, policy or presentation fields merge. Disabled overrides still
shadow. Archive/removal reveals the baseline; restore reinstates the saved full override. Logical keys cannot
be edited. Source settings with duplicate keys fail closed. Create is always disabled; activation and lifecycle
commands are explicit. Test observations belong outside authentication material and do not alter material revisions.

Configuration readiness and recovery eligibility are separate Core contracts. Readiness validates the installed
protocol, current secret resolution, callbacks, artifact/session authorities, IAM, policy and role references.
It does not exchange a code or provision a user. Recovery additionally requires an eligible administrator.

External recovery uses a recent successful **normal** administrator sign-in bound to the actual source record,
material revision, callback, exact issuer/subject/user and current secret generation. Preview and reader sign-ins
never count. Recording also requires the actual freshly issued durable session for that user and connection;
its account-version binding is retained only inside the protected proof. The checker revalidates that binding,
current framework/user/membership status, provider-management permission,
qualified link and live bounded discovery/JWKS. A successful metadata read alone cannot prove the registered
client credentials or account. This is a recent observation, not a guarantee of upstream account availability.
Normal sign-in stays successful if optional observation recording fails; absent evidence cannot authorize retirement.

Observations reuse provider-configuration persistence in separate host-tenant/logical-key rows with CAS, a distinct
versioned kind and purpose-bound Data Protection envelope. Up to sixteen recent proofs are retained per key;
expired proofs are ignored and pruned on the next successful recording. They never affect authentication material
or expose credential bindings in management responses. Removed-key observation rows remain protected in that
store; there is no additional audit store or scheduled history cleanup. Nodes need the same key ring/application
name. Database-disabled hosts do not record these observations.
Older observations without an account-version binding do not count; a fresh normal administrative sign-in is
required. Rebinding a login away and back cannot revive its earlier observation. Ending an individual session
does not remove the credential's ability to sign in again, but that ended session cannot renew an observation.

Local recovery checks the enabled password method, an actual password, current framework eligibility, user and
membership status and current provider-management permission. Custom canonical session authorities must implement
read-only eligibility and durable-session support; the default `CanSignInAsync` returns false and the
default durable-session capability is unavailable. The ASP.NET implementation binds sessions through the
[optional durable store](../Persistence/EntityFrameworkCore/README.md). The optional API coordinates guarded
link administration and recovery with current IAM/catalog revisions; ordinary live credential validation also
checks current account and exact-link bindings.
For recovery recording, a custom canonical authority returns its issued `AuthenticationSessionId` on the
server-side `AuthSession` and supports reading/validating that committed session. This metadata is excluded from
JSON. Missing or mismatched bindings never produce recovery evidence.

Before disabling/replacing an enabled effective provider, the writer proves a remaining external recovery path
or actual local recovery, then saves the complete catalog with CAS. External checks share a thirty-second bound;
caller cancellation propagates. Concurrent workers cannot both save stale decisions against one catalog revision.
The native IAM adapter atomically checks user, role, link and catalog revisions in one owned serializable
relational transaction. It refuses split targets, caller-owned transactions and pending changes. The
[API README](../Api/README.md#link-and-session-administration) describes coordinated link mutations and recovery;
selected-session revocation uses the durable session store's independent revision guard.

## Extension points and tests

See [Core catalog](../Core/EXTENSION_POINTS.md). Replacement defaults are scoped. The immutable system clock and startup-only activation check are
singletons. `ExternalAuthenticationActivation` implements both `IShellInitializer` and `IHostedService` so plain
and shell hosts reject malformed deployment policy or missing opt-in persistence before serving requests.
It performs no table queries ahead of the persistence feature's migrations. No event handlers or recurring tasks are added in this stage.
Cross-domain consumption: Secrets.Core reference metadata and Runtime.Core's explicit persistence access scopes;
there are no anonymous dispatch or cross-domain event contributions.

Behavior tests: `Essentials/tests/Foundation/Identity/Tests/ExternalConnectionTests.cs` and
`ExternalConnectionPersistenceTests.cs` cover source lifecycle, stale/concurrent changes, invalid/unlinked recovery,
permission/delegation boundaries, redacted credential rejection, detached views, durable catalog restart and
shared-store CAS. These tests do not replace actual provider/Studio/multi-node qualification.

## Admission policies

Canonical role/identity behavior: [authentication architecture](../../../../../../docs/reference/authentication-architecture.md).
The default `Admission.Type = "reject"` refuses unlinked identities. Existing qualified links resolve only active
Elsa users and active tenant memberships. `"jit"` explicitly provisions a new external-only user with the configured
`DefaultRoleIds`; its user name is a deterministic opaque key, so equal email addresses at different providers do
not cause an implicit match. The existing IAM mutation creates the user and link together, with no local password.

`Type = "match"` requires `Matcher = "verified-email"`. The provider must supply exactly one email and one boolean
`email_verified = true`; only a unique active Foundation-managed address is eligible. An ambiguous address,
unverified evidence or an earlier imported external profile refuses rather than falling back to JIT. The existing
user's roles are preserved. Their effective role/direct-permission boundary must fit the connection's approved
`DefaultRoleIds`; a provider editor cannot use matching to attach an identity to a more privileged account.
Ownership/capability settings prevent local provisioning/link creation when user authority is external, while existing
links can still resolve through the selected IAM authority.

An integrator can register `IExternalProvisioningPolicy` with `services.AddScoped<IExternalProvisioningPolicy,
CompanyPolicy>()`, and select its unique `Name` in `Admission.Type`. `AssessAsync` returns
`ExternalProvisioningDecision` and must perform no writes or external side effects. For new users, it may select a
subset of the connection's declared `DefaultRoleIds`; host `AllowedRoleIds` also applies. This API assigns saved
Elsa roles at initial provisioning. Later sign-ins use saved Elsa memberships and preserve manual assignments. Recurring external group-to-role mappings are excluded for now.
For example, a policy can choose the declared `operator` role for a trusted department claim and `reader` otherwise:

```csharp
public sealed class CompanyPolicy : IExternalProvisioningPolicy
{
    public string Name => "company-onboarding";

    public ValueTask<ExternalProvisioningDecision> AssessAsync(
        ExternalAdmissionContext context, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new ExternalProvisioningDecision
        {
            Kind = ExternalAdmissionKind.NewUser,
            RoleIds = context.Claims.Any(claim => claim.Type == "department" && claim.Value == "Operations")
                ? ["operator"] : ["reader"]
        });
}
```

Both role IDs must already exist in Elsa and be declared for that connection. Raw upstream role and permission
claims never flow into the canonical principal. Policies return a closed failure category; no arbitrary provider
message is exposed. Preview calls only `IExternalAdmissionService.AssessAsync`; normal admission calls `AdmitAsync`,
which reloads current source/lifecycle/material state and reassesses before the atomic write. A disabled draft can
be assessed but cannot establish a normal login. The optional API supplies interactive preview and the selected canonical sign-in authority supplies the normal cookie.

## Protected one-time flows

`IExternalAuthenticationFlowService` coordinates begin/callback and preview-result consumption. Compose an
`IAuthenticationArtifactStore` authority and a shared Data Protection key ring with the same application name
on every backend node. The optional [EF module](../Persistence/EntityFrameworkCore/README.md) owns these artifacts;
the base IAM persistence package acquires no dependency on Secrets or managed authentication. Missing storage
refuses the flow. Non-durable replacement stores are accepted only in Development, never by default.

The API adapter must supply the purpose from its route and a cryptographically random browser binding from
its protected correlation cookie. These service inputs are server-only; they are not request-body contracts.
Normal and preview callbacks have distinct routes under the trusted base: `signin/{key}/callback` and
`preview/{key}/callback`. Preview also pins the selected management record ID, allowing a disabled or shadowed
unarchived draft to be tested without making it effective. Begin, callback and result read reauthorize the
initiating canonical administrator; another administrator cannot consume that flow or result.

Storage contains domain-separated hashes of handles/bindings and a Data Protection payload bound to tenant,
purpose, artifact ID, browser and administrator. State includes nonce, PKCE verifier, source record/material
revision, issuer, secret generation, exact callback and return destination. A matching callback consumes state
before code exchange; failures do not reopen it. Wrong browser/purpose/route/admin does not consume the correct
flow. Current source, lifecycle, material, callback, return policy and secret generation are checked again.
Provider errors return closed categories rather than raw upstream messages.

Normal completion returns a server-only admitted user ID and provenance; the optional API delegates to the
selected `ICanonicalUserSignInService` for the ordinary Elsa session. Preview runs pure admission assessment and creates a separately protected, bounded,
short-lived result artifact. Result reads consume it once and flag observations whose source material changed.
No raw upstream access/refresh tokens are persisted. Component tests use a real HTTP OIDC fixture, native SQLite
and owned shared key directories across independent service providers/restarts. Actual composed HTTP cookie/token
and preview isolation evidence lives in the optional API's tests. Actual Keycloak, reference Studio/Workbench,
and separate-process native SQLite/PostgreSQL managed and deployment-only profiles are reproducible with the
[owned qualification runners](../../../../../../tools/identity/ExternalAuthentication.Probe/README.md).
The [evidence](../../../../../../specs/1212-external-authentication/evidence.md) states their limits; these results
do not establish universal cross-site cookie behavior or runtime qualification for every supported EF engine.
