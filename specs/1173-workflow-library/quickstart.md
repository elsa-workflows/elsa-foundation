# Qualification journeys

Use an owned disposable normal EF-backed Workbench/Studio with isolated database/ports. Follow `tools/studio/README.md` and `Studio/docs/custom-hosting.md` for package/host setup.

1. Discover page/folder/organization/tag/view/favorite links; create Finance / Receivables and a workflow there.
2. Record authored draft/version/publication identities; rename/move folders and bulk-move definitions; verify cycle/non-empty/refused mutations preserve context.
3. Assign marker/controlled tags, exercise stale edits, retirement and mixed bulk outcomes.
4. Combine folder/subtree/search/lifecycle/tag filters, page/count and URL/Back behavior.
5. Save private/shared views and favorite workflows; use distinct subjects/tenants to verify sharing never grants visibility.
6. Soft-delete/restore and reconcile a source-owned definition; restart the host and verify metadata plus unchanged authored/publication identities.
7. Run `Studio/tests/live/module-styles.spec.ts` against the real host. Verify all automatically loaded Workflows assets stay within 402,000 bytes, canvas CSS loads only after opening the editor, and a missing entry-style contract makes the guard fail under the legacy eager fallback.
8. Run the 10,000-definition deterministic fixture, supported-provider and migration suites, affected whole frontend/API suites, architecture/maps and package consumers.

Suite roots: existing Workflow Design API and EF Tests/ProviderTests projects, Workflows Client `src/__tests__/`, and `Studio/tests/{browser,live}/`. Exact commands/receipts go on #3500/the PR when executed; this guide claims no pass.
