# Test Migration Ledger: Value-Flow Redesign

This ledger preserves the behavioral intent of tests affected by the replacement of memory blocks,
argument wrappers, ambient expression access, and mutable activation state. It is the deletion gate
for the migration described by [the feature specification](spec.md) and
[ADR 0045](../../docs/adr/0045-workflow-value-flow-uses-role-owned-bindings-and-immutable-invocation-records.md).

## Ledger rules

- **Retain** means the objective remains valid. Its fixture may be adapted to the canonical role-owned
  value model without weakening the assertion.
- **Replace** means the objective remains necessary, but its current test mechanism asserts or relies
  on a legacy memory/argument behavior. A new objective must cover the behavior through canonical
  bindings, invocation records, variable frames, or explicit transitions.
- **Remove** means the legacy behavior is intentionally prohibited. The listed replacement or
  prohibition guard becomes its successor objective.
- `Legacy baseline exists; replacement planned` means the current test is evidence only; it does not
  satisfy the migration gate.
- `Missing; planned` means no sufficient current test was found and a new test is required.
- A row reaches `Implemented / passing` only when the replacement exists in the target project and
  passes in the relevant test command.

> **Non-deletion gate:** no legacy test, fixture, helper, or production type covered by this ledger may
> be deleted until every replacement objective on which it depends is `Implemented / passing`.
> Temporary dual coverage is required. A `Remove` disposition authorizes eventual removal, not
> immediate deletion.

## Direct `IMemoryBlock` and `IMemoryRegister` test anchors

These files directly construct, stub, or implement a legacy memory abstraction. Each anchor is kept
separate so none can disappear inside a broad migration task.

| Current anchor and objective | Disposition | Replacement objective | Target project / fixture | Current status |
|---|---|---|---|---|
| `tests/Elsa/Activities/ControlFlow/Tests/ForEach/ForEachItemResolutionRuntimeTests.cs`: resolve each loop item through an iteration-local value location | Replace | Materialize the item into an explicit per-iteration variable frame, pin the consumer input, and prove parallel/repeated iterations cannot observe another iteration's item. | `Elsa.Activities.ControlFlow.Tests`; `ForEachItemResolutionRuntimeTests` canonical-frame fixture | Legacy baseline exists; replacement planned |
| `tests/Elsa/Activities/Sequence/Tests/SequenceContainerVariableRuntimeTests.cs`: share and mutate a sequence-scoped variable | Replace | Declare a lexical variable frame, mutate it only through explicit `Set`, and verify descendant visibility plus checkpoint/recovery. | `Elsa.Activities.Sequence.Tests`; `SequenceContainerVariableRuntimeTests` frame fixture | Legacy baseline exists; replacement planned |
| `tests/Elsa/Activities/Http/Tests/WriteHttpResponseLiveWriteTests.cs`: bind response inputs by preset live blocks | Replace | Hydrate ordinary annotated input properties from one committed input snapshot and verify the response write uses only those pinned values. | `Elsa.Activities.Http.Tests`; `WriteHttpResponse` hydrated-input fixture | Legacy baseline exists; replacement planned |
| `tests/Elsa/Activities/Runtime/Tests/SetDataLeafExecutionTests.cs`: seed a shared block and update workflow data | Replace | Materialize the source value, execute an engine-intrinsic explicit state transition, and verify the new variable-frame value without a shared address. | `Elsa.Activities.Runtime.Tests`; `SetData` intrinsic transition fixture | Legacy baseline exists; replacement planned |
| `tests/Elsa/Activities/Runtime/Tests/VariableExpressionEvaluatorTests.cs`: read a workflow variable through a register-backed expression | Replace | Resolve a structured `VariableReadBinding` against the declaring lexical frame, with missing/out-of-scope diagnostics and no mutation capability. | `Elsa.Activities.Runtime.Tests`; variable-binding evaluator fixture | Legacy baseline exists; replacement planned |
| `tests/Elsa/Activities/Runtime/Tests/ContainerScopedVariableEvaluatorTests.cs`: resolve a container-scoped variable block | Replace | Resolve the declaration and activation identities to the correct variable frame and reject ambiguous, sibling, or expired scopes. | `Elsa.Activities.Runtime.Tests`; scoped-variable binding fixture | Legacy baseline exists; replacement planned |
| `tests/Elsa/Expressions/JavaScript/Jint/Tests/AnyValueReadParityTests.cs`: preserve `Any`/JSON value parity in JavaScript | Retain | Evaluate the same values from an explicit immutable parameter map, round trip their portable form, and prove no memory register or ambient output access is available. | `Elsa.Expressions.JavaScript.Jint.Tests`; explicit-parameter parity fixture | Legacy baseline exists; fixture adaptation planned |
| `tests/Elsa/Expressions/Tests/Unit/LiquidTemplateManagerCacheKeyTests.cs`: keep Liquid compilation/cache keys isolated by relevant inputs | Retain | Preserve cache-key assertions with a parameter-only evaluation context and portable expression options. | `Elsa.Expressions.Tests`; Liquid parameter-context fixture | Legacy baseline exists; fixture adaptation planned |
| `tests/Elsa/Expressions/Tests/Unit/LiquidTemplateManagerInvalidTemplateTests.cs`: report invalid Liquid templates predictably | Retain | Preserve diagnostic assertions without a memory-context stub and include the declared parameter contract in validation. | `Elsa.Expressions.Tests`; Liquid validation fixture | Legacy baseline exists; fixture adaptation planned |
| `tests/Elsa/Workflows/Runtime/Tests/RuntimeContainerScopeServiceTests.cs`: create and restore container-local variable storage | Replace | Create, persist, restore, and retire role-specific variable frames keyed by declaration and scope activation; do not persist `IVariable` or memory-block objects. | `Elsa.Workflows.Runtime.Tests`; runtime variable-frame service fixture | Legacy baseline exists; replacement planned |
| `tests/Elsa/Workflows/Runtime/Tests/RuntimeLoopIterationScopeFactoryTests.cs`: create isolated loop-iteration storage | Replace | Create stable iteration identities and isolated frames, including parallel completion out of order and deterministic collection by input/iteration identity. | `Elsa.Workflows.Runtime.Tests`; loop-iteration frame factory fixture | Legacy baseline exists; replacement planned |
| `tests/Elsa/Workflows/Runtime/Tests/RuntimeVariableScopeFactoryTests.cs`: compile variable declarations into runtime storage | Replace | Compile pure alias-based variable schemas and defaults, create frames per structural activation, and round trip them without runtime memory implementations. | `Elsa.Workflows.Runtime.Tests`; variable-schema/frame factory fixture | Legacy baseline exists; replacement planned |

## Activity construction, input materialization, and result capture

| Current anchor and objective | Disposition | Replacement objective | Target project / fixture | Current status |
|---|---|---|---|---|
| `tests/Elsa/Activities/Runtime/Tests/ActivityArgumentBinderTests.cs`: bind `InputArgument<T>` and `OutputArgument<T>` wrappers after construction | Remove | Hydrate ordinary annotated properties once from the committed `InvocationInputSnapshot`; reject missing required members and preserve omitted versus explicit `null` versus default. Add a prohibition guard for runtime wrapper binding. | `Elsa.Activities.Runtime.Tests`; generated/reflection hydration contract fixture, plus `Elsa.Architecture.Tests` | Legacy baseline exists; replacement planned |
| `tests/Elsa/Activities/Runtime/Tests/ClrActivityConstructorTests.cs`: resolve a descriptor, construct a CLR activity, and inject dependencies | Replace | Preserve descriptor opacity and DI activation, then hydrate inputs exactly once before user code; verify fresh CLR object and activation lease per attempt, including retry/resume disposal. | `Elsa.Activities.Runtime.Tests`; transient CLR activation fixture | Legacy baseline exists; replacement planned |
| `tests/Elsa/Activities/Composition/Tests/WorkflowActivityConstructorTests.cs`: construct and bind a child-workflow activity | Replace | Activate the child-workflow boundary with a typed request/result contract and canonical bindings, without a synthetic dynamic argument bag. | Composition test project; typed child-workflow invocation fixture | Legacy baseline exists; replacement planned |
| `tests/Elsa/Activities/Runtime/Tests/WriteLineBoundInputExecutionTests.cs`: execute a primitive with a bound wrapper input | Replace | Commit the input snapshot, hydrate the plain input property, execute, and prove later source changes cannot affect the attempt. | `Elsa.Activities.Runtime.Tests`; pinned-input `WriteLine` fixture | Legacy baseline exists; replacement planned |
| `tests/Elsa/Activities/Runtime/Tests/SetDataLeafActivityTests.cs`, `FinishActivityTests.cs`, `WriteLinesActivityTests.cs`, `FaultActivityTests.cs`, `CorrelateActivityTests.cs`, `ReadLineActivityTests.cs`, and `InlineActivityTests.cs`: expose primitive activity inputs/outputs through wrappers | Replace | Preserve each public behavior with plain annotated input properties, typed results or explicit transitions, stable contract member keys, and no activity-owned value address. | `Elsa.Activities.Runtime.Tests`; migrated primitive activity fixtures | Partial: `WriteLines`, `ReadLine`, and the adjacent `Event` start leaf now use plain inputs/atomic results; the remaining listed primitives still use legacy wrappers or await intrinsic/transition support. |
| `tests/Elsa/Activities/ControlFlow/Tests/While/WhileActivityTests.cs`, `ForEach/ForEachActivityTests.cs`, `If/IfActivityTests.cs`, `Do/DoActivityTests.cs`, `For/ForActivityTests.cs`, and `Switch/SwitchActivityTests.cs`: configure control-flow activities with argument wrappers | Replace | Lower literals, variables, results, and expressions to canonical input bindings; execute control flow as engine intrinsics with explicit scope/result contracts. | `Elsa.Activities.ControlFlow.Tests`; canonical-binding fixtures per intrinsic | Legacy baselines exist; replacements planned |
| `tests/Elsa/Activities/Http/Tests/WriteHttpResponseLiveWriteTests.cs` and adjacent HTTP execution fixtures: use wrapper-backed HTTP activity contracts | Replace | Hydrate request/response activity inputs from immutable snapshots and expose typed results without live block writes. | `Elsa.Activities.Http.Tests`; HTTP activity contract fixtures | Legacy baseline exists; replacement planned |
| `tests/Elsa/Activities/Runtime/Tests/CodeIoLeafRuntimeTests.cs`: preserve code-first leaf input/output execution | Replace | Execute ordinary CLR activity members through the same snapshot/result path used by dynamically authored definitions and compare their canonical contracts. | `Elsa.Activities.Runtime.Tests`; code/dynamic leaf conformance fixture | Legacy baseline exists; replacement planned |
| Existing output-capture and workflow-output read-back coverage, including `WorkflowOutputReadBackEndToEndExecutionTests.cs` and `FinishCorrelateExecutionTests.cs` | Replace | Atomically commit one typed invocation result, expose named read-only projections, reuse the committed result after a crash before downstream scheduling, and reject required projections absent on any path. | `Elsa.Activities.Runtime.Tests`, `Elsa.Workflows.Runtime.Tests`, and Groundwork recovery fixtures | Legacy baselines exist; invocation-result replacement planned |

## Variables, scopes, durability, and runtime execution

| Current anchor and objective | Disposition | Replacement objective | Target project / fixture | Current status |
|---|---|---|---|---|
| `tests/Elsa/Activities/Runtime/Tests/SetVariableTypedBindingTests.cs` | Replace | Type-check a structured variable-write intent, commit it explicitly, and reject alias/type mismatch before execution. | `Elsa.Activities.Runtime.Tests`; typed variable-write fixture | Legacy baseline exists; replacement planned |
| `tests/Elsa/Activities/Runtime/Tests/SetVariableDurabilityExecutionTests.cs` | Replace | Persist a variable-frame transition atomically and recover the same value after restart without replaying a hidden memory write. | `Elsa.Activities.Runtime.Tests` and `Elsa.Persistence.Groundwork.Tests`; durable frame-transition fixture | Legacy baseline exists; replacement planned |
| `tests/Elsa/Activities/Runtime/Tests/WorkflowVariableSuspendResumeWriteBackTests.cs` | Replace | Resume with immutable private state and typed trigger payload; any workflow-variable assignment is a separate explicit transition. | `Elsa.Activities.Runtime.Tests` and Groundwork recovery fixtures | Legacy baseline exists; replacement planned |
| `tests/Elsa/Activities/Runtime/Tests/SeededVariableEndToEndExecutionTests.cs` | Replace | Seed lexical variable-frame defaults from alias-only definitions and prove reads/writes survive publication and execution. | `Elsa.Activities.Runtime.Tests`; canonical variable seeding fixture | Legacy baseline exists; replacement planned |
| `tests/Elsa/Activities/Runtime/Tests/ContainerScopedVariableIsolationTests.cs`, `VisibleScopeNameAccessTests.cs`, and `tests/Elsa/Activities/Sequence/Tests/ScopedVariableInputCoercionTests.cs` | Retain | Preserve isolation, lexical visibility, and coercion objectives using structured variable bindings and per-activation frames; add sibling/expired-frame rejection. | Existing Activities Runtime/Sequence test projects | Legacy baselines exist; fixture adaptations planned |
| `tests/Elsa/Activities/Flowchart/Tests/FlowchartScopedVariableTests.cs` and flowchart cyclic fixtures | Replace | Validate lexical visibility and require explicit back-edge state transfer; prohibit globally latest node output lookup. | Flowchart test project; cyclic state-transfer fixture | Legacy baseline exists; replacement planned |
| Runtime suspension/resumption and crash fixtures, including `RuntimeResumeExecutionCarrierTests.cs`, `RuntimeResumptionServiceTests.cs`, and `GroundworkDurableResumptionCrashTests.cs` | Retain | Preserve recovery and deduplication while persisting logical invocation, attempt, input snapshot, private state, committed result, and typed trigger records with explicit schema versions. | `Elsa.Workflows.Runtime.Tests` and `Elsa.Persistence.Groundwork.Tests` | Partial: typed author contracts and atomic initial suspension records pass; typed delivery/recovery and Groundwork mixed-worker coverage remain. |
| Runtime downstream scheduling fixtures, including `RuntimeDownstreamSchedulingTests.cs` and `WorkflowParentActivityCompletionSchedulerWorkHandlerTests.cs` | Replace | Schedule from the atomically committed result/outcome record; after result commit and crash, continue downstream without reactivating the completed activity. | `Elsa.Workflows.Runtime.Tests` and Groundwork crash-convergence fixtures | Legacy baseline exists; replacement planned |
| `tests/Elsa/Activities/Runtime/Tests/ActivityLibraryAcceptanceTests.cs` | Retain | Keep the library-wide acceptance objective and require every migrated activity contract to avoid memory interfaces and wrapper-shaped runtime members. | `Elsa.Activities.Runtime.Tests`; activity-library acceptance fixture | Legacy baseline exists; expansion planned |

## Expressions and ambient-access removal

| Current anchor and objective | Disposition | Replacement objective | Target project / fixture | Current status |
|---|---|---|---|---|
| `tests/Elsa/Activities/Runtime/Tests/RunJavaScriptExecutionAccessorsTests.cs`: expose ambient variables and activity outputs to JavaScript | Remove | A pure expression receives only declared, serialized parameters. Undeclared variable/output reads and mutation fail with precise capability diagnostics. | `Elsa.Expressions.JavaScript.Jint.Tests` and `Elsa.Activities.Runtime.Tests`; explicit-parameter/prohibition fixtures | Legacy baseline exists; replacement planned |
| `tests/Elsa/Activities/Scripting/Tests/RunJavaScriptExecutionTests.cs`: execute stateful script activity behavior | Replace | Model a script with effects as an activity with pinned inputs and a typed result; perform any workflow assignment through a separate explicit `Set`. | Scripting tests; script-activity/result fixture | Implemented / passing: transient DI activation, plain `script`/`arguments` inputs, frozen typed result, isolated evaluator, and no workflow writer. |
| `tests/Elsa/Activities/Runtime/Tests/WriteLineExpressionInputExecutionTests.cs`, `WriteLineExpressionScopeInputExecutionTests.cs`, `WriteLineVariableInputExpressionExecutionTests.cs`, and real-expression control-flow tests | Replace | Serialize language, source, result type alias, declared parameters, and options; bind parameters explicitly and re-evaluate only when creating a new invocation snapshot. | Activities Runtime/ControlFlow and expression-engine test projects | Partial: explicit literal/request/variable parameter materialization and restart-safe pinned snapshot pass; remaining legacy scope/control-flow fixtures await migration. |
| `tests/Elsa/Expressions/Tests/Unit/ArgumentDefinitionSerializationTests.cs` | Replace | Round trip the portable expression definition and explicit parameter-binding map without delegates, closures, runtime references, or assembly-bearing type names. | `Elsa.Expressions.Tests`; expression-definition golden fixture | Implemented / passing via `ExpressionDefinitionContractTests`; the legacy fixture remains until the deletion gate closes. |
| Expression purity and nondeterminism boundary | Replace | Reject ambient time/randomness and mutation; accept only explicit pinned inputs or an approved deterministic capability recorded in portable options. | `Elsa.Expressions.Tests` and Jint tests; purity/capability contract fixture | Implemented / passing for canonical JavaScript and Liquid binding evaluation; legacy compatibility evaluators remain separately gated pending clean removal. |

## Authored metadata, publishing, and serialization

| Current anchor and objective | Disposition | Replacement objective | Target project / fixture | Current status |
|---|---|---|---|---|
| Input-editor and activity metadata coverage, including `ActivityIoTypeRegistrationTests.cs`, `ClrAssemblyScannerTests.cs`, and design API input-option tests | Retain | Preserve editor hints, required/default metadata, stable member keys, type aliases, and collection kinds while scanning ordinary annotated CLR properties rather than wrapper-shaped properties. | `Elsa.Activities.Design.Tests` and Design API tests; plain-property scanner fixture | Legacy baseline exists; scanner adaptation planned |
| `tests/Elsa/Workflows/Design/Tests/Unit/BaselineValidatorTests/RequiredInputOutputValidatorTests.cs` and `RequiredInputOutputValidatorDerivationTests.cs` | Replace | Validate required inputs and result projections from canonical contract metadata, including producer path analysis and omitted/null/default distinctions. | `Elsa.Workflows.Design.Tests`; canonical binding validator fixture | Legacy baseline exists; replacement planned |
| `tests/Elsa/Workflows/Publishing/Api/Tests/ConstructActivityRequestHandlerTests.cs` | Replace | Construct authored state from descriptor metadata and canonical role-owned bindings without instantiating runtime memory objects. | `Elsa.Workflows.Publishing.Api.Tests`; construct-state fixture | Legacy baseline exists; replacement planned |
| `tests/Elsa/Workflows/Publishing/Api/Tests/WorkflowExecutableCompilerTests.cs` and `WorkflowExecutableCompilerGoldenTests.cs` | Replace | Lower authored literals, workflow inputs, variable reads, result projections, and expressions into one canonical executable; round trip and compare a stable golden fixture. | `Elsa.Workflows.Publishing.Api.Tests`; canonical executable golden fixture | Legacy baseline exists; replacement planned |
| Existing `TypeReference` and `Variable<T>` serialization objectives | Replace | Serialize types by stable alias plus collection kind only; prohibit assembly-bearing `typeName`; serialize variable declarations as pure metadata, never memory-backed runtime objects. | Publishing/Design serialization fixtures and `Elsa.Architecture.Tests` | Legacy behavior exists; replacement planned |
| Canonical blueprint serialization boundary | Replace | Round trip the complete canonical blueprint, including bindings and expression parameter definitions, with no delegate, closure, generator carrier, memory implementation, or importer DTO. | `Elsa.Workflows.Publishing.Api.Tests`; versioned golden fixture | Missing; planned |
| Groundwork activity-execution persistence | Replace | Version records for logical invocation, attempt, pinned input snapshot, private state, trigger, result, and transition; add upcasters, golden round trips, and mixed-version recovery before the schema is used. | `Elsa.Persistence.Groundwork.Tests`; serializer/upcaster/mixed-version fixtures | Existing store/recovery coverage; new schema fixtures planned |

## Elsa 3 importer boundary

| Current anchor and objective | Disposition | Replacement objective | Target project / fixture | Current status |
|---|---|---|---|---|
| `tests/Elsa3/Mapping/Tests/Elsa3ActivityToStateTests.cs`: map Elsa 3 input expressions into authored state | Replace | Build an importer-local producer/consumer graph and lower literals, workflow inputs, variables, activity results, JavaScript, Liquid, and loop references into canonical roles. | `Elsa3.Mapping.Tests`; representative import matrix | Implemented / passing: eight-case canonical import matrix plus parser-preservation coverage. |
| Elsa 3 output-only `memoryReference` | Replace | Resolve an output producer and its consumers, or emit a path-specific dangling/cross-subtree diagnostic; never silently drop the reference. | `Elsa3.Mapping.Tests`; output-only fixtures | Implemented / passing: output producer identity is retained through canonical result bindings. |
| Elsa 3 value containing both `expression` and `memoryReference` | Replace | Preserve both producer and consumer meaning through the importer graph or reject the ambiguous construct with an actionable path-specific diagnostic. | `Elsa3.Mapping.Tests`; combined-value fixtures | Implemented / passing: combined input/output shape lowers to a literal input and canonical activity-result consumer. |
| Elsa 3 custom, dangling, cross-subtree, loop, and cyclic references | Replace | Lower only representable graphs; reject unsupported custom references, ambiguous scope, and implicit latest-output cycles without adding a canonical compatibility path. | `Elsa3.Mapping.Tests`; invalid-reference diagnostic matrix | Implemented / passing with path-specific `VF-IMP-001` through `VF-IMP-008` diagnostics. |
| Import output cleanliness | Replace | Assert every successful imported canonical artifact contains zero legacy memory interfaces/implementations, importer DTOs, delegates, or assembly-bearing type names. | `Elsa3.Mapping.Tests` plus `Elsa.Architecture.Tests` | Implemented / passing for successful authored-state fixtures and the canonical project boundary. |
| `tests/Elsa/Architecture/Elsa3MigrationBoundaryTests.cs` | Retain | Preserve the one-way import boundary: Elsa 3 DTOs and memory terminology may exist only inside the importer and its tests; no live-instance resume or dual-read path is allowed. | `Elsa.Architecture.Tests` | Implemented / passing: canonical projects cannot reference Elsa 3; importer cannot reference Runtime/Persistence; live-state import returns `VF-IMP-009`. |

## Code-first generator and dynamic-authoring conformance

| Current anchor and objective | Disposition | Replacement objective | Target project / fixture | Current status |
|---|---|---|---|---|
| Current runtime generator scheduling coverage, including `RuntimeGeneratorContractTests.cs` and `RuntimeGeneratorEmissionSchedulerTests.cs` | Retain | Preserve runtime scheduling objectives, while keeping the activity-call source generator and its authoring carriers outside runtime artifacts. | `Elsa.Workflows.Runtime.Tests` plus architecture boundary tests | Existing baseline; boundary expansion planned |
| Generated activity-call lowering | Replace | Generate named-argument calls that lower literals, workflow request members, variables, prior results, and portable expressions into the same canonical bindings as dynamic authoring. | Source-generator test project; compile-time golden/source tests | Missing; planned |
| Generated argument semantics | Replace | Distinguish omitted, explicit `null`, and explicit default; surface compile-time diagnostics for required/type-invalid inputs. | Source-generator test project; diagnostic fixture | Missing; planned |
| Generated call-handle API | Replace | Expose node identity, whole typed result, named result projections, outputs, and control outcomes as distinct strongly typed members. | Source-generator test project; API snapshot/compilation fixture | Missing; planned |
| Generated hydration and contract metadata | Replace | Generate or consume stable member-key metadata and one-time hydration without emitting runtime `ActivityArgument<T>` wrappers or reflection-only contracts. | Source-generator and `Elsa.Activities.Runtime.Tests`; hydration conformance fixture | Missing; planned |
| Code-first versus dynamic conformance | Replace | Compile equivalent request-to-result workflows, including variables, conditional scope, child workflow, literals, results, and expressions; compare semantic canonical artifacts and validation diagnostics. | Source-generator tests and `Elsa.Workflows.Publishing.Api.Tests`; paired golden fixture | Missing; planned |
| Generator isolation | Remove | Prohibit generator assemblies, Roslyn types, generated authoring carriers, CLR delegates, and captured closures from runtime and serialized artifacts. | `Elsa.Architecture.Tests`; dependency/artifact scan | Missing; planned |

## Architecture and removal guards

| Current anchor and objective | Disposition | Replacement objective | Target project / fixture | Current status |
|---|---|---|---|---|
| `tests/Elsa/Architecture/RuntimeCoreEngineShapeGuardTests.cs` | Retain | Preserve Runtime Core engine-shape restrictions and add zero canonical references to `IMemoryBlock`, `IMemoryBlockReference`, `IMemoryRegister`, runtime `Argument`/`InputArgument`/`OutputArgument`, and generated authoring carriers. | `Elsa.Architecture.Tests` | Existing baseline; expansion planned |
| Runtime-to-Design dependency boundary | Retain | Assert Runtime and Runtime Core reference no Design, builder, catalog, source-generator, or importer assembly/type; Design may consume only stable runtime-independent metadata. | `Elsa.Architecture.Tests`; assembly-reference guard | Existing baseline; expansion planned |
| Compatibility-module exemption | Replace | Permit Elsa 3 DTO/memory terminology only in the explicitly named import module and tests; fail any reference from canonical Core, Runtime, Design, Publishing, activity packages, or persistence. | `Elsa.Architecture.Tests`; allowlist guard | Missing; planned |
| Obsolete forwarding shims | Remove | After replacements pass, assert no forwarding type, obsolete alias, service registration, or runtime adapter preserves legacy memory/argument APIs. | `Elsa.Architecture.Tests`; public API and registration scan | Missing; planned |
| Canonical package surface | Remove | Assert public and internal dependency surfaces contain zero legacy memory abstractions/implementations and zero Elsa 3 DTOs outside the importer exemption. | `Elsa.Architecture.Tests`; metadata/dependency scan | Missing; planned |

## Activation-scope benchmark evidence

The benchmark is an evidence gate, not a preselected lifetime decision. All rows remain blocked until
the real transient activation seam and intrinsic execution path exist.

| Current anchor and objective | Disposition | Replacement objective | Target project / fixture | Current status |
|---|---|---|---|---|
| No current equivalent: compare burst-only, per-attempt child scope, and safe conditional strategies | Replace | Run all three strategies against identical workflow fixtures and retain raw reproducible results. | `benchmarks/Elsa/Activities/Runtime/Benchmarks`; `contracts/activation-scope-benchmark.md` | Missing; planned, blocked by activation prototype |
| Constructor-free no-op and transient-dependency workloads | Replace | Measure operations/second, median/p95 latency, allocations/collections, activation count, and scope count. | BenchmarkDotNet activation-scope harness | Missing; planned, blocked by activation prototype |
| Scoped-disposable and transitive-scoped dependency workloads | Replace | Measure identity isolation and sync/async disposal; fail any candidate that violates its observable lifetime contract. | BenchmarkDotNet harness plus semantic assertions | Missing; planned, blocked by activation prototype |
| Intrinsic-heavy, mixed micro-activity, and I/O-bound workloads | Replace | Compare overhead while proving intrinsic-only execution creates zero CLR activity activations and zero activity child scopes. | BenchmarkDotNet harness plus intrinsic counters | Missing; planned, blocked by activation prototype |
| Retry, suspension/resumption, and concurrent-drain workloads | Replace | Verify fresh CLR objects and activation-only services, correct scoped identity, disposal, and absence of cross-execution contamination. | BenchmarkDotNet harness plus deterministic semantic fixtures | Missing; planned, blocked by activation prototype |
| Activation-scope decision record | Retain | Record environment, warm-up/sample configuration, raw results, rejected strategies, correctness failures, selected lifetime contract, and ADR amendment/supersession. | Benchmark results artifact and ADR review | Missing; planned, blocked by benchmark results |

## Specification anchors carried forward

These earlier work units are not deletion targets themselves. They identify objectives that must
remain visible while their legacy implementation model is replaced.

| Prior anchor | Disposition | Objective carried into this ledger | Current status |
|---|---|---|---|
| Spec 006, activity construction seam | Retain | Preserve opaque descriptors, registry resolution, and DI activation; replace argument-wrapper binding and dynamic bags with one-time hydration. | Covered above; replacements planned |
| Spec 011, active output and durable capture | Replace | Preserve no Design/history dependency while replacing concrete activity-execution-id and generic runtime-reference binding with structural result projections and committed invocation results. | Covered above; replacements planned |
| Spec 015, runtime execution slice | Replace | Replace the `InputArgument<T>` literal path with committed input snapshots and ordinary property hydration. | Covered above; replacement planned |
| Spec 029, materialized input and memory seeding | Replace | Preserve materialize-before-execute and durable recovery; replace memory seeding with immutable input snapshots and variable frames. | Covered above; replacements planned |
| Specs 060 and 061, output capture and input resolution | Replace | Preserve deterministic resolution/capture through typed invocation results, projections, structural producer selection, and pinned snapshots. | Covered above; replacements planned |
| Spec 081, type aliases and variable metadata | Retain | Preserve aliases and collection kinds; remove memory-backed `Variable<T>` and prohibit assembly-bearing `typeName`. | Covered above; replacements planned |
| Spec 083 and ADR 0030, expression output access/write-back | Remove | Replace ambient output access and hidden JavaScript variable write-back with explicit expression parameters or a stateful script activity followed by explicit `Set`. | Covered above; replacements planned |
| Spec 090, input editor and activity metadata | Retain | Preserve editor metadata and validation while scanning/generating contracts from plain annotated properties. | Covered above; replacements planned |

## Completion gate

The migration is complete only when:

1. every `Replace` and `Remove` row has an `Implemented / passing` successor objective;
2. every `Retain` row passes on the canonical model without a legacy memory fixture;
3. architecture guards prove that only the explicitly scoped Elsa 3 importer exemption contains
   legacy memory terminology or DTOs;
4. canonical serialization, Groundwork upcasting/recovery, importer, generator conformance, and
   expression purity suites pass; and
5. the activation-scope benchmark and semantic gates produce a reviewed lifetime decision.

Until then, legacy tests and their fixtures remain in place as baseline evidence.
