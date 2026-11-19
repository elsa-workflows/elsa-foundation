# Publisher fixture and recovery-tool CI repair

Source parent `c5a1e4718fce09168f7e90a860388805c4fe3f32`; the [machine receipt](broadcast-ci-fixture-repair.json)
binds 36 changed source/lock artifacts. This increment fixes concrete CI failures following publisher
activation and the first recovery probe. **It does not add upgraded-host, Studio or production acceptance.**

[Architecture guards](https://github.com/elsa-workflows/elsa-foundation/actions/runs/40457797447/job/122035152218)
rejected the new reader's committed lock file because its project was absent from the Server solution.
The exact guard failed locally. The existing checked-reader project is now included in the solution;
no guard exception or lockfile omission was added.

[PR shard 3](https://github.com/elsa-workflows/elsa-foundation/actions/runs/40457797447/job/122035152104)
found four BPMN message fixtures and the Behavioral PublishEvent drive without the schema qualification
required by the publisher. The five-case local BPMN run reproduced four refusals. An explicit native
component helper now composes the real `SchemaDormancyCheck` over candidly constructed fresh-native
observations, a default persistence access context and the actual router. Only the message fixture,
PublishEvent drive and existing nine payload-routing cases opt in. `TryAdd` preserves caller services;
ordinary harness defaults and the shared BPMN/EF recovery fixture remain unchanged. Constructed
observations are not schema migration, fleet admission or host recovery evidence.

[PR shard 2](https://github.com/elsa-workflows/elsa-foundation/actions/runs/40457797447/job/122035152264)
showed Elsa 3 event deliveries stalling. Its actual TestServer host lacked the shared schema-readability
service. A new public availability regression failed on the missing check before the repair. The host
now composes the real `AddEfSchemaReadability` boundary used by Workbench; the regression verifies the
resolved shell's finalized outbox availability. No production publisher guard or schema policy changed.

| Whole affected project | Passed | Failed | Skipped |
|---|---:|---:|---:|
| BPMN | 297 | 0 | 0 |
| Behavioral | 215 | 0 | 0 |
| Activities Runtime | 720 | 0 | 0 |
| Elsa 3 import integration | 51 | 0 | 0 |
| Architecture | 590 | 0 | 0 |
| **Total** | **1,873** | **0** | **0** |

The focused repair run passed 195 cases, including existing missing/unobserved schema refusal controls.
One missing test import was fixed before successful execution. The first whole architecture run had
569 passes and 21 missing isolated Debug restore-asset failures; the prescribed Debug/Release restore
prepared those assets, then the complete 590-case suite passed. These setup failures were not treated
as passing checks. The one new test raises import integration from 50 to 51 cases.

Default locked restore of all **340 Server projects**, the prescribed dual-configuration restore and
map checks pass. The actual NuGet graph supplies required test-support/readability edges and new
closure nodes in 27 lockfiles; all **1,720 existing external records** remain exactly unchanged. Existing
warnings remain. The frozen authoring fingerprint is unchanged. Three independent component source
and receipt audits are clean; this is not a full-PR round.

The previous actual post-cohort and partial callback/rearm receipts retain their own source/build
bindings. Remaining T051 cases, T045 semantics/native-composite proof, actual historical upgraded-host
recovery, final current Studio, deletion/guards, full-PR review, green exact-head required checks,
protected merge/resulting-main verification and separate ADR 0063 acceptance remain open. The pushed
repair needs fresh CI; the three public-source-digest GitGuardian incidents still need legitimate
resolution. No checks, protection or human holds are bypassed. #2898/#2907/program production
acceptance remains open; Orleans and the task-runner follow-up remain deferred.
