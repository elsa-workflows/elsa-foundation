# Extension points — Activities.Runtime domain

The per-domain catalog (framework §2.22.1). Anchored at `Elsa.Activities.Runtime` — the composition root where `ActivitiesRuntimeFeature` registers the activity construction factory, the descriptor-type → constructor registry, the single aggregating `RegisterActivityConstructors` handler, and the startup task that drives the Registry + StartUp Task pattern.

> Carries **no** `Elsa.*.Design.*` dependency (Elsa §E2.2). Construction is discriminated by the descriptor type's `FullName`, not a `Kind` string.

---

## Implementable contributor interfaces

### `WorkflowInvokeActivitySchedulerWorkHandler` *(Activities Runtime — `Elsa.Activities.Runtime`)*
- **Kind:** Scheduler work contributor.
- **Register:** `ActivitiesRuntimeFeature` registers it as an `IWorkflowSchedulerWorkHandler`.
- **Usage:** handles `WorkflowExecutionCommandKind.InvokeActivity` work by constructing an activity from the runtime-owned executable node descriptor through `IActivityFactory`, invoking `CanExecuteAsync`/`ExecuteAsync`, and recording the targeted `ActivityExecutionState` as completed or faulted. Composite activities may request child executable-node scheduling through the runtime activity execution context; generic workflow-level edge traversal remains outside this handler. It does not load Design-owned authored workflow models.

### `WorkflowParentActivityCompletionSchedulerWorkHandler` *(Activities Runtime — `Elsa.Activities.Runtime`)*
- **Kind:** Scheduler work contributor.
- **Register:** `ActivitiesRuntimeFeature` registers it as an `IWorkflowSchedulerWorkHandler`.
- **Usage:** handles `ParentCompletionEvaluation` completion work by reconstructing the running parent activity and invoking `IActivityChildCompletionHandler` when implemented. The handler enqueues child `ScheduleActivity` work requested by the parent or completes the parent activity when the parent requests composite completion. It does not interpret workflow-level edges or load Design-owned authored workflow models.

### `ResumeTargetAttribute` *(Core — `Elsa.Activities.Runtime.Core`)*
- **Kind:** Declaration surface (activity author contract).
- **Signature:** `[ResumeTarget("stable-resume-target-id")]` on an activity handler method.
- **Usage:** declares a stable runtime resume target ID. Workflow compile/publish can copy the ID into a runtime executable artifact's resume-target table. Durable bookmarks store this ID, not the C# method name.
- **Related runtime seam:** `IBookmarkResumeResolver` in `Elsa.Workflows.Runtime.Core`.

### `IActivityConstructor<TDescriptor>` *(Core — `Elsa.Activities.Runtime.Core`)*
- **Kind:** Contribution (one constructor per descriptor type).
- **Signature:** `string DescriptorType { get; }`; `ValueTask<IActivity> Construct(TDescriptor descriptor, IDictionary<string, InputArgument>?, IDictionary<string, OutputArgument>?, CancellationToken)` (with the non-generic `IActivityConstructor` bridge that owns `payload.Deserialize<TDescriptor>()`).
- **Register:** `services.AddSingleton<IActivityConstructor, MyConstructor>()`.
- **Aggregated by:** the single `RegisterActivityConstructors : IEventHandler<OnActivityConstructorsInitializing>` (this feature), which collects every registered constructor and adds it to the registry. The registry enforces one-constructor-per-`DescriptorType` (throws on a duplicate).

**Known implementations (shipped):**
- `Elsa.Activities.Primitives` — `ClrActivityConstructor` *(descriptor type `Elsa.Primitives.Models.TypeInformation`; the default/primitive CLR kind)*
- `Elsa.Activities.Composition.Runtime` — `WorkflowActivityConstructor` *(descriptor type `Elsa.Workflows.Primitives.Models.WorkflowIdentity`; the Workflow kind)*

### `IActivityChildCompletionHandler` *(Core — `Elsa.Activities.Runtime.Core`)*
- **Kind:** Activity-owned continuation handler.
- **Signature:** `ValueTask OnChildCompletedAsync(ActivityChildCompletedContext context)`.
- **Usage:** implemented by composite activities that own child-completion routing semantics. The runtime invokes it only for parent-completion evaluation work after reconstructing the parent activity from the pinned executable artifact.

**Known implementations (shipped):**
- `Elsa.Activities.Flowchart` — `Flowchart` *(routes completed children through Flowchart-owned slot metadata)*

---

## Events

`CatalogParityTests` scans the `Elsa.Activities.Runtime.Core` assembly, paired with this catalog file, for `IEvent` types.

### OnActivityConstructorsInitializing
`(ICollection<IActivityConstructor> Constructors)`

**Semantic.** The activity constructor registry is initialising. Every registered `IActivityConstructor` is contributed to the `Constructors` collection, then flushed into the registry.

**Delivery strategy.** Sequential — all constructors must be registered before the first activity construction.

**Publication site.** `ActivityConstructorsStartupTask` (`Elsa.Activities.Runtime`) — fired once at startup.

**Expected handler.** Exactly one: `RegisterActivityConstructors` (this feature).

---

## Cross-references

- Repo-wide index: [`../../EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.1 + §2.22.1; Elsa §E2.2 (no Runtime → Design dependency).
