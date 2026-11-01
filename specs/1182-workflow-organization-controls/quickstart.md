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
