# In-progress validation checkpoint

This is incomplete work on #3359, not a delivery or completion receipt.

## Current checkpoint — Web adoption and maintenance UI qualified locally

The owner granted a new #3359 exception after the Web adoption three-attempt stop, recorded at
3359#issuecomment-6431700996. Exclusive resumption claim6431701224 owns this work. The first resumed attempt
fixed the registration-timing mismatch by preparing the owned management file before addon composition;
package discovery/replacement worked, but the fixture still filtered reload logs on the retired Web category.
Updating that filter to the addon category produced24/24 targeted passes. No production loader was changed.
The fixture now asserts the registered feed path, actual active package/version and runtime First descriptor.

Whole Studio after adoption passed564/564,0skipped; whole Modularity passed398/398,0skipped. Pre-adoption595
minus the31 retired private helper cases and2 fake package-cache cases, plus1 public boolean/object blueprint
case, equals564. Native package cache/range/revalidation and public upload/path/store/catalog coverage remain;
the framework owns runtime-empty-dependency precedence and manifest dependency projection. Discovery557 plus
7 expanded rows of the existing runtime8-row module-manifest theory equals564, with none omitted.

A new public fault-provider test proved feature/feed/retention writes each committed a valid file before an
unhandled reload exception. The controlled pre-fix run failed3/3 on that fact after checking committed bytes.
The adapter now distinguishes atomic save from reload failure and returns503 with an explicit saved-state
message without provider/feature secrets. Targeted corrected3/3 and whole Studio567/567,0skipped,86s passed
(studio3359-post-commit-{before,fixed}.trx; studio3359-web-and-reload-whole.trx).

Maintenance UI now uses explicit nullable inactive-version retention, default Preview, complete native reports,
separate confirmation with observed root/epoch/policy and truthful execution/partial physical outcomes. Plans
clear after host/policy changes; cancelled or detached confirmation cannot execute. Older Server count/age data
remains visible and read-only; no synthetic3-version/30-day/automatic policy is created. New UI11 plus existing
module-management31 tests passed42/42. Actual Chromium3/3 passed twice, including cancel, partial failure with
observed deletion, saved-policy invalidation and read-only Server. The preview screenshot was visually inspected;
Reconcile was moved outside the report's grid to avoid stretching. Configured workspace typecheck/lint/build pass;
lint has56warnings,0errors. Full workspace test count is recorded below when final artifacts are qualified.

An extra broad standalone Hosting Client tsc check (outside configured workspace typecheck) failed with91
diagnostics in unrelated existing tests/vendor declarations. A fresh owned git-archive baseline39c351228 with
the same dependency links produced byte-identical normalized diagnostics, also91. No changed maintenance file
has a diagnostic. This extra failed check is not represented as green; see
/tmp/studio3359-host-tsc-baseline-comparison.json. Canonical workspace typecheck passed after browser additions.

Both Server and Studio locked restores pass. Whole architecture590/590 passes after the required graph restore.
Package/inventory/isolated-consumer/Web smoke, final causal controls, review, CI and resulting-main delivery
remain open. Nothing here marks #3359 or Program #3340 complete.

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

The public ownership regression proved a derived JSON provider could replace the managed source, advertise the
same physical path, and supply synthetic retention99 while a save wrongly returned200 and wrote the file. The
single-case pre-fix check failed as intended (expected409, observed200). ConfigurationOwnership now admits only
the standard JsonConfigurationProvider/JsonConfigurationSource/PhysicalFileProvider combination; unsupported
custom implementations remain readable but cannot claim writable ownership from path metadata. Whole Studio
Release project then passed567/567,0skipped,77s (`studio3359-provider-provenance-fixed.trx`). The regression also
asserts the original file bytes are unchanged after refusal. Existing optional missing-file and feature-source
behavior remains covered by that whole run. No public API or caller's configuration source was replaced.

The addon now maps registry/upload/drop-file deletion/reconciliation/feed CRUD alongside its existing native
maintenance and shared feature-management endpoints. Registry metadata uses the existing admitted reader for
root/build manifests and NuSpecs, with the1MiB preflight, protected reads, bounded XML parsing and per-package
diagnostics; there is no physical-path byte fallback. Root manifest queries reuse Hosting's tracked selected-shell
adapter. Uploads use the frozen effective directory selection; pending feed edits do not redirect them. Conditional
feed writes share the configuration lease and preserve unrelated JSON/unknown directory-role fields.

New public endpoint tests passed27/27: registry without Web identity, valid/invalid/malformed upload and staged-file
cleanup, pending-directory upload/delete, concurrent feed/retention writes, duplicate/not-found no-write behavior,
overlaid source refusal, callback cache-only upload without desired discovery, source-only selection refusal,
anonymous denial on every administration route, and native/ordinary/oversized/linked NuSpec metadata. The initial
targeted attempt located an uncaught malformed multipart error; it now returns400. A second attempt used JSON
against the multipart-only route in its auth fixture; correcting the fixture's request format produced the27-case
pass. These failed attempts are not counted as passes. The initial addon compile also required the correct public
Nuplane.Abstractions.ActivePackage import; the corrected addon build passed with0warnings/errors.

A physical deployed-file provider could bypass ordinary-store admission through a directory symlink alias.
The public regression reproduced200/payload instead of503. Physical deployed paths now resolve linked ancestors
before comparison to the configured install root, with unresolved identity routed through protected admission;
the regression then passed. Host-owned nonphysical static providers retain their existing trusted immutable
content boundary. This classifies deployed content only; native content identity/traversal/read authority still
belongs to Nuplane. Whole Studio project subsequently passed595/595,0skipped,85s
(`studio3359-module-and-alias-whole.trx`); discovery588 expands the same8-row runtime theory by7, none omitted.

Web still uses the old private adapters at this checkpoint. Their adoption/removal, old helper-test retirement,
maintenance UI, final package/CI/review/main gates remain incomplete; no duplicate source is accepted as delivery.

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

## Original blocked whole-project check (historical)

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


## Web adoption checkpoint — stopped on a new three-attempt sequence

Web now composes AddElsaStudioNuplane/MapElsaStudioNuplane, keeps only its distribution module roster,
and references the addon instead of direct Nuplane packages. The management file is authoritative for the
Nuplane setup, with explicit fresh-template retain-all policy; duplicated appsettings setup and copied shared
contract identities were removed. Four obsolete server adapters and their private helper tests were removed.
Public HTTP/native tests replace those helpers; host caching tests retain deployed-file cases while native
PackageAssetTests owns package cache/range/revalidation coverage. Public selected-blueprint boolean/object
configuration coverage and the round-trip no-temp-file assertion were added.

This adoption is unqualified. Three consecutive targeted-check attempts failed:

1. Compilation found StudioAssetCachingTests still calling removed MapNuplaneStaticWebAssets. Its fake package
   reader was retired in favor of the existing real native-store public test; host caching cases remain.
2. 23 passed, 1 failed: lifecycle fixture mixed imperative AddShell with Hosting's configuration blueprint provider.
   The fixture was migrated to configuration features and a configured reload-failure switch.
3. 23 passed, 1 failed: lifecycle fixture's first activated generation has no registered string service from First.dll.
   The failing line is StudioPackageLifecycleTests' first GetRequiredService<string>(); no assertions were relaxed.

Log: /tmp/studio3359-web-adoption-targeted.log; TRX: studio3359-web-adoption-targeted.trx.
The likely fixture boundary is feed registration timing: its feed-directory override is appended after addon
composition, while configured directory feeds are registered during composition. This is not yet proven or fixed.
No fourth attempt or further implementation was made. The organization three-failed-check stop rule requires a
new owner exception for this sequence; the previously granted #3359 exception covered the saved recovery tests.
The prior 595/595 whole-project pass applies only to pre-adoption commit4a4fc68, not this checkpoint.

Still required: qualify Web/lifecycle adoption and shared Modularity replacement suites, maintenance UI, truthful
post-save reload errors, package/architecture/maps/CI gates, independent review and delivery/main verification.
No branch pushed, PR opened or leaf marked Done. Program #3340 remains unfinished.


## Draft review round1 and corrected qualification

Exact diff9e96273c8041476fe8bfb102c84019359c617b84..2dd78e8bef28b1ae21e28e52314f8dc3369204dc
was reviewed independently for correctness, standards and acceptance. Two P2 findings survived: management
writes rejected comments/trailing commas accepted by AddJsonFile, and activation guards saw stale file-only
shell Configuration when a provider overlaid it. The suspected undeclared-secret leak was discarded after
inspection of the existing shared masker's fail-closed undeclared-key behavior.

Public regressions reproduced3/3 failures:2 management mutations threw JsonException; a guard rejecting effective
limit99 wrongly returned200 against the file's limit7. The corrected targeted4/4 passes, and the entire Studio
project passes570/570,0skipped,101s (studio3359-review1-whole.trx). The guard now sees effective IConfiguration
values separately from feature secrets; leaves follow IConfiguration string semantics. Managed reads/writes
wrap IO/JSON failures in StudioHostingException. Failed shell replacement now logs the underlying failure on the
trusted server while retaining the generic saved-state response. A fixture compilation typo before the causal
run was corrected; compilation refusal was not counted as a behavioral red.

The author qualification also found the new package missing from the inventory. Fixed ind3f718a4d; exact21
archives and verifier self-tests pass. A verifier invocation from Studio without --root failed for an incorrect
working directory, not the artifact; the corrected repository-root invocation passes. The owned Release producer
at2dd78e8be built with0warnings/errors and passed567/567; revised candidate proof follows the review fixes.
Full workspace frontend unit3654 passed across19 executed test scripts, plus typecheck/lint/build and Chromium3.
Mandatory/revision/preflight mutation controls each produce a behavioral red and restore green with byte-exact
source restoration; initial constant-false harness mutation failed compilation and was replaced by a compilable
control. Existing managed-source/provider-alias regressions also retain their red/green evidence.

The first isolated candidate restores/locked-restores/publishes with only the Nuplane addon referenced,
matching actual candidate DLL hashes and denying frontend tooling. Its first feature apply returned503; the
response body was not retained by that initial harness. A fresh diagnostic apply and a full sequence replay
both returned200. This original failure is retained as unresolved until the revised candidate/journey qualifies;
no first-attempt success or complete consumer acceptance is claimed. All owned processes stop in finally.
