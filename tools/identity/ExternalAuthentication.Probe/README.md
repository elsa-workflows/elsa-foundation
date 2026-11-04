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
