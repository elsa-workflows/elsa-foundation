# Owned external-authentication qualification probe

Development-only, non-packable executable for spec 1212. It composes the actual Identity/API/managed OIDC/native EF/OpenIddict implementations with two controlled real HTTP OIDC providers. It shares the provider fixture and explicit vendor EF wiring used by backend tests; it does not replace authentication handlers with fake responses.

```sh
dotnet build tools/identity/ExternalAuthentication.Probe/ExternalAuthentication.Probe.csproj
pnpm --dir Studio --filter @elsa-workflows/studio-external-authentication test:browser
```

The browser runner owns Vite, Kestrel and Chromium processes, uses ephemeral loopback ports and creates an owned temporary directory. A nonempty custom `ELSA_EXTERNAL_AUTH_PROBE_ROOT` needs the exact `.elsa-3579-owned-probe` marker. The probe stores native SQLite databases, Data Protection keys and an owner-readable signing key there. It never opens existing Workbench/Studio stores. It retains its owned evidence/stores on exit; remove that directory only after retaining needed evidence. It stops only processes it started, using SIGTERM and a bounded fallback. A timed-out startup observation is not a request to launch another instance.

Direct invocation accepts `--owned-root <new-directory>` and optional `--ui <owned-loopback-vite-url>`. Public `probe.json` contains loopback addresses/client ID/secret-reference names, never secret values. The asset proxy forwards GET assets only, stays on the selected loopback origin, and never forwards authentication/cookie headers. The known development administrator is the existing `TestAdmin` fixture; do not expose this probe beyond loopback.

The Chromium journey signs in through normal local login, creates a disabled Studio connection using existing Elsa roles and a secret reference, tests real metadata/JWKS, follows an actual provider preview and restores the saved draft, checks unchanged user/link/token rows and administrator identity, enables the connection, checks narrow layout, then completes ordinary external reader sign-in in a separate browser context. Through the real user-access component it refuses removal of the last sign-in link, attaches the second provider to the existing user, completes normal sign-in through that provider, removes the first link, verifies denied reader panels and revokes the selected session. Its cookie is rejected and access/refresh credentials return 401. It scans both pages with axe against WCAG 2 A/AA and 2.1 AA tags using actual Studio Classic light styles. Redacted aggregate counts/result categories and screenshots are retained; no browser trace or credentials are published.

This proves a controlled HTTP provider, native SQLite and the mounted product component/public transport. It does **not** prove an external IdP product, the full Studio host, separately hosted/cross-site cookie topology, server databases, restart or shared separate-node behavior. The separate profiles below cover additional boundaries; their results and limits are recorded in the
[spec 1212 evidence](../../../specs/1212-external-authentication/evidence.md).

## Provider interoperability and reference hosts

The additional runners always allocate new owned roots and stop only their own processes/labelled containers.
Their aggregate JSON receipts and screenshots stay in those roots. No credentials or browser traces are published.
Build the probe and frontend module before invoking them; the runners intentionally use the built artifacts.

```sh
docker pull quay.io/keycloak/keycloak@sha256:3f3c5f8e03c614774b4bd9173b88391fefa4153bfc010d8012d59ad825377332
node tools/identity/ExternalAuthentication.Probe/qualify-keycloak.mjs

# Reserve capacity first. This wraps the existing standard composed runner once;
# it includes its normal builds and browser inventory, not just compilation.
export ELSA_EXTERNAL_AUTH_UI_BUILD_ROOT="/tmp/elsa-3579-ui-build-<unique>"
node tools/identity/ExternalAuthentication.Probe/ui-build-provenance.mjs pin --output "$ELSA_EXTERNAL_AUTH_UI_BUILD_ROOT.toolchain.json"
node tools/identity/ExternalAuthentication.Probe/ui-build-provenance.mjs controls --root "$ELSA_EXTERNAL_AUTH_UI_BUILD_ROOT" --toolchain "$ELSA_EXTERNAL_AUTH_UI_BUILD_ROOT.toolchain.json"
node tools/identity/ExternalAuthentication.Probe/ui-build-provenance.mjs studio --root "$ELSA_EXTERNAL_AUTH_UI_BUILD_ROOT"
node tools/identity/ExternalAuthentication.Probe/ui-build-provenance.mjs probe --root "$ELSA_EXTERNAL_AUTH_UI_BUILD_ROOT"
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

## Representative full Studio modes

Use the same verified Studio/probe build receipts through `ELSA_EXTERNAL_AUTH_UI_BUILD_ROOT` for each independent profile; each invocation owns a fresh root:

```sh
node tools/identity/ExternalAuthentication.Probe/qualify-studio.mjs empty
node tools/identity/ExternalAuthentication.Probe/qualify-studio.mjs configuration
node tools/identity/ExternalAuthentication.Probe/qualify-studio.mjs provider-reader
node tools/identity/ExternalAuthentication.Probe/qualify-studio.mjs denied
node tools/identity/ExternalAuthentication.Probe/qualify-studio.mjs external-owned
node tools/identity/ExternalAuthentication.Probe/qualify-studio.mjs hybrid
node tools/identity/ExternalAuthentication.Probe/qualify-studio.mjs missing-api
node tools/identity/ExternalAuthentication.Probe/qualify-studio.mjs missing-sessions
```

These use saved administrator role permissions, actual ownership settings and actual omitted module/store
registrations. The browser checks current metadata against visible read-only/denied/unavailable actions and
requires zero external-management writes except the empty profile's explicit first-draft creation. External-owned mode refuses link changes and session revocation;
hybrid preserves read-only links but permits session revocation under the application authority. Missing API
capability uses the host's unavailable-module surface. Missing durable sessions keeps ordinary local login while
explaining unavailable session administration. Each profile scans settled light/dark themes with axe, changes
colour mode with the keyboard and checks 360-pixel layout without horizontal overflow.

The default controlled-provider full Studio journey additionally edits/disables/archives/restores/removes a
managed connection, then prepares/tests/previews/promotes a complete same-key override. It proves an ordinary
draft does not shadow, a disabled explicit override does, archive/removal reveal the baseline and restore
reinstates the override. Actual Keycloak and cross-process proofs remain separate profiles above.

## Packed public API consumer

```sh
python3 tools/identity/ExternalAuthentication.Probe/qualify-packages.py
```

This requires a clean committed checkout, archives that revision into a new owned directory, and builds/packs
only the four selected Identity roots and their production project closure. Candidate versions and repository
commit metadata are checked. An independent console application outside the archive restores exact local NuGet
candidates with a lock file and fresh package directory, with no project references. It composes optional managed
OIDC/API/artifact/session registrations, implements the public pure provisioning-policy contract and checks exact
scoped token identity values. It does not claim to repeat the full HTTP/provider journey. Reports, package hashes,
producer sources and build/consumer logs remain in the owned directory; nothing is published.

Full Studio captures explicitly scroll to provisioning, callback URLs, preview results and enablement actions in
light/dark desktop/narrow layouts; outer-document full-page capture cannot establish those internally scrolled
panels. Confirmation screenshots and cancellation/focus restoration are included. The provider-reader profile
uses an unavailable secret to prove current readiness remains visible without management/test permission.

## Source for remaining composed UX states

The primary controlled-provider full-Studio profile enables `uiRecovery` in its **owned probe only**. It creates a private per-invocation control key, seeds a pending ordinary user, and installs a standard registered session-store decorator. Protected loopback controls can deny one ordinary provider authorization or arm a single selected active session for a failure before the real write or after its real commit. Every other operation delegates to the actual store. No production service, identity policy, saved role or schema gate is changed. Keycloak and the representative permission/ownership profiles do not enable these faults.

The source exercises invalid/missing fields and correction, owned discovery404 and preview-denial recovery, connection/user no-match recovery, real catalog CAS and qualified-link ownership conflicts, before-write and lost-acknowledgment revocation recovery, and current-session revocation/reauthentication. The original successful sign-in/revocation assertions remain. The additional `empty` profile inspects an empty managed catalog, creates/reopens its first disabled draft, and inspects a pending user without links/sessions. It does not claim a globally empty user database while an administrator is required. Custom minimum viewport is360px.

`ui-evidence.mjs` records ordered named actions, selected ARIA, focus, native-invalid-field flags and hashed selected screenshots. It rejects authentication/callback routes, unexpected query data and credential entry; the chooser requires blank credentials. Public snapshots omit unknown form values and retain only a closed vocabulary of public fixture draft values. Known authentication material/control sentinels fail capture. This is bounded owned-fixture handling, not a general web-page sanitizer. A missing checkpoint or changed file hash fails the receipt. Source SHA and actual dirty state accompany each record.

Raw selected ARIA remains in an owner-only `elsa-3579-ui-private-*` directory outside the artifact glob, referenced by the owned root's hidden `.ui-private-evidence` pointer. CI exports only the checked `ui-evidence/*.json` and screenshots. Failed/unsafe captures are withheld with a fixed marker instead of dumping authentication pages or body text. Credentials, storageState, headers, request bodies and raw native traces are not captured. These records are **not native Playwright replay**; native replay remains explicitly unverified. The source and privacy-control tests require actual execution and independent evidence inspection before any UX acceptance claim.

Full-Studio launch also requires successful build receipts from `ui-build-provenance.mjs`, bound to the clean source commit/tree and actual output hashes. Its `studio` phase wraps the existing standard runner, so it adds no second whole build or browser inventory; local execution still needs a capacity grant and a bounded launcher for that runner's child builds. The `probe` phase performs only the ordinary locked scoped restore/build with at most two workers. It can run first in a new owned build root; Studio is a later phase. Failed commands emit no successful receipt and existing receipts cannot be overwritten or reused for another source.

`pin` performs metadata-only probes and writes the actual Node executable/hash/version, ordinary PATH-selected dotnet launcher/hash, real dotnet host/hash/version and resolved SDK path/version/entry/MSBuild hashes. The Python launcher/hash/version is pinned as the expected Studio producer too. The owned build root keeps that explicit pin. Commands and launches use the recorded executable paths, and pre/post probes refuse tool or SDK drift even though repository `global.json` permits SDK roll-forward. No global SDK policy or system installation is changed. Successful build receipts require matching actual command executions, zero exits and confirmed owned-group cleanup; bare argv labels cannot certify a launch. Nonempty string argv, absolute executable/real paths and full SHA-256 identities are mandatory. Command count, order, arguments and executable must exactly match the fixed controls, Probe or Studio recipe; missing, extra, malformed and substituted commands fail closed. Controls receipts cannot certify a build.

The POSIX-only bounded launcher creates a new process group for each command and handles SIGINT, SIGTERM, cancellation and deadlines with TERM/KILL plus bounded reaping. It drains the group after ordinary exits too; unconfirmed cleanup fails and retains the allocation. Probe operations have one 20-minute queue-inclusive ceiling (restore at most 10 minutes, build at most 12 minutes within that shared ceiling), with up to 20 additional seconds for termination/cleanup on failure. Persistent build servers, MSBuild node reuse and shared compilation are disabled for these scoped commands. The controls phase has a two-minute command ceiling and runs the eleven pure controls plus two tiny owned-child deadline/cancellation controls, serially; it starts no product host, browser or compiler. All actual test/build execution still requires a scheduled grant. The standard Studio phase remains separate, has a 30-minute operational ceiling and allows its existing producer up to 60 seconds to clean its own owned children before escalation; it is not included in FirstV.

Before and after a custom journey, hashes must still match the launched managed/native binaries, dependency/runtime manifests, Studio static asset roots and built external-module files. Studio must serve the recorded module JavaScript/CSS bytes without redirects. The checked source/build/output/served-asset records accompany each composed receipt and `build-provenance.json`. CI uploads those safe records and the fixed `failure-*.json`/`mode-failure.json` omission markers, while keeping build logs, control keys and private ARIA outside the artifact globs. Hash receipts are local build evidence, not signed supply-chain attestations. Provenance controls and actual launches remain unverified until executed.
