# In-progress validation checkpoint

This is incomplete work on #3359, not a delivery or completion receipt.

## Passed evidence

- Protected feature-manifest adapter: whole Studio project 551 passed, 0 failed/skipped
  (`studio3359-protected-metadata.trx`). Real package fixtures cover root/build manifests, shared mapping and
  secret defaults, oversized/invalid/missing metadata, ordinary-store refusal, linked metadata and native
  reconciliation overlap. The default unscoped manifest contributor is replaced only in the Studio addon.
- Root manifest-provider adapter: whole Studio project 554 passed, 0 failed/skipped
  (`studio3359-root-manifests-fixed.trx`). Root callers use the existing interface and the same tracked selected-shell
  acquisition as discovery; awaited reads survive generation replacement and release on cancellation.
- Feature editor: all 72 module unit tests passed. Diagnostic manifest-error rows are excluded from apply;
  unknown configured features remain in the full desired state.
- Studio `pnpm typecheck`, `pnpm lint`, `pnpm build` passed. Lint reported 56 warnings and no errors. Runtime UI
  qualification and the remaining maintenance UI changes are still outstanding.

## Latest whole-project check

`studio3359-maintenance-qualified.trx`: 564 passed, 2 failed, 0 skipped (566 total).

The passing public-addon HTTP/native fixture proves default null retention retains all, confirmed zero-retention
execution physically deletes an obsolete install, and a real retained First shell scope continues serving after
Second replaces it. The old generation remains protected by ProtectedLiveUse. Root identity/epoch, policy/label
binding, missing confirmation, legacy-policy selection, overlaid source refusal and invalid retention checks also
passed. Maintenance returns the upstream result and maps upstream argument validation to HTTP 400.

The two failing tests are `Stale_content_only_graph_uses_refuse_deletion_instead_of_claiming_known_protection` and
`Null_retention_does_not_override_unknown_protection_to_authorize_execution`. Both assert Status=Refused after an
Unknown preview; actual execution returns Completed. Preview is an observation, while Execute replans under
ownership. These assertions must be corrected against the fresh execution facts and actual filesystem outcomes;
do not force the implementation to preserve an earlier preview or replace the upstream planner. No correction
has been applied after the third consecutive check failure.

The preceding maintenance attempts were a test compilation failure (calling Reload through IConfiguration,
fixed by using IConfigurationRoot) and a run with assertion failures that subsequently stalled. A managed stack
snapshot showed console-write contention with the console-stream stress harness. Fixture hosts now StopAsync
before DisposeAsync and clear unused default logging providers. The corrected full run completed in 1m21s.

The organization three-failure stop rule has been reached. Work is preserved locally pending an owner exception
for #3359. The earlier owner exception was explicitly scoped to #3356. No branch has been pushed and no PR opened.

Remaining work includes module registry/upload/delete/feed extraction, all remaining admitted nuspec/registry
reads, Web adoption and obsolete-code removal, maintenance UI/runtime qualification, causal controls, package and
CI qualification, independent/external review and resulting-main verification. The program goal remains active.
