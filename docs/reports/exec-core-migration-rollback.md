# Execution-core S8: native and direct-BPMN migration and rollback

Point-in-time statement for execution-core milestone slice 9 / goal §E. Docs only. No production or test change. Native and BPMN semantics are not merged; a fact proved on one model is not inferred for the other.

**Pinned main SHA:** `a9771131c37650d518587805abadb6d2b77d18a3`  
**Commit:** `[Claude] A poison record is written under the run's ownership lease (#4063)` (2026-11-11)  
**Required CI on this SHA:** [run 42967904085](https://github.com/elsa-workflows/elsa-foundation/actions/runs/42967904085) **success** (`Build & test`, `Generated maps fresh`, `Full tier`).  
**Inventory baseline:** `60d284932739871166198741603a01103f87cee3` (R3 dependency report).  
**Method:** re-read publish/store/migration/admission source on the pin; `git diff` against the inventory SHA; ApiCompat of current `Runtime.Core` / `Bpmn.Core` against last-published nupkgs from `origin/publish-state`. **No test re-run on this pin.** Last counted hosted/unit runs stay bound to their original SHAs and are labelled HISTORICAL.

This statement does not complete Program [#2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892), [#2905](https://github.com/elsa-workflows/elsa-foundation/issues/2905), or [#2907](https://github.com/elsa-workflows/elsa-foundation/issues/2907). It does not replace the operator-facing note in [native-bpmn-migration-and-rollback.md](native-bpmn-migration-and-rollback.md) (#3988); that note remains bound to `c1f35d2dc` / `9812e60f54`.

**Status vocabulary:** **PROVED** (source on this pin, with file:line); **HISTORICAL** (an older counted run, not re-executed here); **UNPROVED** (searched; no current-pin proof); **OPEN** (named gap, not silently deferred).

---

## 1. Stores and tables written by publish

Re-verified on `a9771131c` against the `60d28493` inventory. **Drift on the publish write sequence and on the named tables: none.** Blob identity of `PublishWorkflowRequestHandler.cs`, `PublicationActivator.cs`, `BpmnModelRuntime.cs`, `BpmnAuthoredDirectPublisher.cs`, `EfWorkflowExecutableStore.cs`, `RuntimeArtifactEfModule.cs`, and `PublishingLedgerEfModule.cs` matches `60d28493`. `WorkflowPreparationEnrollmentTransaction` remains **absent** on this pin (zero matches).

The two pipelines stay different: native uses unfenced `SaveAsync` then a publication journal and activation slot; direct-BPMN uses `SaveModelAsync` then fenced `SaveWithRootWriteLeasesAsync`. They are not equivalent and are not consolidated here.

### Native publish (`PublishWorkflowRequestHandler`)

Entry: `POST /publishing/workflows/{versionId}/publish` → `Essentials/src/Workflows/Publishing/Elsa.Workflows.Publishing/Handlers/PublishWorkflowRequestHandler.cs`.

| Store / table | When on this pin | Role vs GC |
|---|---|---|
| Design version / layout | read only | not written |
| `elsa_runtime_workflow_executable` + `elsa_runtime_workflow_executable_coordination` (`RuntimeArtifactEfModule`) | first durable write: `executableStore.SaveAsync` at handler **line 131**; EF `EfWorkflowExecutableStore.SaveAsync` → `SaveBatchAsync` | GC candidate until a live root exists |
| `elsa_runtime_executable_activity_template` (+ hash claim) | fence staging on save | retained if referenced |
| `elsa_publication_records` (`PublishingLedgerEfModule`) | `publicationStore.SaveAsync(candidate)` at `PublicationActivator.cs:51` **before** activation | **not a GC root** |
| `elsa_runtime_workflow_executable_root_write_lease` | during `WorkflowActivationCoordinator.ActivateAsync` (`ExecuteAsync` around the sequence) | blocks deletion while live |
| `elsa_runtime_workflow_executable_source_reference` | `sourceReferenceStore.SaveAsync` at `WorkflowActivationCoordinator.cs:493` (**unfenced**; lease held around the sequence, not the BPMN fenced writer) | GC root; `Scope: Published` |
| `elsa_runtime_workflow_activation_slot` (`RuntimeActivationSlotEfModule`) | activation switch after source mint | serving authority; not listed in GC `ListRuntimeRootIdsAsync` |
| `elsa_runtime_workflow_trigger_binding` + `elsa_runtime_trigger_binding_projection_state` (`RuntimeTriggerBindingEfModule`) | projection preparation after source mint | serving projections |

Native TestRun (`StartWorkflowTestRunRequestHandler`) is **not** canonical publish: `SaveAsync` then an expiring `TestRun` source reference. Same store family, different scope.

### Direct-BPMN publish (`BpmnModelRuntime.PublishCoreAsync`)

Entries: `POST interchange/bpmn/versions/{versionId}/publish-direct` → `BpmnAuthoredDirectPublisher.PublishAsync` (`Essentials/src/Activities/Bpmn/Interchange/Services/BpmnAuthoredDirectPublisher.cs:24–66`); and `POST bpmn/artifacts` → `IBpmnModelRuntime.PublishAsync`. Neither calls `PublishWorkflow` / `PublicationActivator` / activation slots.

| Store / table | Authored `publish-direct` | API `bpmn/artifacts` | Role vs GC |
|---|---|---|---|
| Design workflow/activity/layout | read | unused | not written |
| `elsa_runtime_workflow_executable` (`IsModelMaterial`) | `SaveModelAsync` at `BpmnModelRuntime.cs:96`; EF `EfWorkflowExecutableStore.ModelMaterial.cs:39–71` | same | staged material |
| `elsa_runtime_workflow_executable_coordination` | with save | with save | incarnation/lease coordination |
| `elsa_runtime_workflow_executable_root_write_lease` | during fenced save | during fenced save | deletion fence |
| `elsa_runtime_workflow_executable_source_reference` (`SourceKind=BpmnModel`, Published) | `ExecuteFencedAsync` + `SaveWithRootWriteLeasesAsync` at `BpmnModelRuntime.cs:102–103` | same | GC root + catalog |
| `elsa_publication_records` | **not written** | **not written** | n/a |
| activation slots | **not written** | **not written** | n/a |
| native worker executable/source | read/validate live Published pins | same | independent native publication |

Catalog listing `GET bpmn/publications` reads Published source references with `SourceKind == "BpmnModel"`, not `elsa_publication_records`.

### Pre-publication window (unchanged)

Both paths persist content-addressed material **before** the first Published source reference. Protection of that gap is policy (`ArtifactCreationGracePeriod` default 5 minutes, `RootWriteLeaseDuration` default 1 minute, optional GC pump), not a durable non-runnable root. Spec 1220 / [#3951](https://github.com/elsa-workflows/elsa-foundation/issues/3951) enrollment is still absent on this pin. See §6.

---

## 2. Additive-schema check: Runtime EF and Bpmn migrations since `60d28493`

**Every Runtime EF Core migration landed since `60d28493`:** none.  
**Every Bpmn migration landed since `60d28493`:** none. BPMN has **no** EF migration tree; it uses Runtime tables.

`git diff --name-only --diff-filter=A 60d28493..a9771131c` over `Essentials/src/Workflows/Runtime/Persistence/EntityFrameworkCore/Migrations/**` and `Essentials/src/Activities/Bpmn/**/Migrations/**` is empty. The Runtime Sqlite/SqlServer/PostgreSql/MySql snapshots, including `RuntimeSqliteDbContextModelSnapshot.cs`, are **byte-identical** to `60d28493`. Publishing ledger migrations (`elsa_publication_records`) also added none.

| Migration | PR / SHA | Additive? | Effect on native hosts | Effect on BPMN hosts |
|---|---|---|---|---|
| *(none)* | — | — | no schema change since inventory | no schema change since inventory |

Last Runtime Sqlite migration **already present** on the inventory SHA (not landed since; listed so the chain is visible):

| Name | Providers | Present on `60d28493` |
|---|---|---|
| `20261017151421_Initial` | Sqlite / SqlServer / PostgreSql / MySql | yes |
| `20261028210825_ModelExecutionState` | same | yes |
| `20261028214345_ModelMaterialKind` | same | yes |
| `20261102222549_UserTasks` | same | yes |
| `20261103045250_OutboxClaimDeliveredAt` | same | yes |

### Named PRs that landed without a Runtime/Bpmn schema migration

| PR | Merge SHA | What landed | Schema verdict |
|---|---|---|---|
| [#4063](https://github.com/elsa-workflows/elsa-foundation/pull/4063) poison-record lease | `a9771131c37650d518587805abadb6d2b77d18a3` | `EfWorkflowSchedulerPoisonStore` fence/lease behaviour over existing `elsa_runtime_scheduler_poison` (`RuntimeSchedulerPoisonEfModule`, table created in `Initial`) | **no new migration**; poison entity snapshot columns unchanged vs `60d28493` |
| [#4054](https://github.com/elsa-workflows/elsa-foundation/pull/4054) drain keeps the state it read | `3e4b7f2cb8dbea4a3444af5466c3283c7985f797` | coalescing/drain snapshot in Runtime services | **no migration** |
| [#3988](https://github.com/elsa-workflows/elsa-foundation/pull/3988) interrupted-publish GC + recheck | `fff9fbf2ce50743226b5390ee89d26b16cc85630` | tests + [native-bpmn-migration-and-rollback.md](native-bpmn-migration-and-rollback.md) | **no migration** |

[#3951](https://github.com/elsa-workflows/elsa-foundation/pull/3951) (preparation retention / R3) has **not** landed. Its enrollment type is still draft-branch only.

Out of this list (not Runtime, not Bpmn): Weaver EF snapshot files under `Extensions/Weaver/.../Migrations/Weaver/{PostgreSql,SqlServer,Sqlite}/` appear in `60d28493..a9771131c`. They are not Runtime or Bpmn schema and are not claimed here.

---

## 3. ApiCompat vs last published `Runtime.Core` and `Bpmn.Core`

**Ran.** No breaking changes in either direction.

Last published record (`origin/publish-state`, commit `49df8302a2daabc0d186dcce16c0cdaa0fa43f2d`, `last_publish_commit` = this pin):

| Package | Version | Published from |
|---|---|---|
| `Elsa.Workflows.Runtime.Core` | `4.0.81-preview` | `a9771131c37650d518587805abadb6d2b77d18a3` (this pin) |
| `Elsa.Activities.Bpmn.Core` | `4.0.8-preview` | `3d21d7075c20fa181bf7e051b42417c1215732c7` (`Recover main after unintended stash while preserving #3998` #4000) |

`Elsa.Activities.Bpmn.Core` source and `PublicAPI.*.txt` vs `3d21d7075` on this pin: **empty diff**.

**How the baselines resolved**

- nuget.org v3-flatcontainer `elsa.workflows.runtime.core` and `elsa.activities.bpmn.core` index: **HTTP 404** (the known preview-feed issue). Observed on this investigation.
- Feedz Elsa 4 nupkgs matching `publish-state` were obtained earlier in the same investigation; each nupkg `nuspec` `<repository commit>` equals the table above. SHA-256 of the files used: Runtime `1915ed59b71ffd2a3f540462684e1a29401d26ecccad8f8ea709cb7035e38d5c`; Bpmn `21066be3d2614229ca1a586ad581ed2dff91c7d8dd32fb85fe6bc7e800b48433`. A later GET of the same Feedz flatcontainer URLs also returned 404; ApiCompat used the already-fetched nupkgs, not a live nuget.org restore.
- No ApiCompat project, suppression file, or CI job was added.

**Tool:** `Microsoft.DotNet.ApiCompat.Tool` 10.0.403. Right side: Release build of `Elsa.Workflows.Runtime.Core.csproj` and `Elsa.Activities.Bpmn.Core.csproj` at `a9771131c` (build succeeded, 0 errors).

| Comparison | Left | Right | Result |
|---|---|---|---|
| Runtime.Core forward | nupkg `4.0.81-preview` `net10.0` DLL | built `Elsa.Workflows.Runtime.Core.dll` | `APICompat ran successfully without finding any breaking changes.` |
| Runtime.Core reversed | built DLL | nupkg DLL | same (public API identical, as expected: nupkg is this pin) |
| Bpmn.Core forward | nupkg `4.0.8-preview` `net10.0` DLL | built `Elsa.Activities.Bpmn.Core.dll` | no breaking changes |
| Bpmn.Core reversed | built DLL | nupkg DLL | no breaking changes |

`PublicAPI.Unshipped.txt` for Runtime.Core **did** grow vs `60d28493` (poison-fence additions in #4063). Those members are in the last published `4.0.81-preview`. They are additions relative to the inventory SHA, not removals relative to the published baseline.

---

## 4. Frozen native families and pre-claim BPMN captures (FR-016 / SC-004)

Spec 1184:

- **FR-016:** existing native/embedded authoring, exported XML/BPMNDI, saved state, immutable selections and composite outcomes remain compatible; persisted-writer changes follow actual prior-writer checkpoints.
- **SC-004:** both actual frozen saved-Studio families complete their compatibility journey; original authoring/checkpoint assets stay unchanged.

### Source on this pin (PROVED as identity, not as a fresh run)

All of the following blobs are **identical** to `60d28493` (git blob id match). They were not edited on the pin.

| Family | Test class | File |
|---|---|---|
| Legacy broadcast | `LegacyBroadcastUpgradeTests` | `Essentials/tests/Activities/Bpmn/Hosting/Tests/LegacyBroadcastUpgradeTests.cs` |
| Legacy called-ack | `LegacyCalledAcknowledgmentUpgradeTests` | `.../LegacyCalledAcknowledgmentUpgradeTests.cs` |
| Legacy controls / compensation | `LegacyControlsUpgradeTests` | `.../LegacyControlsUpgradeTests.cs` |
| Legacy multi-instance | `LegacyMultiInstanceUpgradeTests` | `.../LegacyMultiInstanceUpgradeTests.cs` |
| Legacy native parent | `LegacyNativeParentUpgradeTests` | `.../LegacyNativeParentUpgradeTests.cs` |
| Legacy partial fork | `LegacyPartialForkUpgradeTests` | `.../LegacyPartialForkUpgradeTests.cs` |
| Pre-claim human upgrades | `DirectHumanPreClaimUpgradeTests` | `.../DirectHumanPreClaimUpgradeTests.cs` |

Pre-claim pins: `specs/1184-direct-bpmn-studio/evidence/preclaim/README.md` (production capture `1d77ed131`; upgrade controls at `bc5f464af` and inspection-authority drop at `c10557709`).

### Last counted runs (HISTORICAL — not re-executed on `a9771131c`)

| Claim | Last counted evidence | Status on this pin |
|---|---|---|
| FR-016 `Legacy*` hosting | [#3988](https://github.com/elsa-workflows/elsa-foundation/pull/3988) `fff9fbf2c`; local tree `9812e60f54` / parent `c1f35d2dc`; **14/14** `FullyQualifiedName~Legacy` in `Elsa.Activities.Bpmn.Hosting.Tests` ([compatibility README PR2 recheck](../../specs/1184-direct-bpmn-studio/evidence/compatibility/README.md#pr2-recheck-7-november-2026)) | **HISTORICAL**. Tests exist and are unchanged. **UNPROVED as a fresh run on `a9771131c`.** |
| SC-004 frozen saved-Studio families | October archive / claim-writer `a96f774eb` (actual browser + `verify_studio_upgrade.py`). PR2: mechanical hash of 90 on-disk non-studio baseline assets OK; 14 `studio/*` assets absent; visual journeys **NOT RUN** | **HISTORICAL / UNPROVED** on this pin. Do not present October screenshots as a fresh run. |
| SC-004 pre-claim captures | `DirectHumanPreClaimUpgradeTests` 4/4 at `c10557709`; files unchanged | **HISTORICAL**. Source correspondence PROVED; not re-run here. |

No frozen-family fixture owned by this statement was edited. No evidence that a frozen family **changed** on this pin (that would have been a stop).

---

## 5. Rollback = admission off, readers/workers kept (FR-015)

Spec 1184 FR-015: inspection/recovery of accepted work remains usable in the qualified runtime-only / admission-off composition. Unavailable authoring is distinct from execution failure.

### Direct BPMN — PROVED on source (this pin); last hosted run HISTORICAL

**Switch:** `ActivitiesBpmnHostingFeature.AdmitNewExecutions` (`Essentials/src/Activities/Bpmn/Elsa.Activities.Bpmn/ActivitiesBpmnHostingFeature.cs:25`), default **false**. Composing hosting registers readers, workers, and `IBpmnModelRuntime` regardless of the flag (`ConfigureServices` lines 27–51; `BpmnModelHostingOptions` at line 48). JSON feature option `ActivitiesBpmnHosting.AdmitNewExecutions`. HTTP view: `GET /bpmn/admission` → `BpmnAdmissionView.AdmitNewExecutions` (`BpmnHostingApi.cs:159–164`).

**Enforcement:**

- New direct start refused: `BpmnDirectExecutionDriver.ValidateInputAsync` (`BpmnDirectExecutionDriver.cs:45–46`) throws `RuntimeSchedulerWorkUnavailableException("bpmn.admission-disabled", ...)` when `InputKind == "start"`, `State is null`, and admission is off.
- New `RunBpmn` embedding refused: `BpmnEmbeddingBoundary.PrepareAsync` (`BpmnEmbeddingBoundary.cs:38`) throws `NotSupportedException("New RunBpmn model invocation admission is disabled.")`.
- Core port default: `IBpmnModelRuntime.AdmitNewExecutions => false` (`IBpmnModelRuntime.cs:13`).

Those production files are blob-identical to `60d28493`.

**Tests that name the switch** (present on this pin; not re-run here):

| Test | File | What it shows |
|---|---|---|
| `Admission_off_keeps_claim_work_operable_and_interruptions_keep_their_first_winner` | `Essentials/tests/Activities/Bpmn/Hosting/Tests/BpmnHumanClaimUpgradeTests.cs` | real restart, `SetAdmissionAsync(false)`, `GET /bpmn/admission` is false, new start `503`, accepted claim work still operable |
| `Inspection_of_accepted_work_survives_provider_restart_and_admission_off_without_design` | `Essentials/tests/Activities/Bpmn/Tests/BpmnDirectInspectionTests.cs` | inspect after reopen with `admitNewExecutions: false` |
| `Accepted_first_input_stays_rooted_and_recovers_after_restart_with_new_admission_disabled` | `Essentials/tests/Activities/Bpmn/Tests/BpmnDirectExecutionTests.cs` | accepted input recovers with admission disabled |
| `A_claim_survives_host_restart_and_admission_off_without_reassignment` | `Essentials/tests/Activities/Bpmn/Tests/BpmnDirectExecutionTests.HumanClaims.cs` | claim identity kept |
| `Saved_authored_version_publishes_directly_and_recovers_its_human_wait_after_actual_restart` | `Essentials/tests/Activities/Bpmn/Hosting/Tests/DirectBpmnStudioPublicationTests.cs` | Runtime-only recovery after restart |
| `Partial_fork_recovers_and_joins_in_the_actual_host_without_design_storage` | `Essentials/tests/Activities/Bpmn/Hosting/Tests/RuntimeOnlyParallelBpmnTests.cs` | Design absent; FR-015 hosting pair in #3988 |

Last counted FR-015 hosting pair: #3988, **2/0/0**, tree `9812e60f54` — **HISTORICAL**.

Safe BPMN admission rollback on this evidence: turn `AdmitNewExecutions` off; keep the feature, stores, readers and workers; already accepted executions remain inspectable/completable. Do not equate authoring-unavailable with execution failure.

### Native Sequence / Flowchart — UNPROVED as a named admission flag

Searched `Essentials/src` for `AdmitNewExecutions` outside `Elsa.Activities.Bpmn*`: **no twin flag** for Sequence/Flowchart/native hosting. Native version rollback remains “publish a previous version through the existing publication slot/journal” ([operator note](native-bpmn-migration-and-rollback.md)); that is not FR-015 admission-off. This statement does **not** invent a native switch. Marked **UNPROVED**, not claimed.

---

## 6. Unsupported / open

The R3 preparation-store waiver (owner record on [#2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892), comment [c6707907446](https://github.com/elsa-workflows/elsa-foundation/issues/2892#issuecomment-6707907446)) **applies only to R3** (old recovery data in the preparation store / enrollment settlement). **There is no waiver for native or BPMN execution state.**

| Item | Status on `a9771131c` |
|---|---|
| **Pre-publication retention gap (S3)** | Native and direct-BPMN still save material before the Published source reference and do not enroll. EF material can be collected after grace if no live root exists. Goal §A requires an owner choice: (a) claim only post-publication execution-crash recovery and name the gap, or (b) run the interrupt-before/after-mint verification on both paths. **Owner decision pending.** No reviewed-candidate retention claim. |
| **G2.4 rows A2 / B2** | **OPEN.** No non-ETL business-error path. See [exec-core-g2-child-outcome-mapping.md](exec-core-g2-child-outcome-mapping.md): A2 (RunBpmn child business error) and B2 (callActivity → native-root child business error). Only first-party Completes-with-`BusinessError` activity is `RunEtl`; ETL [#3804](https://github.com/elsa-workflows/elsa-foundation/issues/3804)/[#3789](https://github.com/elsa-workflows/elsa-foundation/issues/3789) is out of this milestone. |
| **SC-005** | Unauthorized / cross-tenant disclosure and read-only/source-owned editing. Spec 1184 / [#3650](https://github.com/elsa-workflows/elsa-foundation/issues/3650) remain open. **Not proved on this pin.** HTTP-level non-admin half, with tenant and Studio halves explicitly deferred: [exec-core-sc005-authz.md](exec-core-sc005-authz.md). Security behaviour is never implicitly deferred. |
| **Narrow validated alteration** | Arbitrary alteration/live-instance migration stay out of scope (specs 1017/1006). The “narrowly validated alteration” in the #2892 BPMN journey has **no current-pin evidence** in this statement and **no owner deferral cited here**. **OPEN.** |
| **SQLite-only / one-host limits** | Interrupted-publish GC classification, FR-015 hosting, G2.4 hosted cells, and the #3988 operator note are SQLite Owned Workbench / one in-process host unless a cited test says otherwise. PostgreSQL, SQL Server, MySQL, multi-machine partition, clock skew, and Orleans are **not** qualified by this statement. |
| **Live model migration** | No supported rewrite of an in-flight native execution into a direct-BPMN execution, or the reverse. Accepted work recovers on the model that admitted it. |
| **Native ↔ BPMN publish consolidation** | Deliberately not done. Unfenced `SaveAsync` vs fenced `SaveWithRootWriteLeasesAsync` remains a documented difference. |
| **#3951 / spec 1220 enrollment** | Not on this pin. Landing it would still be a Runtime EF event for native/BPMN hosts even if they do not yet enroll. |
| **Default Workbench GC pump** | Not enabled in default `shells.json`. Tests often use zero grace. Do not treat default Workbench retention as the GC-on case. |
| **Physical external effects** | Remain at-least-once. |

---

## Findings / stops

None that block this docs-only statement:

- No Runtime or Bpmn migration since `60d28493` (so none non-additive).
- No frozen-family source change vs the inventory SHA.
- BPMN FR-015 switch exists and is named; native Sequence/Flowchart twin is UNPROVED rather than fabricated.
- ApiCompat against last published Core packages found no breaking changes.

Not claimed: a fresh `Legacy*` / Studio-family / FR-015 hosting re-run on `a9771131c`; G2.4 A2/B2; SC-005; pre-publication retention beyond grace; native admission-off.

## Out of scope (honoured)

No production or test change. [#4038](https://github.com/elsa-workflows/elsa-foundation/issues/4038), ETL [#3804](https://github.com/elsa-workflows/elsa-foundation/issues/3804)/[#3789](https://github.com/elsa-workflows/elsa-foundation/issues/3789), [#3650](https://github.com/elsa-workflows/elsa-foundation/issues/3650), [#3204](https://github.com/elsa-workflows/elsa-foundation/issues/3204) untouched. No issue closed. No ApiCompat infrastructure added. `origin/main` at investigation included a later unrelated commit `ecf080a7a` (#4080); this statement is pinned to the green SHA above, not that commit.
