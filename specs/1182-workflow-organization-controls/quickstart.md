# Validation guide

Use isolated owned Workbench/Studio hosts following [the two-backend guide](../1178-studio-backends/quickstart.md) and the existing live suite instructions. Never rebuild/stop the user's 7030/7243 processes.

1. Build the affected API test project and Studio packages from the candidate. Run API contracts, complete affected Workflows/Features frontend suites, typecheck/lint, production bundle checks and targeted host builds. Keep red/green logs for setting discovery, stale mutation/placement refusal and browse recovery.
2. On backend A, create or retain a parent/child folder, tag assignment, private/shared views, favorites and a published workflow. Record all identities, revisions, owners, queries, columns and publication/executable values.
3. In Studio's existing Features UI, select Server/backend A. Confirm **Workflow organization** defaults true. Save a false draft, inspect the single-setting review and cancel once. Verify configuration is unchanged. Review/apply false; compare the changed settings and prove backend B is unchanged.
4. After refreshed availability, confirm organization controls/columns are absent, ordinary paged browsing works, and stale direct organization actions refuse without writes. Open an organization-filtered URL at desktop/320px; verify explicit keyboard recovery preserves unrelated search/state/backend values.
5. With organization off, perform normal workflow edit/publish and execute an existing published artifact. Record completion and incident state; a transient Test Run is not published-execution proof.
6. Restart only owned hosts while off. Confirm disabled behavior and retained data. Review/apply true, then compare all saved organization values/identities and restored optional-column choices. Exercise source-owned and denied-administration settings behavior without changing those sources or permissions.
7. Qualify responsive themes, stale delayed responses/dialogs and backend isolation. Post actual commands/counts/results on the PR; label fixture vs real-host evidence.
8. Complete independent code review and exact-head hosted gates. Merge only through the normal authorized path, then inspect actual resulting-main CI/Studio/package/security outcomes and source preservation. Report skips, retries, quarantines and superseded/cancelled evidence distinctly.

The repeatable witness is `Studio/tests/live/workflow-organization-controls.spec.ts`. It requires
an owned primary with a published, foldered, tagged workflow with a literal Text activity such as Write Line (the normal library journey or an
online SQLite backup can prepare this), an independent secondary and configured `authoring` /
`secondary` Studio profiles. Use HTTPS or `app.studio.localhost`, `api-a.studio.localhost` and
`api-b.studio.localhost` so the browser provides secure-context APIs while the hosts remain local.
Configure exact Studio origins in API CORS/return-URL policy. Studio's browser authority uses
the primary subdomain; `Studio:BackendServerBaseUrl` can use its loopback URL.

```sh
cd Studio
ELSA_STUDIO_URL=http://app.studio.localhost:56950 \
ELSA_ORGANIZATION_CONTROLS_LIVE=1 \
ELSA_ORGANIZATION_API=http://api-a.studio.localhost:56951/default/ \
ELSA_ORGANIZATION_PRIMARY_SHELLS=/absolute/owned/authoring/shells.json \
ELSA_ORGANIZATION_SECONDARY_SHELLS=/absolute/owned/secondary/shells.json \
ELSA_ORGANIZATION_PROOF=/absolute/owned/retained-library.json \
ELSA_ORGANIZATION_PHASE=disable \
pnpm exec playwright test --config tests/live/playwright.live.config.ts workflow-organization-controls.spec.ts
```

Restart only those owned hosts without changing their configuration/data, then repeat with
`ELSA_ORGANIZATION_PHASE=restore` and the same proof file. The first phase performs reviewed false
Apply, cancellation, exact configuration isolation, stale-operation refusal, ordinary metadata
editing, publication and published-artifact execution. The second verifies configured false after
restart, reviewed true Apply, exact organization readback and restored optional columns. No bearer
is written into the proof file or attachments.
