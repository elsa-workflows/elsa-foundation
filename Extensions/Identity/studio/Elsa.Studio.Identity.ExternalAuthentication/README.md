# External sign-in Studio module

Optional `ExternalAuthenticationStudio` feature. It contributes **Security → External sign-in** when the selected backend advertises `elsa.api.identity.external-authentication` contract 1 and the user has provider read/manage access. It consumes the public Studio SDK/UI and the backend [managed authentication API](../../../../Essentials/src/Foundation/Identity/ExternalAuthentication/Api/README.md).

**Security → User access** requires user-read permission on that backend. Its bounded user directory does not require provider-management access. Link and session panels independently require their dedicated read permissions; each action follows current server permissions, ownership capabilities and module availability. Link attachment selects a configured connection and its exact external subject. Removal checks remaining user and administrator recovery paths. Selected-session revocation invalidates its cookie, access and refresh credentials while preserving other sessions. These actions preserve saved Elsa roles.

Configured enablement, current configuration readiness and prior test observations are shown separately.
Selecting or refreshing a connection rechecks current structural/secret readiness through the read-permission detail
endpoint, including for read-only operators. A revoked read permission clears the protected catalog/editor state.
Refreshing user access reconciles the selected user with current metadata and clears a disappeared selection.

Changes require confirmation and current server revisions. An uncertain or conflicting result requires a refresh before another change; the page preserves an unattached draft. Revoking the current session requires signing in again. Legacy issuer-less links, externally owned users and missing administration services display read-only guidance.

Save a disabled connection draft, register the displayed sign-in and preview callbacks with the provider, test metadata, preview an actual sign-in, then enable it. A permitted deployment override starts as a complete disabled Studio draft; promotion is explicit after test/preview. Assessment shows the resulting sources before confirmation; writes carry the catalog revision. Deployment and unsupported configurations remain read-only. Archived records retain their explicit lifecycle controls.

The editor selects existing Elsa roles for the provisioning policy and accepts only existing text-secret references. The public metadata-only picker is optional; integrators without that endpoint can enter the reference name/scope. Provider groups do not grant roles automatically. Later sign-ins use saved Elsa memberships and preserve manual assignments. See the [role decision](../../../../specs/1212-external-authentication/research.md#r6--provisioning-policy-only-owner-decision-4-november) and [identity configuration](../../../../docs/reference/identity-configuration.md).

Preview uses full-page provider navigation and returns to the same Studio route/backend/tab. It requires the administrator's ordinary backend cookie; a different bearer/cookie identity is refused. Only source/test metadata is retained in session storage. The one-time result handle is removed from the address bar before retrieval; returned identity/permission data stays in component memory. A bound provider failure returns a closed message to the saved draft without enabling it. Invalid state or a revoked return-origin policy cannot redirect. Configure an allowed Studio return origin on the backend when origins differ. Cross-site cookie topologies still require the deployment qualification tracked in spec 1212.

Dirty drafts guard tab close/navigation and browser unload. Responses from a previously selected backend cannot replace the current page. Module CSS consumes `--studio-*` tokens.

## Standalone client

The module build ships a React-independent `sdk.js` and `sdk.d.ts` at `_content/Elsa.Studio.Identity.ExternalAuthentication/studio/sdk/external-authentication/`. `ExternalAuthenticationClient` accepts a selected backend base URL and caller-owned `getJson`/`postJson` transport. The transport owns authentication and backend path-base resolution; it must honor `credentials`, antiforgery headers and cancellation. The client reads configured protocol paths and actual antiforgery field/header names rather than assuming defaults. It exposes source/catalog/detail, assessment/commands, test, existing roles, metadata-only secret picking, preview submission/results, bounded user/link/session inspection, link attachment/removal and selected-session revocation. Identity commands discover their protocol path through the user directory without reading provider configuration. It never resolves secret values.

## Verification

From the repository root:

```sh
pnpm --dir Studio --filter @elsa-workflows/studio-external-authentication typecheck
pnpm --dir Studio --filter @elsa-workflows/studio-external-authentication test
pnpm --dir Studio --filter @elsa-workflows/studio-external-authentication build
dotnet build tools/identity/ExternalAuthentication.Probe/ExternalAuthentication.Probe.csproj
pnpm --dir Studio --filter @elsa-workflows/studio-external-authentication test:browser
```

The browser command mounts the real connection and user-access components against an owned composed backend/controlled HTTP provider and native SQLite. See the [probe](../../../../tools/identity/ExternalAuthentication.Probe/README.md) and [evidence](../../../../specs/1212-external-authentication/evidence.md). It does not substitute for full Studio host, external IdP product, server-database or separate-node acceptance.

The probe's separate Keycloak, full Studio and Workbench runners qualify provider interoperability and actual
reference hosts. Their receipts identify the precise profile and remaining deployment limits. Full Studio uses its
normal administrator token exchanges during API calls/reload; those are counted separately from preview effects.
