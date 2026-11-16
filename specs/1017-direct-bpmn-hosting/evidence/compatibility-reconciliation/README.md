# Public API, version and compatibility reconciliation

**Current decision and audit:** [Source25148 qualification](../main-adoption/ab46bc22a/README.md) refreshes exact publish-statea82590ea. Runtime Core180/0 and Runtime4/0 tracked changes, twelve current-product binary comparisons and the read-only computation pass. [The owner approved a scoped computed4.0-preview exception for this PR](https://github.com/elsa-workflows/elsa-foundation/pull/3410#issuecomment-6540308020). T042 is complete under that exception; the older audit below records the policy question as it stood then and does not override the current decision. No release/publish or general-policy amendment is implied.

This T042 audit compares implementation source `fae8dce0d5177e987ca50affde428eec03303219` with the actual last-published preview records at publish-state `f49ff392871e217896efdbfe8c208f10cece7365`. The adopted main is `31b75cb60df779c94c7c28867dc6023f749a40f9` (PR3463, variable/argument add-button polish). That change does not touch the qualified BPMN gateway edit, export, publication, run or persistence paths. [T041's actual witnesses](../final-compatibility/README.md) retain their original executing sources; they are not relabelled as this head.

## API comparison and version proposal

| Package | Recorded preview | Tracked API delta | Read-only computed numeric version |
|---|---|---|---|
| Elsa.Workflows.Runtime.Core | 4.0.55-preview | 177 additions, zero removals | 4.0.56 |
| Elsa.Workflows.Runtime | 4.0.69-preview | Four additions, zero removals | 4.0.70 |
| Elsa.Activities.Bpmn | 4.0.15-preview | New hosting feature, RunBpmn, activity-runtime contract and three typed activity records; existing public source files unchanged | 4.0.16 |
| Elsa.Activities.Bpmn.Core | No published package | New consumer contracts with tracked API | 4.0.0 |
| Elsa.Activities.Bpmn.Api | No published package | New host/API feature with tracked API; endpoint DTOs internal | 4.0.0 |
| Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore | 4.0.42-preview | Additive store capabilities, model storage/migrations and physical comparisons | 4.0.43 |
| Elsa.Activities.DispatchWorkflow.Runtime | 4.0.13-preview | No package change | Retains 4.0.13 |

The two tracked Runtime deltas are identical against the published source and this PR's current main base. Both raw diffs and all exact added entries are retained. Existing `IWorkflowExecutableStore` and `IRuntimeCheckpointCommitStore` additions have default refusal/false behavior; existing provider implementations gain no model authority from an interface update. Native constructors and signatures remain. Optional model state/origin fields preserve their absent native wire. `IBpmnModelRuntime`'s added cancellation/incident/retry methods also default to refusal for existing replacements. New `.Core` code references Runtime Core only, and the API consumes those contracts without a provider or interpreter dependency.

For the untracked BPMN implementation package, the only changed existing C# file is internal `BpmnProcessRuntime`; its evaluation is shared through internal `BpmnEvaluationComponent`. New public surfaces are `ActivitiesBpmnHostingFeature`, `RunBpmn`, `IBpmnModelActivityRuntime`, `RunBpmnState`, `RunBpmnCompletion` and `RunBpmnResult`. Existing authored DTOs, activity identities, structure schema and compatibility codec remain. EF changes add participant methods/metadata/migrations without removing existing public declaration lines; this source review is not a binary ApiCompat certification. The retained [storage/migration gate](../providers/model-storage/README.md) and actual upgrade journeys establish their separately stated behavior.

The calculator command was:

```sh
dotnet run --project tools/versioning/Elsa.Versioning.Calculator -c Release -- \
  --record-ref origin/publish-state --commit fae8dce0d5177e987ca50affde428eec03303219 \
  --output /absolute/fresh/version-computation.json
```

The recorded publish-state revision, full record and computation are frozen here. The command packs/publishes nothing and reserves no version. A later publish must calculate against its own current source/record. No project version was hand-authored or force-advanced. Public API analyzers build in the source-bound host builds; **no ApiCompat binary or automatic magnitude gate was run**.

**Version magnitude remains an owner decision.** [Framework §4.2](../../../../.specify/memory/constitution-framework.md#4-versioning) classifies compatible `.Core` expansion, including default interface members, as MINOR. The configured pre-GA 4.0 line currently computes 4.0.55-preview → 4.0.56-preview for Runtime Core. [Elsa §E5](../../../../.specify/memory/constitution.md#e5-elsa-packaging-and-versioning) retains the magnitude rule while [ADR0080 D4](../../../../docs/adr/0080-elsa-4-simplification-decisions.md#d4--pre-ga-cleanup) keeps one version line until GA; no stable Elsa release baseline is present in the record. This audit exposes the preview-baseline interpretation/exception needed before merge rather than declaring the patch-sized preview proposal a passed magnitude check. T042 stays unchecked pending that decision and final reconciliation.

## Contract and composition boundaries

The [Core README](../../../../Essentials/src/Activities/Bpmn/Core/README.md#declared-model-formats) is the profile index for definition formats1–5. Frozen native `elsa.bpmn.structure/1.0.0`, direct definition format, `elsa.bpmn/1`, runtime material schema and private state versions are independent. Omitted fields retain old wire/hashes; unknown profiles/frames refuse. The legacy alias/codec stays installed. Existing XML import/export continues through the authored native envelope and released Interchange0.5.0; this PR adds no general XML-to-direct-model conversion. Library support or retained XML does not establish a host binding.

`ActivitiesBpmnHosting` composes Events/Runtime Resumption, common model hosting, the scoped driver, native-worker/embedding checkpoint enrichers and durable outbox handlers. `AdmitNewExecutions=false` is the default and leaves accepted readers/workers installed. `ActivitiesBpmnHostingApi` composes that feature with normal Identity and explicit persistence scope. `RunBpmn` consumes the same driver and freezes its model pin during native publication. Model dependencies and native worker/pipeline pins remain Runtime roots; the actual [retention/Design-off recovery](../retention/runtime-only-rollback/README.md) demonstrates this boundary. Unknown provider tuples and actor providers outside the built-in single-node mailbox refuse.

The shared physical EF tests run on SQLite/PostgreSQL/SQL Server/MySQL. Fresh/additive migration fixtures on the latter three engines are not original old-engine stores or actual host upgrades. Actual upgraded-host, runtime-only and existing Studio journeys qualify SQLite. First-class direct-model Studio, distributed/Orleans placement and task-runner follow-up remain separate program obligations/deferrals. Native composite fault disposition #3242 remains an owner decision; the implementation preserves observed native behavior. The API README, extension catalogs, root index and reproduction sequence now reflect the implemented cancellation, retry, duration, signal and settlement surfaces without asserting those remaining acceptances.

The [manifest](manifest.json) binds the released record, raw comparisons, complete computed proposal and [summary](summary.json). Documentation relative links and the repository catalog link check pass. Full affected-project gates, FR/SC mapping, independent complete-PR review, required CI and the deferred GitGuardian finding remain separate open delivery gates.

The [round1 repair audit](../reviews/round-1/repair-qualification/api/tracked-api-comparison.json) refreshes publish-state8832507151eaa2a308a69cf2cd23ac41bab52b5b at source0ce9a41db: Runtime Core180 additions/0 removals (Source rename is unpublished; public Core event adds three entries), Runtime4/0. Its current version computation still proposes Core4.0.56. This preserves the pending owner magnitude decision above; it is not a binary ApiCompat or magnitude pass. The original audit assets retain their original source.
