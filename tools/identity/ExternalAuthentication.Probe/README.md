# Owned external-authentication qualification probe

Development-only, non-packable executable for spec 1205. It composes the actual Identity/API/managed OIDC/native EF/OpenIddict implementations with two controlled real HTTP OIDC providers. It shares the provider fixture and explicit vendor EF wiring used by backend tests; it does not replace authentication handlers with fake responses.

```sh
dotnet build tools/identity/ExternalAuthentication.Probe/ExternalAuthentication.Probe.csproj
pnpm --dir Studio --filter @elsa-workflows/studio-external-authentication test:browser
```

The browser runner owns Vite, Kestrel and Chromium processes, uses ephemeral loopback ports and creates an owned temporary directory. A nonempty custom `ELSA_EXTERNAL_AUTH_PROBE_ROOT` needs the exact `.elsa-3579-owned-probe` marker. The probe stores native SQLite databases, Data Protection keys and an owner-readable signing key there. It never opens existing Workbench/Studio stores. It retains its owned evidence/stores on exit; remove that directory only after retaining needed evidence. It stops only processes it started, using SIGTERM and a bounded fallback. A timed-out startup observation is not a request to launch another instance.

Direct invocation accepts `--owned-root <new-directory>` and optional `--ui <owned-loopback-vite-url>`. Public `probe.json` contains loopback addresses/client ID/secret-reference names, never secret values. The asset proxy forwards GET assets only, stays on the selected loopback origin, and never forwards authentication/cookie headers. The known development administrator is the existing `TestAdmin` fixture; do not expose this probe beyond loopback.

The Chromium journey signs in through normal local login, creates a disabled Studio connection using existing Elsa roles and a secret reference, tests real metadata/JWKS, follows an actual provider preview and restores the saved draft, checks unchanged user/link/token rows and administrator identity, enables the connection, checks narrow layout, then completes ordinary external reader sign-in in a separate browser context. Through the real user-access component it refuses removal of the last sign-in link, attaches the second provider to the existing user, completes normal sign-in through that provider, removes the first link, verifies denied reader panels and revokes the selected session. Its cookie is rejected and access/refresh credentials return 401. It scans both pages with axe against WCAG 2 A/AA and 2.1 AA tags using actual Studio Classic light styles. Redacted aggregate counts/result categories and screenshots are retained; no browser trace or credentials are published.

This proves a controlled HTTP provider, native SQLite and the mounted product component/public transport. It does **not** prove an external IdP product, the full Studio host, separately hosted/cross-site cookie topology, server databases, restart or shared separate-node behavior. Those acceptance obligations remain open in [spec 1205](../../../specs/1205-external-authentication/tasks.md).

## Provider interoperability and reference hosts

The additional runners always allocate new owned roots and stop only their own processes/labelled containers.
Their aggregate JSON receipts and screenshots stay in those roots. No credentials or browser traces are published.
Build the probe and frontend module before invoking them; the runners intentionally use the built artifacts.

```sh
docker pull quay.io/keycloak/keycloak@sha256:3f3c5f8e03c614774b4bd9173b88391fefa4153bfc010d8012d59ad825377332
node tools/identity/ExternalAuthentication.Probe/qualify-keycloak.mjs

dotnet build Studio/src/apps/Elsa.Studio.Web/Elsa.Studio.Web.csproj
node tools/identity/ExternalAuthentication.Probe/qualify-studio.mjs
# Combine the same full Studio host with both real Keycloak providers:
node tools/identity/ExternalAuthentication.Probe/qualify-keycloak.mjs --studio

dotnet build Essentials/apps/Elsa.Workbench/Elsa.Workbench.csproj
node tools/identity/ExternalAuthentication.Probe/qualify-workbench.mjs
```

The pinned Keycloak 26.8.1 runner uses two independent instances, each limited to one CPU/1 GiB, with imported
disposable realms/clients/accounts and exact callback allowlists. Their users deliberately share the same subject
ID and have an upstream administrator membership. It runs the ordinary connection/user-access journey against
the actual provider product, including code/PKCE, metadata/JWKS, preview, sign-in and revocation. Separate instances
allow equal user IDs without conflicting with Keycloak's global user primary key. See the upstream
[container](https://www.keycloak.org/server/containers) and [realm import](https://www.keycloak.org/server/importExport) documentation.

The Studio runner starts the actual reference application with an isolated content root, package feed and backend
profile. Its public SDK uses the real capability and authentication endpoints across separate same-site origins,
with an exact credentialed CORS/return-origin allowlist. Dialogs, navigation, retained workspace tabs and theme
controls are the product implementations. The standard Studio redirect adapter requests an administrator token
pair for API calls/reload; the browser accounts for exactly those ordinary exchanges, checks their administrator
subject and refuses any extra preview-issued credentials. Isolated protocol runs still require zero token-row change.

The Workbench runner proves actual shell activation and capability registration, local sign-in, managed catalog
and durable session inspection. It uses owned native SQLite stores for IAM, tokens, artifacts and Data Protection,
and random fixture credentials/signing material. It is a composition smoke; provider/browser journeys are recorded
separately. These profiles do not establish server-database/restart/separate-node or universal cross-site acceptance.

## Restart and separate processes

After building the probe and standalone SDK, run each profile against new owned resources:

```sh
node tools/identity/ExternalAuthentication.Probe/qualify-nodes.mjs --sqlite
node tools/identity/ExternalAuthentication.Probe/qualify-nodes.mjs --sqlite --baseline
node tools/identity/ExternalAuthentication.Probe/qualify-nodes.mjs
node tools/identity/ExternalAuthentication.Probe/qualify-nodes.mjs --baseline
```

The default engine is PostgreSQL 16.15, using the locally installed image
`postgres@sha256:0cefaabd882e42bcb2a2f3dc56d3ce4770560ba62fd7f197f1a2e008fd5731ab`; pull that digest first if absent.
The runner verifies the actual server version. Its owned container has one CPU/768 MiB, an invocation-specific
database user and two private databases: shared IAM/catalog/artifacts, and the explicit vendor token store.
Connection files remain private inside the owned root; the probe refuses PostgreSQL outside its loopback fixture
namespace. SQLite uses owned files on this machine and establishes no multi-machine SQLite claim.

Two independently started backend processes share the configured stores, application discriminator, filesystem
Data Protection ring and owner-readable signing key. An owned loopback proxy deterministically routes requests
to either node while keeping the public callback URL stable. A separate provider process keeps real signed
OIDC/PKCE grants alive while backend processes restart. Its private controller selects disposable upstream
subjects; each grant captures its subject at authorization time. No backend domain response is intercepted.

The profiles prove same-node restart, start-on-A/complete-on-B, concurrent single completion and replay refusal,
equal-subject issuer isolation, shared cookie/bearer validation, selected revocation and unrelated refresh survival.
Each node receives its own retained-cookie snapshot for revocation checks. Two guarded unlink requests cannot
remove the final user path; removed-link credentials fail while the other link stays usable on both processes.
Managed mode also proves catalog CAS and disabled override/archive/restore/remove observation across nodes.
Deployment-only mode installs no provider-configuration store and refuses catalog writes; a rolled-out deployment
material change invalidates pending state. Restoring prior material cannot revive rejected state.

Contexts/processes stop before PostgreSQL is frozen to private custom-format dumps. Teardown verifies ownership
and removes only the owned container and its anonymous volumes. Receipts mark inapplicable catalog races/lifecycle
as null in deployment-only mode. These are HTTP process/store proofs, separate from the browser, external product
and full Studio profiles; they do not establish universal cross-site or other server-engine qualification.
