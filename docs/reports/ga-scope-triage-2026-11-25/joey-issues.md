# GA scope triage: Joey's open issues

25 November 2026. This covers every open issue in `elsa-workflows/elsa-foundation` authored by `j03y-nxxbz`: 58 issues
across the three pages of the list. Each body was read, and comments were read where they changed the picture. Linked
PRs were checked for their state. The source was read at `main` `1c930f7e4d`. Nothing on GitHub was changed. No label,
comment, assignment or state was touched. This report is for Sipke to act on.

## What the verdicts rest on

- **The GA core is the clean-slate kernel** ([#4229](https://github.com/elsa-workflows/elsa-foundation/issues/4229)).
  The 24 November trim keeps the Actor Foundation, the Elsa engine (Native model, Flowchart), PostgreSQL and SQLite,
  K8 synchronous HTTP, K9 read models, K10 Studio and the rest of K11. It cuts the StateMachine, SQL Server and MySQL,
  the migration-plan runner, values by reference, and Park discard and retry selection
  ([trimmed scope](../../program-goals/clean-slate-kernel.md#trimmed-scope-owner-24-november-2026)).
- **The old core is frozen and then deleted.** On 19 November Sipke froze runtime feature work on the old core: only
  fixes, plus what keeps `main` usable until K13. K14 removes the old core. On 16 November he ruled that an old-core
  defect is fixed only if a release ships the old core; otherwise it closes as superseded. No release ships it.
  Host-neutral work continues: activities, Studio, the BPMN semantics library.
- **The GA test drive** of 24 November limited GA essentials to the designer, management and runtime with HTTP-endpoint
  workflows ([core preview map](../ga-test-drive-2026-11-24/core-preview-map.md)). In Sipke's scope for that drive,
  human tasks, DMN, BPMN, agents and external triggers were left out.
- **The release 1 activity list** has 26 activities that run on the kernel. Eight are hidden: CompensationScope,
  Compensate, Confirm, BpmnProcess, BpmnDecision, RunBpmn, CreateUserTask and ReadLine
  ([design cards](../ga-test-drive-2026-11-24/design-cards.md#3-the-release-1-activity-list), card 3). Connector and
  DMN activities are not on the list.
- **RunJavaScript on the kernel reuses the old core's Jint evaluator.** The K10 host adapter
  (`apps/Elsa.Workbench.Host/CorePreview/NativeHostAdapters.cs`, #5752) wraps `IJavaScriptScriptEvaluator`, which runs
  through `PreparedJavaScript`. JavaScript defects there are GA defects.
- **No backwards compatibility before GA** (Sipke, 25 November).
- **Initiative #3052** (Elsa 3 to Elsa 4 conversion) is human-led. It builds on its own branch,
  `initiative/3052-elsa3-to-elsa4-conversion`, on top of the old core's publication compiler, value model and variable
  lifecycle. Neither the trimmed scope nor the test drive named it. Sipke put it in 4.0 on 25 November (Q1 below).

## Summary

Sipke answered the three owner questions on 25 November (see "Owner decisions" below). The counts include his
answers. Before them, 6 issues were in scope, 9 waited on him and 30 were out of scope.

| Verdict | Count |
|---|---|
| In scope for GA | 14 |
| Needs an owner call | 0 |
| Already done, duplicate or obsolete | 13 |
| Out of scope | 31 |
| **Total** | **58** |

### Owner decisions, 25 November 2026

| Question | Recommended | Sipke's answer | Effect on the verdicts |
|---|---|---|---|
| Q1 | After GA, in 4.x | **In 4.0, full conversion.** 4.0 ships the whole initiative, ported onto the kernel. Every converter defect is GA scope. The old-core runtime work the initiative depends on (variable lifecycle, value model) is rebuilt on the kernel before release. | #3052, #4919, #5664, #5681, #5686 and #5697 are in scope. |
| Q2 | Capture in, drivers refused | **Capture in, drivers refused.** 4.0 captures complete values in the kernel's workflow history up to the checkpoint cap, behind a host setting, so the S-15 value suites can pass. Custom storage drivers are refused at publish with a clear diagnostic. Implementing drivers moves to 4.x, so the values-by-reference cut stands. | #5647 is in scope. #5698 is in scope for the refusal; the driver implementation is 4.x. |
| Q3 | K7 rules ship; rest 4.x | **K7 rules ship; rest 4.x.** G1 to G5 are the 4.0 join behaviour, documented for users. J-1 is answered for 4.0 as "no per-arrival mode", which unblocks #3052. A per-arrival mode can come later as an opt-in. K7 adds a G5 case for the early-release joins Joey found (`firstWins`, Parallel threshold) with a suspended sibling. | #4193 is out of scope for 4.0. |

**Not yet applied.** These answers change release 1 as the 24 November trim set it: Elsa 3 conversion and complete
value capture join it, and K7 gains a case. The program plan and #4229 do not record them yet. The #4229 coordinator
writes them into [the plan](../../program-goals/clean-slate-kernel.md).

**Verdicts Q1 reopens.** The verdicts above call these #3052 follow-ups out of scope because they sit on the old core.
Under full conversion, some may be part of what is rebuilt on the kernel. They keep their verdicts until the kernel port of
#3052 is planned, and then need a second pass: #3023, #3283, #4734, #4805, #4806, #4844, #4856, #4858 and the S-11
connector items #4721 to #4724 and #4737. Three stay out whatever that plan says: #4103 comes after the initiative
by D-16, #4831 is outside the initiative by its own text, and #4857 is BPMN.

### Owner questions as asked

Three questions decided all nine "Needs an owner call" verdicts.

| Question | What decides it | Issues |
|---|---|---|
| **Q1. Does 4.0 GA ship the Elsa 3 to Elsa 4 conversion?** | Neither the trimmed scope nor the test drive names it. The Elsa 3.8/3.9 parity program lists "Elsa 3 import". The converter emits definitions that today publish through the old core's compiler, which K14 deletes. If yes, converted workflows must publish onto the kernel through K10, and these converter defects become GA scope. If no, they stay on the initiative branch for a 4.x release. | #3052, #4919, #5664, #5681, #5686, #5697 |
| **Q2. Do complete value capture and storage drivers beat the values-by-reference cut?** | Joey labelled both `ga-blocker` on 24 November, the day the trim cut values by reference from release 1. A storage driver that keeps a value outside the instance is values by reference. Keep the cut and move both after GA, or reverse it in part. | #5647, #5698 |
| **Q3. Must the join-behaviour initiative finish before 4.0?** | K7 pins Flowchart joins for release 1 (G2: a join fires at most once per round; G3: an All join fires once every inbound flow has arrived). Before GA, a change to join behaviour breaks nobody. After GA it changes how running workflows behave. | #4193 |

## Designer and Studio

| Issue | Title | Verdict | Reason |
|---|---|---|---|
| [#2859](https://github.com/elsa-workflows/elsa-foundation/issues/2859) | Studio: show start-trigger placement refusals on the node, and recognise Timer/Cron as start triggers | In scope | Timer and Cron start workflows on the kernel (card 3, option B), so the designer must know they are start triggers. The backend half of draft PR #3944 targets the old core's trigger preflight and moves to K10's publish path. |
| [#3190](https://github.com/elsa-workflows/elsa-foundation/issues/3190) | An Object variable's default value can only be text | In scope | Designer gap in shipped scope: typing `{"a":1}` gives a string. Sipke decided on 9 November that the default is JSON. Size S. |
| [#3191](https://github.com/elsa-workflows/elsa-foundation/issues/3191) | Collection defaults are a JSON array: Studio list editor, no comma parsing | In scope | `["a","b"]` becomes a silently wrong list today. The design is decided and draft PR #3933 exists. No legacy values need keeping before GA. |
| [#3197](https://github.com/elsa-workflows/elsa-foundation/issues/3197) | Studio graph editor: offer earlier body results in pickers, add the Add-two-numbers browser test | Out of scope | Picker convenience in the reusable-activity editor. Graph bodies already map inputs and outputs. Draft PR #3943 can land after GA. |
| [#3282](https://github.com/elsa-workflows/elsa-foundation/issues/3282) | Value-flow ordering rules and the expression picker take ordering from structure handlers | Out of scope | Quality refactor (S-5, D-8). The suspected false refusals inside If, Switch and loops are unconfirmed. If a test confirms one, that part becomes a GA defect. |
| [#4734](https://github.com/elsa-workflows/elsa-foundation/issues/4734) | Name the operator actions Retry and Re-execute distinctly for a faulted activity | Out of scope | Built on old-core alteration kinds from the #3052 branch (#4316). The kernel's incident retry is K11's one-page recover, with its Studio view in K10. |
| [#4806](https://github.com/elsa-workflows/elsa-foundation/issues/4806) | Inspection API and Studio: show dropped members and session reads (spec 1231 follow-up) | Out of scope | Old-core inspection views of the #3052 variable lifecycle (S-7). K9 and K10 build the kernel's read models and run views. |

## Management and APIs

| Issue | Title | Verdict | Reason |
|---|---|---|---|
| [#2792](https://github.com/elsa-workflows/elsa-foundation/issues/2792) | Headless workflow submit bypasses every draft validator (validation runs only on draft promotion) | In scope | The definitions API stays in GA, and K10 publishes through it. PR #3929 (open since 6 November) conflicts with `main` and still uses the old `Essentials/` paths. It covers `definitions/submit` only; the 9 November triage added #3141's versions/ingest path to this issue, and that path still needs a fix. |
| [#3809](https://github.com/elsa-workflows/elsa-foundation/issues/3809) | A Flowchart connection on a port its source node does not declare publishes, and is never followed | Already done (initiative branch) | PR #4314 (spec 1208, S-3) merged into the initiative branch on 17 November and fixes the code side (`VF-AUTH-011`). It reaches `main` only when the initiative lands. K10's definitions-to-kernel mapping needs the same port check. |
| [#1146](https://github.com/elsa-workflows/elsa-foundation/issues/1146) | Workflow-definition import endpoint reusing the reconciliation pipeline | Out of scope | New feature. The map lists export and import as "No case". Its prerequisites #1144 and #1145 are closed. |
| [#3023](https://github.com/elsa-workflows/elsa-foundation/issues/3023) | Support engine intrinsics in graph activities (SetVariable, SetOutput, Correlate, SetName) | Out of scope | Elsa 3 parity extension. Graph bodies already return values through output mappings (spec 265), and the old graph runtime it targets is retired. The kernel's GraphActivity port (K6) and the intrinsics port (#5747) are where it would land later. |
| [#3203](https://github.com/elsa-workflows/elsa-foundation/issues/3203) | Publishing reports only the first error: collect every publish error in one response | Out of scope | Author experience improvement. The compiler it changes may not sit on the kernel's publish path, which K10 has not designed yet. |
| [#3283](https://github.com/elsa-workflows/elsa-foundation/issues/3283) | Reconciled workflow versions pass the full draft validation gate | Out of scope | File and Git reconciliation sources are outside the GA drive. The kernel registry already refuses hidden activities on any path (K6). |
| [#3284](https://github.com/elsa-workflows/elsa-foundation/issues/3284) | Workflow executable compilation reports every error in one refusal | Out of scope | Same as #3203: old-core executable compiler, diagnostics improvement. |
| [#3974](https://github.com/elsa-workflows/elsa-foundation/issues/3974) | Feature request: OpenAPI document for workflow HTTP endpoint triggers | Out of scope | New capability with no Elsa 3 precedent. The lead parked it on 7 November. Kernel routes (K8) would be its new source. |
| [#4805](https://github.com/elsa-workflows/elsa-foundation/issues/4805) | Graph activity bodies: check step-variable writes at publish (spec 1231 follow-up) | Out of scope | Old-core step-session follow-up on the #3052 branch (S-7). |
| [#4844](https://github.com/elsa-workflows/elsa-foundation/issues/4844) | Value model: JSON kind checks — refuse impossible JSON bindings at publish, name the kind at run time | Out of scope | S-8 follow-up on the old core's value-conversion planner. It also needs a spec amendment first. The kernel's binding checks come with K10's mapping. |
| [#5681](https://github.com/elsa-workflows/elsa-foundation/issues/5681) | Source-owned composite activities with dependencies are refused at publish | In scope | Q1 (full conversion in 4.0). Its driver is the Elsa 3 migration CLI (spec 1237): a converted reusable activity must load from a reconciliation source. |

## Runtime and kernel

| Issue | Title | Verdict | Reason |
|---|---|---|---|
| [#3171](https://github.com/elsa-workflows/elsa-foundation/issues/3171) | RunJavaScript fails to parse when the script ends with a line comment | In scope | RunJavaScript is on the release 1 list. The kernel adapter reuses the Jint evaluator, and `PreparedJavaScript.cs` line 46 still wraps the script on one line. Size S. |
| [#5687](https://github.com/elsa-workflows/elsa-foundation/issues/5687) | JavaScript expression returning a Promise evaluates to {} on Elsa 4 | In scope | Same evaluator, and a silent wrong value. The Jint sources have no Promise handling. Awaiting or refusing would both fix it; K12 item 3 qualifies JavaScript semantics. |
| [#4193](https://github.com/elsa-workflows/elsa-foundation/issues/4193) | Initiative: join behaviour in Elsa 4 | Out of scope | Q3: K7's G1 to G5 are the 4.0 join behaviour, and J-1 is answered "no per-arrival mode" for 4.0. The early-release joins Joey found become a G5 case in K7. The initiative continues for 4.x. |
| [#1785](https://github.com/elsa-workflows/elsa-foundation/issues/1785) | IRuntimeActivityConsumerCapability is keyed by value but registered by type, so a third capability silently collapses | Already done / obsolete | Old-core publishing and runtime DI. The trap is closed for today's two capabilities (#1783). PR #2891 is open with changes requested. K14 removes the type. |
| [#2752](https://github.com/elsa-workflows/elsa-foundation/issues/2752) | Early composite completion leaves sibling bookmarks, timers and queued work in storage (Flowchart Break, Parallel threshold, finish) | Already done / obsolete | Old-core bookmarks, timers and work items. On the kernel, K7's invariant G5 ("no path outlives its cancelled scope") is the matching check. K7 should include a Break case with a suspended sibling. |
| [#4103](https://github.com/elsa-workflows/elsa-foundation/issues/4103) | Opt-in .NET interop (proxies) for scripts and input bindings, after the Elsa 3 → 4 initiative | Out of scope | D-16 (12 November): built after the #3052 initiative. Proxies stay off by default. |
| [#4192](https://github.com/elsa-workflows/elsa-foundation/issues/4192) | Research: settings with a host default and workflow or node overrides | Out of scope | Research spike. The body says no research is done until someone picks it up. |
| [#4721](https://github.com/elsa-workflows/elsa-foundation/issues/4721) | Microsoft 365 CreateEvent: idempotent re-run so it can run in the background | Out of scope | Connector activities are not on the release 1 list. The `[BackgroundExecutionSafe]` mark is the old core's ADR 0093 worker. |
| [#4722](https://github.com/elsa-workflows/elsa-foundation/issues/4722) | Microsoft 365 UploadFile: re-run safe conflict behaviour and a C10 answer for its byte[] input | Out of scope | Same as #4721. |
| [#4723](https://github.com/elsa-workflows/elsa-foundation/issues/4723) | Slack AddReaction: treat already_reacted as success so a re-run is idempotent | Out of scope | Same as #4721. |
| [#4724](https://github.com/elsa-workflows/elsa-foundation/issues/4724) | EvaluateDecision: prove the body is correct on a background worker | Out of scope | DMN is outside the GA drive, and the worker is the old core's. |
| [#4737](https://github.com/elsa-workflows/elsa-foundation/issues/4737) | Run the eight connector activities on a background worker, then mark the ones that pass (spec 1230 Q10) | Out of scope | Same as #4721. On the kernel, background work runs as K6 jobs. |
| [#4831](https://github.com/elsa-workflows/elsa-foundation/issues/4831) | D-2 commit boundary for host functions declared effectful (with the declaration) | Out of scope | Targets the old core's checkpoint policy (`CoalescingRuntimeCheckpointPersistencePolicy`). The kernel has its own commit and effect-key rules (K6). |
| [#4857](https://github.com/elsa-workflows/elsa-foundation/issues/4857) | BPMN joins: name every joined branch as a scheduling cause (spec 1229 FR-007) | Out of scope | BPMN is a release 1 non-goal (ADR 0099), and the provenance is old-core. |
| [#4858](https://github.com/elsa-workflows/elsa-foundation/issues/4858) | ModifyVariable: show the operator's written value in the write history (spec 1229 FR-006) | Out of scope | Old-core alteration and write history on the #3052 branch. |

## Persistence and execution values

| Issue | Title | Verdict | Reason |
|---|---|---|---|
| [#5647](https://github.com/elsa-workflows/elsa-foundation/issues/5647) | Initiative: complete capture and retention of workflow execution values | In scope | Q2: complete values are captured in the kernel's workflow history, up to the checkpoint cap and behind a host setting. Its research sources are old-core files, so the work lands on the kernel's history. |
| [#5698](https://github.com/elsa-workflows/elsa-foundation/issues/5698) | Elsa 4: workflow variables, inputs and outputs cannot use a storage driver other than elsa.json | In scope (refusal only) | Q2: a driver other than `elsa.json` is refused at publish with a clear diagnostic, the body's agreed direction of 24 November. Today workflow inputs and outputs ignore the driver silently. Implementing drivers is values by reference and moves to 4.x. |
| [#1154](https://github.com/elsa-workflows/elsa-foundation/issues/1154) | Root activity inputs are never captured as inspection value snapshots (valueSnapshotCount stays 0 for bare root activities) | Already done / obsolete | Old-core diagnostic snapshots. The kernel records history its own way (K9, K10). Close as superseded per the 16 November ruling. |
| [#1155](https://github.com/elsa-workflows/elsa-foundation/issues/1155) | No operator control over captured activity values: previews are always capped and full values are unreachable (no Elsa 3 logPersistenceConfig equivalent) | Already done / obsolete | Old-core snapshot code, superseded by the kernel. Its "see more" half is in #5647. Its "hide more" half (exclude values per activity or input) is in no other issue; fold it into #5647 if it should survive. |
| [#1358](https://github.com/elsa-workflows/elsa-foundation/issues/1358) | Reference GC orphans trigger-binding and recurring-schedule rows of hard-deleted artifacts | Already done / obsolete | Old-core reference GC and trigger stores. Kernel retention and delete are K11. |
| [#3194](https://github.com/elsa-workflows/elsa-foundation/issues/3194) | Elsa 3 resource variables (FormFile, Stream, byte[]) have no Elsa 4 counterpart: they need their own spec | Out of scope | A durable resource handle is values by reference, cut from release 1. Large bodies stay in state under the checkpoint cap (map, runtime pass). |
| [#4856](https://github.com/elsa-workflows/elsa-foundation/issues/4856) | Variable write history and journal: first page within the activity-list read time (spec 1229 SC-004) | Out of scope | Performance of the old-core write-history view. K9's read models replace it. |

## Hosting and packaging

| Issue | Title | Verdict | Reason |
|---|---|---|---|
| [#5688](https://github.com/elsa-workflows/elsa-foundation/issues/5688) | Workbench exits after a shell restart: tracing bridge writes to the disposed shell's store | Already done / obsolete | The bridge (`EngineTracingBridge.cs`) listens only to the old core's `Elsa.Workflows.Runtime` activity source, and only the Workbench composes it. The crash happened on an old-core run. If K11's basic traces reuse the bridge, the shell-lifetime fix goes with that work. |
| [#1891](https://github.com/elsa-workflows/elsa-foundation/issues/1891) | Foundation.Host cannot take a separately hosted Studio: the OpenIddict store and CORS live only in the Workbench application | Out of scope | A separately hosted Studio already works on the Essentials host (#5671), which registers the OpenIddict store and a Studio CORS policy in `apps/Elsa.Essentials.Host/Program.cs`. Foundation.Host, the bare host you compose yourself, still lacks both. Moving them into shell features can wait until after GA. |

## Docs and design records

| Issue | Title | Verdict | Reason |
|---|---|---|---|
| [#4050](https://github.com/elsa-workflows/elsa-foundation/issues/4050) | Proposed ADR: per-arrival join mode (deferred until a real need and evidence) | Out of scope | Parked by its author until a real need appears. Moved under #4193 (J-1). ADR 0064 rejects declared join modes. |

## Elsa 3 conversion

| Issue | Title | Verdict | Reason |
|---|---|---|---|
| [#3052](https://github.com/elsa-workflows/elsa-foundation/issues/3052) | Epic: Convert Elsa 3 workflows to Elsa 4 | In scope | Q1: full conversion ships in 4.0, ported onto the kernel. All specs except S-9 are on the initiative branch. S-9 (#5460) and S-15 (#5662) are drafts. Under Q3, J-1 no longer blocks its closure. |
| [#4919](https://github.com/elsa-workflows/elsa-foundation/issues/4919) | Elsa 3 import: two JTest JavaScript definitions fail to publish with a 500 (regression since #4838) | In scope | Q1. An unexplained 500 at publish, seen at the initiative head. It overlaps #5686 (same two definitions). |
| [#5664](https://github.com/elsa-workflows/elsa-foundation/issues/5664) | Elsa 3 converter: map Elsa 3 custom JavaScript functions to declared Elsa 4 host functions | In scope | Q1. Converter feature for the migration skill. |
| [#5686](https://github.com/elsa-workflows/elsa-foundation/issues/5686) | Converted Elsa 3 workflows fail to publish with VF-STATE-002 (HTTP body and query state) | In scope | Q1. VF-STATE-002 comes from the old core's publication compiler, so the fix must also hold on the kernel's publish path. It blocks at least six JTest definitions in S-15. |
| [#5697](https://github.com/elsa-workflows/elsa-foundation/issues/5697) | Converter guidance for a For-loop value read yields a workflow that fails to publish (VariableUnknown) | In scope | Q1. Converter guidance defect found in S-15. |
| [#5666](https://github.com/elsa-workflows/elsa-foundation/issues/5666) | Elsa 3 converter: diagnose unregistered custom variable types and unmapped storage drivers | Already done (initiative branch) | PR #5668 merged into `initiative/3052-elsa3-to-elsa4-conversion` on 24 November. It reaches `main` when the initiative lands. |
| [#5683](https://github.com/elsa-workflows/elsa-foundation/issues/5683) | Elsa 3 converter: diagnose non-default storage drivers on workflow inputs and outputs | Already done (initiative branch) | PR #5696 merged into the initiative branch on 24 November. |

## CI and tests

| Issue | Title | Verdict | Reason |
|---|---|---|---|
| [#1419](https://github.com/elsa-workflows/elsa-foundation/issues/1419) | Five backend e2e suites fail on main (SetOutput submit, alteration-plan shape, OpenTelemetry search) | Already done / obsolete | #2372 fixed the SetOutput submit cause. The alteration-plan suite tests the old core, and the migration-plan runner is cut. One re-run on `main` settles the OpenTelemetry route before closing. |
| [#3259](https://github.com/elsa-workflows/elsa-foundation/issues/3259) | Flaky in CI: NativeHostingConformanceTests.Departed_routing_owner_is_reclaimed_and_its_real_transport_command_completes_on_survivor | Already done / obsolete | Quarantined test of the old core's native hosting (`essentials/tests/Workbench`). K14 removes it. Draft fix PR #3946 can close with it. |
| [#3260](https://github.com/elsa-workflows/elsa-foundation/issues/3260) | Flaky in CI: WorkbenchBackgroundActivityRecoveryTests.Killed_workers_are_reclaimed_with_stable_invocations_and_one_native_continuation | Already done / obsolete | Same as #3259: the old core's background worker recovery. |
| [#4916](https://github.com/elsa-workflows/elsa-foundation/issues/4916) | Re-capture the BPMN legacy upgrade baselines (or retire the Legacy* family) before the first stable release | Already done / obsolete | Decided by the 25 November "no backwards compatibility before GA" direction, and BPMN is a release 1 non-goal. Retire the `Legacy*` family, in K14's retirement inventory at the latest. |
| [#2849](https://github.com/elsa-workflows/elsa-foundation/issues/2849) | Architecture guard fails on a local Windows checkout (pack-test cleanup, Claude worktrees) | Out of scope | Developer tooling. PR #3936 is ready and can land at any time. |
| [#3177](https://github.com/elsa-workflows/elsa-foundation/issues/3177) | CI: no gate catches Studio API clients drifting from the backend API | Out of scope | CI improvement. K10's composed Studio journeys exercise the GA paths against a real backend. |
| [#3263](https://github.com/elsa-workflows/elsa-foundation/issues/3263) | CI flake tracker | Out of scope | Standing record. Its body says never to close it. |
| [#3267](https://github.com/elsa-workflows/elsa-foundation/issues/3267) | Spec 263: measure the tiered-CI outcomes (SC-001, SC-002, SC-004) after the switch | Out of scope | CI measurement. Its draft PR #3931 is closed. |

## Sources

- [Clean-slate kernel plan](../../program-goals/clean-slate-kernel.md): milestones K4 to K14 and the trimmed scope.
- [GA test drive map](../ga-test-drive-2026-11-24/core-preview-map.md) and
  [design cards](../ga-test-drive-2026-11-24/design-cards.md).
- [Issue triage of 9 November](../triage-2026-11-09/README.md), for the earlier calls on the older issues.
- `apps/Elsa.Workbench.Host/CorePreview/NativeHostAdapters.cs`,
  `essentials/src/Expressions/JavaScript/Jint/Services/PreparedJavaScript.cs`,
  `apps/Elsa.Essentials.Host/Program.cs`, `essentials/src/Workflows/Runtime/Native/Model/ActivityCatalog.cs`.
