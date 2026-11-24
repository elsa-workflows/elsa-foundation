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
| 14 | `ConfigureEngine(engine.SetValue(...))`, `EvaluatingJavaScript` handler | `JavaScriptHostFunctionDeclaration("prefix.name", …)` naming the replaced Elsa 3 global | Direct when effect-free and JSON in and out; a function with effects becomes an activity. The converter rewrites direct calls of a declared global only (`lookupCustomer(x)` → `acme.lookupCustomer(x)`); a global assigned or passed as a callback is not rewritten and fails at run time, so the catalog says to make it a direct call first. The catalog also states the conversion's rules in prose: one declaration per global; the global must not clash with a generated variable or input accessor; scripts must not declare the prefix themselves; functions the converter already converts keep that conversion. |
| 15 | `ILiquidFilter` + `AddLiquidFilter`, expression descriptor providers | Liquid filters closed (`ILiquidFilter` internal); `IExpressionDescriptorProvider` and `IPortableExpressionHandler` are public | Liquid filters: redesign as a host function or an activity. A new syntax: named with its extension points, no example; Elsa 3 projects that add a whole expression language are rare, so no compiled example is built for it. |
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

**Decision**: The skill gates only on the report's integer schema version, which it lists; on any other it stops and
tells the user to update the skill. The Elsa 4 version the user targets is recorded in the plan and selects the
migration image tag. `plugin.json` carries no version, so a marketplace install follows the repository's commits.

**Rationale**: Package versions are computed per commit (`PackageVersioning.props`), so no file can name "the" tool
version, and a marketplace install follows the default branch rather than a release. Spec 1237 ships the tool and
the skill separately and versions the report by schema. The user's target version is a real choice they make once,
and it must match the engine image they go live on.

**Alternatives considered**: Stamping the tool version into `plugin.json` and the skill front matter at release:
there is no release step to do it, and it would go stale on every commit. Reading any schema version leniently:
silent misreads of a changed report.

## R-5 Where the check runs

**Decision**: `tools/skills/check_migration_skill.py` runs (self-test, then real) as its own step next to
`Check catalog and module README links` in `.github/workflows/ci.yml`. `plugins/**/*.md` joins the documentation-only
paths in `tools/ci/change_scope.py`, so a catalog edit gets the fast run. The sample's build and tests run in the PR
tier like any test project.

**Rationale**: That job runs on every PR, including documentation-only ones that skip the .NET build
(`docs/contributing/merging.md`); without the change-scope entry, a catalog typo fix would trigger every shard. An
API change is a code change, so the full build runs and breaks the sample.

**Alternatives considered**: An xunit test reading the catalog: it would not run on a documentation-only PR.

## R-6 Matching components to source

**Decision**: The skill's focused searches follow Elsa 3's naming. An activity type `Ns.Type` is found by: a class
named `Type` in namespace `Ns`; `[Activity("Ns"` on a class named `Type` (the second string argument of the common
three-argument form is the category, not the type); `Type = "Type"` in an activity attribute; or the five-argument
form `[Activity("Ns", "Type", <version>, ...)]`. A
JavaScript function by `SetValue("name"`; a variable type by `AddVariableTypeAndAlias<` with the alias; a storage
driver by the type name before the comma in its assembly-qualified name.

**Rationale**: Elsa 3 forms the type name as `{attribute namespace or CLR namespace}.{attribute type or class name}`.
