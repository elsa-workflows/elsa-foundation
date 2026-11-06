# Research: business decisions

**Date**: 2026-11-02. Research results, not product acceptance. The owner's card selected common bounded tables and explicit refusal of unsupported FEEL.

## Decision: separate optional Decisions domain

**Rationale**: Rules Core's `RuleProgram` has facts, premises and conclusions; Runtime/Persistence add rounds, receipts and committed facts. These are finite positive inference semantics, not typed input/output table contracts. Adding a language string would not preserve their meaning. Proposed purpose: “Evaluates versioned business decisions and explains their results.” Preserve ADR 0088 and existing Rules contracts/artifacts.

**Alternatives**: Broaden Rules with a second unrelated model, or adapt each decision into inference. Both entangle accepted inference/recovery contracts with different semantics. Reuse shared publication/permission/persistence mechanisms through public seams, rather than reuse Rules state/codecs.

**Governance**: The current request authorizes autonomous research/design/delivery. An additive constitutional domain admission will be recorded explicitly with the owner before production projects or a constitution amendment. The plan is otherwise concrete; no architecture vote or prior-domain authority is implied.

## Decision: own the bounded evaluator

Actual NuGet consumers ran both candidates on .NET SDK 10.0.402. These are representative probes, not comprehensive security or DMN/TCK qualification.

| Candidate | Exact pin / observed envelope | Result |
| --- | --- | --- |
| net.adamec.lib.common.dmn.engine | 1.1.1; commit `9c849a7c1519cf1c36aa1d6ade5e1ec1a0fd5f1d`; netstandard2.0; MIT. DynamicExpresso.Core 2.12.0, NLog 4.7.15, RadCommons.core 2.0.1 and transitive Newtonsoft.Json 12.0.2. Last upstream push observed December 2022. | Basic literal/range tables work, but a valid FEEL conditional output throws because expressions use C# semantics. Unset numeric input throws. Restore reports NU1903 for Newtonsoft.Json. Reject unchanged. |
| ScratchyDisk.DmnEngine | 2.0.1; commit `67bae6927a28f77138aae98dcd35d93b1af39838`; net10.0; MIT-form license retaining attribution. Runtime Antlr4.Runtime.Standard 4.13.1 and NLog 5.3.4. Observed upstream is a single initial commit dated 2 March 2026; continued maintenance unestablished. | Decimal/null/range/alternative/negation/conditional probes work, and actual hit-rule identities/snapshots are available. CLR static method and reflection probes execute; no budget/cancellation hooks found in inspected evaluator/options. Reject unchanged for pure bounded evaluation. |
| Elsa-owned closed grammar | No implementation yet; no third-party evaluator runtime needed. | Recommended for the explicitly limited profile. Carries an implementation/conformance/maintenance obligation; it has not passed any product test. |

Specific observed Scratchy results: `DateTime.Parse("2026-11-02")` returned `2026-11-02T00:00:00+01:00`; `"x".GetType().Name` returned `String`. These demonstrate host/CLR exposure, not exploitation or external business actions. Wrapping a synchronous evaluator in a timeout would not stop unbounded evaluation.

Scratchy table boundaries: 0/1000 → Automatic; 1001/10000 → Manager; 10001 → no result. Its null wildcard returned true, whereas selected DMN 1.3 §10.3.2.10 specifies non-null matching without inputValues. Do not inherit that discrepancy implicitly. Common's FEEL conditional output failed on the Automatic row. Neither candidate's full hit-policy/null/interoperability matrix was qualified.

**Alternatives**: Harden/fork Scratchy with AST allowlisting and operation/allocation metering, or embed an external JVM/service evaluator. Both add dependency/maintenance boundaries unnecessary for the owner-selected small grammar. Reconsider a vetted engine for a later wider profile; do not promise broad FEEL now.

Primary sources: [Common README](https://github.com/adamecr/Common.DMN.Engine/blob/9c849a7c1519cf1c36aa1d6ade5e1ec1a0fd5f1d/readme.md), [project](https://github.com/adamecr/Common.DMN.Engine/blob/9c849a7c1519cf1c36aa1d6ade5e1ec1a0fd5f1d/net.adamec.lib.common.dmn.engine/net.adamec.lib.common.dmn.engine.csproj), [Scratchy evaluator](https://github.com/Scratchydisk/ScratchyDisk.DmnEngine/blob/67bae6927a28f77138aae98dcd35d93b1af39838/ScratchyDisk.DmnEngine/feel/eval/FeelEvaluator.cs), [options](https://github.com/Scratchydisk/ScratchyDisk.DmnEngine/blob/67bae6927a28f77138aae98dcd35d93b1af39838/ScratchyDisk.DmnEngine/engine/execution/context/DmnExecutionContextOptions.cs), [license](https://github.com/Scratchydisk/ScratchyDisk.DmnEngine/blob/67bae6927a28f77138aae98dcd35d93b1af39838/license.txt).

## Decision: reuse the delivered abstract execution foundation

The owner's follow-up identified the connection to execution-model delivery. Refreshed current source and GitHub show #3410 and #3603 merged, beyond the older chat handoff. Actual Runtime Core includes model material/protocol registration/drivers, exact execution authority, model state/accepted-input receipts and checkpoint participation. See [the concrete integration contract](contracts/execution-integration.md) and [spec 1017](../1017-direct-bpmn-hosting/contracts/hosting.md).

The first-release direct evaluator is a pure library call; native/BPMN integration uses the existing caller lifecycle. Independent durable decision runs are not required by #3602. If needed later they must use that shared host, not a Decisions/Rules-owned parallel execution coordinator. Saved-case tests are bounded synchronous calls with completed receipts; a proposed resumable-test ledger was removed during design review because it was unnecessary and conflicted with default raw-value retention.

Existing current-subject authorization and workflow value-protection/inspection contracts also apply. An execution authority snapshot is not a grant; the integration explicitly resolves current execution subjects and carries Decisions protection into existing output/evidence policy rather than assuming workflow read/execute permission covers it.

## Decision: DMN 1.3 named subset

**Rationale**: Pinned dmn-js models DMN 1.3; normal external table XML uses that namespace. A tested versioned subset is more honest than accepting later XML namespaces without semantic evidence. [The profile](contracts/dmn-profile.md) concretizes types, grammar, policies, limits, null/no-match and unsupported constructs. This first profile supports literal outputs rather than unrestricted output expressions.

**Alternatives**: Full FEEL, later DMN, decision graphs and temporal/function support materially enlarge qualification and are outside the selected release. A future profile can add them without reinterpreting existing artifacts.

Primary sources: [OMG DMN 1.3](https://www.omg.org/spec/DMN/1.3/PDF), [DMN 1.5](https://www.omg.org/spec/DMN/1.5), [Camunda table XML](https://docs.camunda.io/docs/components/modeler/dmn/decision-table/), [DMN TCK](https://github.com/dmn-tck/tck). The TCK is a reference source, not a claimed executed suite. Verify licensing before incorporating its fixtures.

## Decision: one owned accessible table editor; external dmn-js witness

Pinned dmn-js 18.0.1, commit `feddee69ded44a62324cee04fe162aa73b9a1395`, and dmn-moddle 12.2.1 were probed in actual Chromium. The public toolkit download page was behind release metadata; use the pin, not a floating download.

Probe results: import with zero warnings; table rows rendered; modeling.editCell changed an output; save/reimport recovered it; undo/redo restored old/new values; tested rule ID, custom namespace/extension text survived; destroy removed modeler DOM. This is a bounded editor round trip, not arbitrary XML preservation, accessibility proof, evaluator equivalence or normal-host qualification.

**Rationale**: An owned native-control grid can expose exactly the admitted profile with standard table labeling/keyboard behavior and Studio tokens. Keep backend profile validation authoritative and preserve original XML beside targeted edits. Use dmn-js for external-tool round trips, avoiding a second production editor.

**Alternative**: dmn-js is viable and supplies editing/undo/serialization; adopting it requires containing broader expression/type/policy offerings, mapping theme tokens, testing keyboard/screen-reader behavior, disposing lifecycle correctly and complying with its license. It is not simply MIT: the [bpmn.io license](https://github.com/bpmn-io/dmn-js/blob/feddee69ded44a62324cee04fe162aa73b9a1395/LICENSE) requires the watermark code intact and the watermark visible when displaying its editor. No legal opinion is asserted.

Sources: [release](https://github.com/bpmn-io/dmn-js/releases/tag/v18.0.1), [modeler](https://github.com/bpmn-io/dmn-js/blob/feddee69ded44a62324cee04fe162aa73b9a1395/packages/dmn-js/src/Modeler.js), [manager APIs](https://github.com/bpmn-io/dmn-js/blob/feddee69ded44a62324cee04fe162aa73b9a1395/packages/dmn-js-shared/src/base/Manager.js).

## Existing integration seams

- `Essentials/src/Workflows/Publishing/Core/Contracts/IExecutableCompilationSource.cs` and `Models/ExecutableCompilationContribution.cs`: tenant-aware node metadata for immutable decision embedding. Dependency claims currently identify workflow executables, not arbitrary decision assets.
- `Extensions/Rules/src/Workflows/Design/Services/RulesPublishedProgramPinSource.cs`: pattern for normal publication pinning, not a codec/model to reuse.
- `Essentials/src/Activities/Bpmn/Elsa.Activities.Bpmn/Activities/BpmnDecision.cs`: gateway expression→outcome only. A normal bound native `EvaluateDecision` child can do business evaluation without changing gateways/interpreter. `IBpmnWorkBinder` is one replacement, not an additive registry.
- `Essentials/src/Workflows/Design/Persistence/EntityFrameworkCore/Stores/EfWorkflowTemplateStore.cs`: tenant/revision/operation/atomic-write precedent. New Decisions persistence follows CQS instead of copying aggregate-returning mutation signatures.
- `Essentials/src/Connections/Api/Services/HttpContextConnectionPermissionGate.cs`: principal-backed authorization pattern without importing workflow runtime context into Decisions Core.
- `Extensions/Connections/studio/Elsa.Studio.Connections/`: optional SDK module pattern. Use featureAreas, workspace.resourceKey, capability-discovered authenticated HTTP and [public module UI](../../Studio/docs/module-ui.md), not host switches or Preferences storage.

## Retained research evidence

The owning session retained scripts, dependency pins and raw outputs outside the repository at `/Users/sipke/.codex/visualizations/2026/11/02/01a19e27-c0c0-7b21-bbf8-b7f5e08b1892/decision-research/`:

- `evaluator/research.md`, `probe/`, `table/`, `common-table/` and three raw output files.
- `editor/probe.cjs`, `package.json`, `package-lock.json`, `probe-result.json`.

Rerun evaluator probes with `dotnet run --project <probe project>`; editor with npm ci then node probe.cjs (its local Chromium path is environment-specific). Raw output assertions describe each probe's expected boundary, not a blanket engine verdict. No product prototype, TCK, full policy/provider matrix, package publication, purchase, deployment or live Studio acceptance occurred.
