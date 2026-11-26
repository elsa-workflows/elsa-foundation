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
| 12 | `ActivityKind.Job/Task`, `RunAsynchronously` | `[BackgroundExecutionSafe]` + author-chosen placement (ADR 0093) | Ports when eligible; otherwise the activity runs in the foreground. An effect that must not repeat is keyed on `LogicalInvocationId` (or `OperationKey`), which an operator retry keeps; `InvocationId` changes on a retry. |
| 13 | Cancel signal, exceptions → incidents | Cancellation token; fault and cancel transitions | Direct; no per-activity cancel callback. |
| 14 | `ConfigureEngine(engine.SetValue(...))`, `EvaluatingJavaScript` handler | `JavaScriptHostFunctionDeclaration("prefix.<global>", …)`, matched to the Elsa 3 global by the converter's rule | Direct when effect-free and JSON in and out; a function with effects becomes an activity. The converter rewrites direct calls of a declared global only (`lookupCustomer(x)` → `acme.lookupCustomer(x)`); a global assigned or passed as a callback is refused at conversion, so the catalog says to make it a direct call first. The catalog also states the conversion's rules in prose, as they stand when it is written: how a global is matched to a host function and what makes the match ambiguous; the global must not clash with a generated variable, input or activity output accessor; scripts must not declare the prefix themselves; functions the converter already converts keep that conversion. |
| 15 | `ILiquidFilter` + `AddLiquidFilter`, expression descriptor providers | Liquid filters closed (`ILiquidFilter` internal); `IExpressionDescriptorProvider` and `IPortableExpressionHandler` are public | Liquid filters: redesign as a host function or an activity. A new syntax: named with its extension points, no example; Elsa 3 projects that add a whole expression language are rare, so no compiled example is built for it. |
| 16 | `AddVariableTypeAndAlias<T>(alias)` | `IVariableTypeDescriptorProvider` with a dotted alias | Direct. The converter looks the Elsa 3 name up as an Elsa 4 alias or as a registered type's full name, then as an assembly-qualified CLR name; Elsa 4 refuses a module alias without a dot, so a bare Elsa 3 alias (the class name when none is given) never matches. The catalog says so: the user changes the variable's type in the Elsa 3 definition to the Elsa 4 alias, or keeps it untyped (Any) and records the component as dropped. No converter rule maps bare aliases. |
| 17 | `IStorageDriver` + `AddStorageDriver`; the memory driver | The converter always converts to the default storage, with a Reconsider finding for the memory driver and custom drivers; Elsa 4 accepts another driver only on a reusable activity's top-level inputs, outputs and graph variables, and only `elsa.json` ships | Interview, once per driver type. A driver choice expresses a goal, and the repository's Elsa 3 storage-driver migration guide gives the answers the catalog restates: a secret → Elsa 4 secret references; bulky data → fetch, use and drop it inside one activity, or keep a reference; another store such as blob storage → not possible per variable today, so the user writes their own plan or spec; no particular reason → the default storage. The catalog carries the interview itself (the skill folder is self-contained) and links the guide for further reading. |
| 18 | `FeatureBase` + `UseXxx()` | `[ShellFeature]` `IShellFeature` in the user's project, referenced and listed by the migration host | Direct. |
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
tells the user to update the skill. The tool runs from the user's clone of this repository, and the plan records the clone's path and commit. `plugin.json` carries no version, so a marketplace install follows the repository's commits.

**Rationale**: Package versions are computed per commit (`PackageVersioning.props`), so no file can name "the" tool
version, and a marketplace install follows the default branch rather than a release. Spec 1237 ships the tool and
the skill separately and versions the report by schema. The tool and the migration host run from the user's clone, so its commit is what the
user migrates against and tries the result on. This is a temporary development setup: the production go-live comes
with an Elsa 3 migration app profile once Elsa 4's distribution channels are decided.

**Alternatives considered**: Stamping the tool version into `plugin.json` and the skill front matter at release:
there is no release step to do it, and it would go stale on every commit. Reading any schema version leniently:
silent misreads of a changed report.

## R-5 Where the check runs

**Decision**: `tools/skills/check_migration_skill.py` runs (self-test, then real) as its own step next to
`Check catalog and module README links` in `.github/workflows/ci.yml`. `plugins/**` joins the documentation-only
paths in `tools/ci/change_scope.py`, so a catalog edit gets the fast run. The sample's build and tests run in the PR
tier like any test project.

**Rationale**: That job runs on every PR, including documentation-only ones that skip the .NET build
(`docs/contributing/merging.md`); without the change-scope entry, a catalog typo fix would trigger every shard. An
API change is a code change, so the full build runs and breaks the sample.

**Alternatives considered**: An xunit test reading the catalog: it would not run on a documentation-only PR.

## R-6 Matching components to source

**Decision**: The skill's focused searches follow Elsa 3's naming. An activity type `Ns.Type` is found by: a class
named `Type` in namespace `Ns`; `[Activity("Ns"` on a class named `Type` (the second string argument of the common
three-argument form is the category, not the type); `Type = "Type"` in an activity attribute; or any form whose third
positional argument is an integer version, such as `[Activity("Ns", "Type", 2)]`. A
JavaScript function by `SetValue("name"`; a variable type by the alias in `AddVariableTypeAndAlias<`/`AddTypeAlias<` (with no alias argument, the alias
is the class name); a variable type registered without an alias (`AddVariableType<`, `AddVariableTypes(`) appears in
the export under its assembly-qualified name, so it is found, like a storage driver, by the namespace and class
before the comma; a storage
driver by the type name before the comma in its assembly-qualified name.

A component is an Elsa 3 built-in, recorded as such and not searched for, when it is a storage driver or activity type whose namespace starts with `Elsa.` (Elsa 3's own), or a JavaScript function that case 14 lists as one Elsa 3 defined itself (the names the
Elsa 3 mapping README gives for functions a host function may take over: the configuration and definition readers,
the last-result reader and the byte and stream helpers). These are names, not diagnostic codes.

**Rationale**: Elsa 3 forms the type name as `{attribute namespace or CLR namespace}.{attribute type or class name}`.
