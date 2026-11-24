# Research: Elsa 3 to Elsa 4 migration skill and documentation

All facts are from the initiative branch at the plan date and from Elsa 3.6.3 source.

## R-1 Where the canonical skill lives

**Decision**: One self-contained folder, `plugins/elsa3-migration/skills/elsa3-workflows-migration/`, that is the
canonical skill and the Claude Code plugin's only skill.

**Rationale**: A Claude Code plugin installed from a marketplace is copied into a cache with only its own folder,
and a user's `.agents/skills/` copy lives in their own workspace. An adapter that says "read
`website/docs/…/README.md`" finds no such file on the user's machine. The canonical files must travel with whatever
the user installs or copies. The open agent-skill format (`name` and `description` front matter, then markdown) is
read by several agents and renders as an ordinary document on GitHub.

**Alternatives considered**:
- Canonical in `website/docs/` (published on the Elsa 4 site) with a plugin copy kept equal by a CI check:
  duplication plus a new guard.
- Canonical in `website/docs/`, adapters fetch the published pages at run time: a network call, and the skill and the
  tool versions drift apart.

## R-2 Catalog verdicts against the code

**Decision**: The FR-014 table. Evidence per case (Elsa 3.6.3 → Elsa 4):

| # | Elsa 3 | Elsa 4 | Why this verdict |
|---|--------|--------|------------------|
| 1 | `Activity`, `CodeActivity`, `CodeActivity<T>`, `Trigger` | `Activity`, `Activity<TResult>`, `StatefulActivity<…>` | Direct counterpart; the result is one record instead of output slots. |
| 2 | `[Activity(ns, type)]` or CLR namespace + class → JSON `type` | Catalog key = CLR full name; `[ReplacesActivityType]` | The resolver tries the Elsa 3 type verbatim as a catalog key, so matching names need no marker. |
| 3 | `Input<T>` + `[Input(DefaultValue, UIHint, Options, UIHandler…)]` | Plain property + `[ActivityInput]`, `[Required]` | Direct; `UIHandler`/`DefaultValueProvider` become `OptionsProvider` or a static default, checked per input at implementation. |
| 4 | `Output<T>`, `Result` | `[Output]` on result record properties | Direct. |
| 5 | `CompleteActivityAsync`, auto-complete | `ActivityTransition.Complete(...)` | Direct. |
| 6 | `[FlowNode(...)]`, `CompleteActivityWithOutcomesAsync` | `[ActivityOutcome]`, outcome in the completion | Direct. |
| 7 | `[Port]` + `ScheduleActivityAsync` | Structural activities are engine-only | No author surface: redesign as a reusable Activity Graph definition or built-in composites. |
| 8 | `CreateBookmark`, `SendSignalAsync`/`OnSignalReceived` | `StatefulActivity` suspend + `[ResumeTarget]`; signals engine-only | Bookmarks port; signals redesign. |
| 9 | `Trigger.GetTriggerPayload`, `IsTriggerOfWorkflow` | `[TriggerActivity]` stateful trigger + trigger stimulus provider | Direct, with one extra registration. |
| 10 | `context.GetRequiredService<T>()`; no constructor injection | Constructor injection only | Inverted; direct port. |
| 11 | `Variable<T>.Get/Set`, `GetVariable`, `WorkflowExecutionContext.Properties` | `VariableReference<T>` + `context.Variables`; `[AccessesUndeclaredVariables]`; `[AccessesWorkflowState]` | Ports, except the properties bag, which has no counterpart. |
| 12 | `ActivityKind.Job/Task`, `RunAsynchronously` | `[BackgroundExecutionSafe]` + author-chosen placement (ADR 0093) | Ports when eligible; otherwise the activity runs in the foreground. |
| 13 | Cancel signal, exceptions → incidents | Cancellation token; fault and cancel transitions | Direct; no per-activity cancel callback. |
| 14 | `ConfigureEngine(engine.SetValue(...))`, `EvaluatingJavaScript` handler | `JavaScriptHostFunctionDeclaration("prefix.name", …)` | Direct when effect-free and JSON in and out; a function with effects becomes an activity. |
| 15 | `ILiquidFilter` + `AddLiquidFilter`, expression descriptor providers | Liquid filters closed (`ILiquidFilter` internal) | Redesign as a host function or an activity. |
| 16 | `AddVariableTypeAndAlias<T>(alias)` | `IVariableTypeDescriptorProvider` with a dotted alias | Direct. |
| 17 | `IStorageDriver` + `AddStorageDriver` | Publication refuses non-default variable storage | Redesign: default durable storage. |
| 18 | `FeatureBase` + `UseXxx()` | `[ShellFeature]` `IShellFeature` in a Nuplane package, enabled in `appsettings.json` | Direct. |
| 19 | `Elsa.Testing.Shared` `ActivityTestFixture` | No shipped helper; build the context, call `IActivity.ExecuteAsync` | Direct, with a few more lines. |

**Alternatives considered**: Porting composites (7) or signals (8) through the engine-only protocol: it is documented
as not an author surface and would break on engine changes. Emulating storage drivers (17) inside an activity:
a workaround that hides a semantic change; refuse with guidance instead.

## R-3 Keeping the catalog's examples correct

**Decision**: A compiled synthetic sample (`Acme`) implements every *port* case; each catalog "after" example is a
marked region of it; a check compares them.

**Rationale**: Specs 1230, 1231 and 1234 changed the activity authoring API within weeks. A wrong "after" example is
copied by every AI that follows the catalog. Only a build catches that without relying on people.

**Alternatives considered**:
- Widen the contributor trigger to activity API changes, no build check: relies on someone noticing.
- Converter trigger only, examples checked by hand once: drift is found by users.

## R-4 Versions

**Decision**: `plugin.json` `version` and `SKILL.md` `metadata.cli-version` carry the `dotnet-elsa` version the skill
is released with; `SKILL.md` names the report schema versions it reads. On another schema version the skill stops
and names the matching release.

**Rationale**: The skill and the report are released together; the cheapest safe behaviour on a mismatch is to stop.

**Alternatives considered**: Reading any schema version leniently: silent misreads of a changed report.

## R-5 Where the check runs

**Decision**: `tools/skills/check_migration_skill.py`, in the CI step that runs the Python documentation guards, so a
documentation-only change to the catalog runs it. The sample's build and tests run in the PR tier like any test
project.

**Rationale**: A documentation-only PR skips the .NET build (`docs/contributing/merging.md`); the check is pure text
and must still run. An API change is a code change, so the build runs and breaks the sample.

**Alternatives considered**: An xunit test reading the catalog: it would not run on a documentation-only PR.

## R-6 Matching components to source

**Decision**: The skill's focused searches follow Elsa 3's naming: an activity type `Ns.Type` is found by
`[Activity("Ns", "Type"` or `[Activity("Ns"` on a class named `Type`, or by namespace `Ns` with class `Type`; a
JavaScript function by `SetValue("name"`; a variable type by `AddVariableTypeAndAlias<` with the alias; a storage
driver by the type name before the comma in its assembly-qualified name.

**Rationale**: Elsa 3 forms the type name as `{attribute namespace or CLR namespace}.{attribute type or class name}`.
