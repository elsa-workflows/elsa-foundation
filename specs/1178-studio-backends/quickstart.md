# Qualification journeys

1. Configure local A and B with different authoring/runtime capabilities and colliding resource IDs. Open Backends list; inspect Connection and Capabilities tabs; create/edit/cancel personal metadata; reload and check ownership.
2. Switch A -> B; assert backend-qualified Home, distinct data, no A bearer/reads/streams on B, and independent second-tab selection. Failed connection/cancel keeps A active.
3. Hold a write or dirty workflow/feature draft, attempt switch, and prove no commit; finish/revert and retry. Hold an old read until after switch and prove no stale data arrives in the new document.
4. Features targets the connected named backend. Stage a setting/dependency cascade, review, switch and prove old review cannot apply. Apply a real setting to A, verify B unchanged and capability discovery refreshed.
5. Verify missing/incompatible/dormant/forbidden/unavailable capability reasons, route/nav/action admission, and host settings/recovery during backend failure.
6. Inspect both themes and 320/390/desktop widths with keyboard navigation. Qualify package-only Hosting consumption and an explicitly composed durable preference provider.


The source-host witness is `Studio/tests/live/backend-connections-journey.spec.ts`. Run it only against owned
Workbench content roots and independent database files. Keep the first host's ordinary authoring/runtime composition;
remove the authoring API features and their required dependents from the second. Keep shell routing Path equal to
its blueprint name (`default` in the witness). Retain the stores/providers and identity services required by each
remaining feature. A missing API is not a substitute for an invalid server composition.

For the cookie flow use separate hosts in one site (or an explicitly configured HTTPS cross-site provider), each
allowing the exact Studio origin through CORS and identity return-URL policy. Local Chromium host resolution can map
`app.studio.test`, `api-a.studio.test` and `api-b.studio.test` to loopback; no system hosts file is changed. The Studio
server-side identity authority may independently use the loopback API URL.

```sh
cd Studio
ELSA_STUDIO_URL=http://app.studio.test:56640 \
ELSA_BROWSER_HOST_RESOLVER_RULES='MAP *.studio.test 127.0.0.1' \
ELSA_BACKEND_CONNECTIONS_LIVE=1 \
ELSA_BACKEND_PRIMARY_SHELLS=/absolute/owned/authoring/shells.json \
ELSA_BACKEND_SECONDARY_SHELLS=/absolute/owned/runtime/shells.json \
pnpm exec playwright test --config tests/live/playwright.live.config.ts backend-connections-journey.spec.ts
```

The witness uses profiles `authoring` and `runtime`, verifies personal CRUD/reload, a failed candidate, guarded feature
settings, actual reviewed Apply on the primary file with the secondary unchanged, fresh capability discovery,
independent authentication/bearers, safe Home selection, native Back/Forward and backend-qualified unavailable routes.
It uses no API mocks. Same-ID/late-read, pending-write, exact-review identity and dormant/incompatible/denied matrices
are deterministic regressions in the Hosting and Features suites. Browser fixture tests cover both modes at 320–1440px.
Build frontend/vendor assets to completion before starting browser qualification against the source host.
