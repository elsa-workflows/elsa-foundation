# In-progress validation checkpoint

This is incomplete work on #3359, not a delivery or completion receipt.

## Owner-authorized resumption, 27 October

The owner explicitly replied "Resume #3359 with this exception" to this leaf's three-failed-check request;
receipt3359#issuecomment-6429394630 records that exception. Fresh exclusive claim6430037530 resumes it after
#3361 delivered through normal Code Review approval/merge, both main CI runs and fresh main packages/consumer
qualification. Verified main9e96273c8041476fe8bfb102c84019359c617b84 merged without conflict into saved4df4d1329
at53094ea0b9ae4845de6bb333237f873bf21b6a7f. Standard browser source now belongs to Hosting; no old Web source
path remains in active test/tool references. This is one implementation leaf, with no implementation delegation.

The two prior failures were stale fixture expectations. Confirmed Nuplane execution verifies root/epoch/policy,
recovers graph-use records under ownership, then freshly inspects/replans. The zero-retention case now asserts
Preview Unknown/read-only, then execution Known/Completed, an actual Deleted outcome, removal of the obsolete
directory, and successful active payload reads. The null-retention case asserts Unknown preview followed by
Known/Completed with no delete/attempt and both original directories and payload bytes intact. Root/epoch/policy
binding, genuine unknown/refusal and retained-generation protection tests remain unchanged. No local planner,
production execution behavior, enrollment shortcut or upstream code was introduced.

Whole Studio Release project passed566/566,0skipped,80s (`studio3359-owner-resume.trx`;
`/tmp/studio3359-owner-resume.log`). Discovery559 expands the existing runtime eight-row module-manifest theory
by7 cases; no discovered test was omitted. Machine load was2.35 on8cores before the check. Research/contract
wording now distinguishes descriptive Preview from fresh owned execution. This clears the diagnosed failure,
not the remaining module/feed/UI/package/review/main acceptance work listed below.

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
