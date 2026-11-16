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

## Source-preparation and acceptance state

The current Studio host source includes this optional module's project, assembly and shell feature. The
contributions still require the selected backend capability and current user permissions. Presence in the host
or package inventory does not establish an available or qualified administration experience.

Component/SDK and long-identity dialog regression source is included. The module uses the current public Studio
SDK/UI, shared styles and navigation/workspace/dialog contracts. Draft, cancellation/focus, delayed request and
backend-switch behavior remain explicit acceptance boundaries. Module styles use the public token contract.

After execution is authorized, run the affected component/SDK/type/declaration/frontend/Studio .NET gates,
then inspect current-source composed Studio/backend journeys across module, permission, ownership, source,
loading/error, theme and narrow-layout states. Retain downloadable exact-head visual evidence and independent
UX acceptance under [Studio UX acceptance](../../../../docs/contributing/studio-ux-acceptance.md).

The owned [Probe and shared controls](../../../../tools/identity/ExternalAuthentication.Probe/README.md) and its
browser helper modules are now source-staged. The `test:browser` entry points to those present dependencies;
invoking it would start owned processes and is still outside the current source-only authorization. It has not installed dependencies, built, typechecked,
executed tests, opened a browser/provider or established product/visual acceptance. Historical
records on the [closed carrier PR3806](https://github.com/elsa-workflows/elsa-foundation/pull/3806) remain bound
to their recorded source.
