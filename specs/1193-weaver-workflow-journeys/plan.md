# Implementation Plan: Weaver workflow authoring and inspection journeys

**Branch**: `agent/claude/3442-weaver-workflow-journeys` | **Date**: 2026-11-03 | **Spec**: [spec.md](spec.md)

**Status**: Approved for task generation after an independent planning review on 2026-11-03. Its verified findings are incorporated:
- read evidence bound to the recording reader instance;
- owner-unavailable codes propagated through the Agent tool invoker;
- a synchronous SHA-256;
- an autosave hold keyed to history lineage;
- Core contracts for every cross-domain read and command (D4);
- the permissions attachment;
- re-admitted context kinds;
- tenant- and subject-scoped operation keys;
- a separate Runtime-only test project;
- full traceability.

**Implementation base**: main `e190ab6ce`, which contains #3440 (`95e4e9610`), #3441 (`364e636c9`) and the workflow library bulk delete (#3653).

## Summary

Deliver the spec as seven sequential slices. Each slice is its own PR that says "Refs #3442". Each slice is reviewed to convergence, proven on its own and merged before the next one starts. The last slice closes #3442.

| Slice | Scope | Stories | Main packages |
| --- | --- | --- | --- |
| A | Read-path hardening: the unavailable outcome and fewer duplicate reads | 5 | Workflows Design Api, Agent Workflows |
| B | Truth seams: the real catalog, revision identity and permissions, and removal of the legacy placeholders | 4 | Agent Workflows |
| C | Draft authoring: the Operation Batch tool, designer applier, history, autosave, risk and clarification | 2, 3 | Agent Workflows, Studio Workflows, Weaver Workflows |
| D | Explain and troubleshoot: property, expression and credential context, and the dangling prompt and renderer | 1 | Agent Workflows, Studio Workflows, Weaver Workflows |
| E | Library, create-dialog intent and templates | 6, 7 | Workflows Design Api and EF, Agent Workflows, Studio Workflows |
| F | Versions and Executables, read-only, and the new Runtime-side package | 8 | Agent Workflows, Agent Workflows Runtime |
| G | One exact Run, read-only, with redaction | 9 | Agent Workflows Runtime, Studio Workflows |

Each slice adds its own conformance-probe cases on both provider adapters, so slice H from the scoping brief is folded into every slice. The last slice runs the full package conformance witness and the composed Studio suite on its final source.

## Technical Context

**Language/Version**: C# on .NET 10; TypeScript and React; Python 3 for the conformance driver.

**Primary Dependencies**:
- Agent Core and Agent, the Agent API and the actual GitHub Copilot and Anthropic adapters, all reused unchanged except where a slice says so;
- Workflows Design Core, Api and Persistence, including the EF library store;
- Workflows Runtime and Execution Evidence, for slices F and G only;
- Studio Workflows and Weaver Workflows, and the Studio public SDK.

**Storage**: No new persisted revision field. Slice E adds one nullable, uniquely indexed operation-key column on the library tag and view rows, with migrations for SQLite, SQL Server, PostgreSQL and MySQL.

**Testing**:
- xUnit behaviour tests for each server contribution and registration;
- the existing authorization boundary tests, plus a read-count regression;
- Vitest for the Studio adapters, including the armed history capture timer;
- Playwright journeys in the normal host;
- conformance-probe cases on both adapters, with negative controls;
- EF provider-contract tests for the operation-key index.

**Constraints**:
- Weaver core, the Studio host and the provider adapters gain no workflow, template or library knowledge (FR-030).
- The shipped session policy, review binding, receipts, uncertainty, client completion and recovery are reused unchanged. The one Agent-core change is slice A's: the tool invoker and context admission keep an owner's *unavailable* code, instead of collapsing it into `agent.tool.forbidden`.
- Agent packages couple to other domains only through `.Core` contracts (framework §2.1, D4).
- No release, deployment, provider purchase or live-provider credential.

## Constitution Check

| Gate | How this plan meets it |
| --- | --- |
| Module ownership (§2.3) | Every contribution lives in its owning package (FR-030). Executable and Run support lives in a new Runtime-side package, so `Elsa.Agent.Workflows` keeps referencing Design only. |
| Replacement contracts (§2.6.2) | The plan removes the legacy contracts rather than keeping placeholders. The one new replacement-contract surface is the operation-key overload on `IWorkflowLibraryStore`, which keeps the store's existing single-registration rule. |
| Extension-point catalogs (§2.22.1) | Each slice updates the catalogs it touches in the same PR (FR-034). |
| SemVer (§4.2, ADR 0080 D4) | Listed per slice under [Public surface](#public-surface-and-semver). |
| Internal visibility (ADR 0080 D3) | New implementations are `internal sealed`. |
| Tests (§3) | Feature registration and behaviour tests per slice. Failure paths are covered for every refusal and unavailable outcome. |
| Evidence categories (FR-037) | Component, deterministic provider-protocol native, and live-provider (none claimed), kept separate in every PR. |

## Decisions

The spec leaves three decisions to the plan (D1–D3). The planning review added D4.

### D1. The authorization-unavailable outcome

A `WorkflowDefinitionAuthorizationException` maps to HTTP 503 with the stable code `workflow.authorization.unavailable`. The body keeps the design error shape. The top-level `message` stays "One or more errors occurred!", as in every design body. The exception's fixed text, "Workflow definition authorization could not be completed.", is the `generalErrors` entry, and it carries no cause. Consumers read `errorCode`, never the message.

The design error body gains an optional `errorCode` property, written only when a code is set, so every existing body is unchanged. The name matches the Publishing API's `errorCode`. The design compatibility cases pin no error bodies, so the 503 shape and the unchanged 403 and 404 bodies are pinned byte for byte by `DefinitionReadAuthorizationTests`. The 503 is written by a new keyed Design `IEndpointFaultRenderer`, as Publishing and Runtime do, because `EndpointProblem` carries no code. The writer's shape stays in one place.

Weaver workflow tools and context admission get the same code. `WorkflowResourceAuthority` catches the exception and returns a decision carrying `workflow.authorization.unavailable`.

Today `AgentToolInvoker` turns every refusal into `agent.tool.forbidden`, and `CanDiscloseAsync` returns a bool. Slice A therefore makes the smallest Agent-core change that keeps an owner-unavailable code, with a 503-class outcome and the fixed message. Every other denial is unchanged. The existing `agent.workflow.authority_unavailable` keeps its meaning, a composition fault with zero or several authorizers.

### D2. The Runtime-side package

The new package is `Elsa.Agent.Workflows.Runtime`, at `Extensions/Agent/src/Workflows/Runtime/`, with the shell feature `FoundationWorkflowsRuntimeAgent`. The architecture guard's domain-tree convention (`Project_paths_match_domain_tree_convention`) derives that path from the name, and makes `Workflows/` a container, so slice F also moves `Elsa.Agent.Workflows` to `Extensions/Agent/src/Workflows/Elsa.Agent.Workflows/` (its package and assembly names are unchanged) and puts the tests at `Extensions/Agent/tests/Workflows/Runtime/Tests/`. It references Agent, Agent Core and `Elsa.Workflows.Runtime.Core` only (D4), and never Design. Its tests live in a new `Elsa.Agent.Workflows.Runtime.Tests` project, which composes no Design assembly, so the Runtime-only proof holds AppDomain-wide. The deployment shapes are proven as follows (SC-009): the started Runtime-only host (`WorkflowRuntimeOnlyHostTests`) activates a real CShells shell with only `WorkflowsRuntimeApi` and `FoundationWorkflowsRuntimeAgent`, reads a seeded Executable from the runtime's own stores through the real `WorkflowExecutableInspection`, asserts that no `Elsa.Workflows.Design*` assembly (nor `Elsa.Agent.Workflows`) is loaded, and refuses draft and version references as `agent.context.kind_unsupported`; the started Design-only host (`WorkflowDesignOnlyHostTests` in `Elsa.Agent.Tests`) activates a shell with `WorkflowsDesignApi` and `FoundationWorkflowsAgent`, reads a version through the real `WorkflowVersionReader` and refuses the Executable reference the same way; the combined shape is composed by `WorkflowDeploymentShapeTests` and by the Workbench (`shells.json`, `shells.baseline.json` and the Docker compose shells), and is exercised natively by the conformance probe, whose packaged host also deselects each agent feature in turn for the absence controls. The package is added to the package conformance consumers.

### D3. The legacy contracts in `Elsa.Agent.Workflows`

All seven public legacy contracts have no production consumer. Only `Elsa.Agent.Tests` uses them. Each is removed, together with its default implementation and its registration. This is a Major change, classed as pre-GA cleanup.

| Legacy contract | Default implementation | Disposition |
| --- | --- | --- |
| `IWorkflowAgentContextProvider` | `DefaultWorkflowAgentContextProvider` | Removed as a public contract. The provider stays as the module's `IAgentContextProvider` contribution, made `internal sealed`. |
| `IWorkflowActivityCatalogProvider` | `DefaultWorkflowActivityCatalogProvider` (empty) | Removed. An internal catalog reader over the Design activity catalog replaces it (FR-003). |
| `IWorkflowRevisionProvider` | `DefaultWorkflowRevisionProvider` (`unknown`) | Removed. Revision identity comes from the disclosed context (FR-005). |
| `IWorkflowChangePermissionEvaluator` | `DenyAllWorkflowChangePermissionEvaluator` | Removed. `WorkflowResourceAuthority` over `IWorkflowDefinitionAuthorizer` is the only permission path (FR-004). |
| `IWorkflowChangeProposalService` | `DefaultWorkflowChangeProposalService` (unsupported) | Removed. Proposals come from Agent's own tool-invocation review. |
| `IWorkflowGraphOperationBatchRiskClassifier` | `DefaultWorkflowGraphOperationBatchRiskClassifier` (direct apply) | Removed. An internal classifier that never grants direct apply replaces it (FR-012). |
| `IWorkflowAuthoringAuditService` | `DefaultWorkflowAuthoringAuditService` | Removed. Agent's tool-invocation audit already records every workflow tool invocation and outcome. |

The models in `WorkflowAgentModels.cs` that only these contracts use are removed with them. A missing owning service fails explicitly: the context provider and every tool resolve `IWorkflowDefinitionAuthorizer` and the activity catalog through `WorkflowResourceAuthority`, which already refuses a missing or ambiguous authorizer.

### D4. Cross-domain reads and commands go through `.Core` contracts

Framework §2.1 lets features couple only through `.Core` contracts or inheritance. Every Agent project references only `.Core` packages from Essentials. Where the owning read or command logic lives in an `.Api` package today, the owning domain gains a narrow `.Core` contract that its `.Api` implements by delegating to the existing service. Each such contract is a Minor, additive replacement contract with a single-registration guard and a catalog entry.

| Contract | Owning `.Core` | Implemented by | Slice |
| --- | --- | --- | --- |
| `IActivityAuthoringCatalog` (the actor-gated catalog, with Core models for entries and inputs) | `Elsa.Activities.Design.Core` | `Elsa.Activities.Design.Api`, over `IActivityAuthoringCatalogReader` | B |
| `IWorkflowLibraryCommands` (tag assignment with tag creation, favorite, folder move, view save and share, each with explicit actor, tenant and per-target outcomes) | `Elsa.Workflows.Design.Core` | `Elsa.Workflows.Design.Api`, extracted from the inline endpoint logic in `WorkflowLibraryEndpoints.cs`. A replacement `IWorkflowLibraryCommands` decides Weaver's library writes; the HTTP endpoints keep the built-in decisions (amended in slice E review: routing the endpoints through a replacement drifted from the routes' own validation and errors) | E |
| `IWorkflowVersionReader` (exact version details) | `Elsa.Workflows.Design.Core` | `Elsa.Workflows.Design.Api`, over `IWorkflowVersionDetailsReader` | F |
| `IWorkflowExecutableInspection` and `IWorkflowRunInspection` (an exact Executable, an exact Run with its incidents and activity executions, redacted more strictly than the run page (sensitive values never released; incident messages and state views of activities with sensitive inputs withheld; see Slice G), with unavailable reasons) | `Elsa.Workflows.Runtime.Core` | `Elsa.Workflows.Runtime.Api`, over `IWorkflowExecutableInspector` and the instance, incident and activity-execution services | F, G |

A missing contract fails explicitly. The Agent tool or context reports an unavailable outcome that names the missing capability, in the same way `WorkflowResourceAuthority` treats a missing authorizer.

## Slice A: Read-path hardening

**Server, Workflows Design Api**:
- A new keyed `WorkflowDesignFaultRenderer` maps `WorkflowDefinitionAuthorizationException` to 503 with D1's code, ahead of the translator, in the same way that Publishing's `WorkflowPublishingFaultRenderer` writes `errorCode`. `EndpointProblem` carries no code. The shared design writer assembles the body in one place, and `WorkflowDesignError` gains the optional `errorCode`.
- The definition GET and the built-in authorizer stop repeating work. The built-in `WorkflowDefinitionDetailsReader` records request-scoped read evidence at the end of a successful `ReadAsync`: the definition identity, the tenant, and the fact that its permission, store and organization checks ran. The built-in `HttpWorkflowDefinitionAuthorizer` accepts that evidence only when the same internal reader recorded it in the same request scope, for the same definition and tenant. It then skips its own repeated Read permission check, store lookup and `AuthorizeReadAsync`. A manage decision still runs the Manage permission check.
- The evidence records the reader instance that produced it. The authorizer accepts it only when `ReferenceEquals(currentReaders[0], recordingReader)`. A replacement reader that delegates `ReadAsync` to the built-in reader therefore still has its own `AuthorizeReadAsync` called, and cannot stand in for the authorizer. A replacement authorizer is unaffected.
- When no library store is registered, the built-in reader runs no permission or organization check. Its evidence then records existence only, so the authorizer still runs its own Read check and skips only its repeated store lookup. The tenant is compared with the stored definition.
- The endpoint's own `RequireAsync` stays, because it orders the permission refusal before the not-found result when no library store is registered.
- The result on a built-in GET with a library store: six Read decisions, four folder decisions, two store reads and two organization lookups become three, two, one and one. Without a library store, two store reads become one.

**Server, Agent Workflows**: `WorkflowResourceAuthority` catches `WorkflowDefinitionAuthorizationException` and returns D1's unavailable outcome. `WorkflowReadGraphTool`, `WorkflowSetDesignerPositionTool` and the context provider surface it unchanged.

**Tests**:
- translator tests for unavailable versus denied, with no cause in the body;
- the boundary tests in `WorkflowDefinitionAuthorizationBoundaryTests` for visibility, organization, actor, tenant and missing definitions, with no change;
- a read-count regression with counting decorators around the permission service, the definition store and the library store;
- a mutation test that a replacement reader cannot produce evidence;
- tool tests for the unavailable outcome.

- **Catalog**: the authorizer and details-reader failure semantics in `Essentials/src/Workflows/Design/Api/EXTENSION_POINTS.md`, and the unavailable propagation in `Extensions/Agent/src/Elsa.Agent/EXTENSION_POINTS.md`.
- **Browser**: a Playwright fixture journey that runs the editor's real load path and error banner against the design API's exact 503 and 403 bodies. Neither Workbench nor the composition probe has a seam to make the authorizer throw. The native evidence is therefore the probe case, whose test-only seam lives in the probe's backend consumer, never in product code.
- **Provider surface**: on Copilot the function return carries the stable code. On Anthropic the refused tool result carries the fixed "unavailable" text, distinct from "forbidden", because the engine's refused-tool result carries only its message.

**Probe**: `workflow-authorization-unavailable` on both adapters, with a throwing authorizer registered through the probe host's test seam, against both the definition API and the Weaver tools.

## Slice B: Truth seams

- Remove the seven legacy contracts and implementations (D3).
- Add `IActivityAuthoringCatalog` (D4) and consume it from an internal catalog reader. It reads with `Availability = All`, gated by the actor's `ActivityDesignPermissions.Read` in the request tenant, which is the designer's own gate ([R1](research.md#r1-activity-catalog-slice-b)). Its entries carry type, version, display metadata, `Available` and declared inputs, including `isCredential`. `Elsa.Agent.Workflows` gains a reference to `Elsa.Activities.Design.Core` only.
- `CollectAsync` contributes two new server-produced attachments: `workflow.catalog` v1 and `workflow.permissions` v1. Both are added to the descriptor's `ContextKinds`, so `AdmitDisclosureAsync` admits them. Both are recomputed and re-admitted on every turn. A changed catalog or permission set produces a new snapshot, under the session's disclosure limits. The hardcoded `Permissions(false,false,["workflow.explain"])` lives in the removed `GetContextAsync` and goes with it. Tool availability follows the permissions attachment.
- Revision identity follows FR-005. The `workflow.definition` context kind already carries the target, the content revision and the editor generation. Slice C strengthens the content revision.
- A registration test resolves the composed host and asserts that no placeholder type is present.
- **Normal host**: a Playwright journey in which a read-only actor sees explain available and edit refused.
- **Probe**: `workflow-truth-seams`: a tenant-restricted activity, a read-only actor and a manage actor, with the catalog and permissions compared with the owning design services' answers.

## Slice C: Draft authoring

**Server, Agent Workflows**:
- A versioned client working-state tool, `workflow_apply_operation_batch` v1, with strict per-operation JSON schemas (`additionalProperties: false`) for add, set-root, position, set-property, update, remove, connect and disconnect. Update allowlists `displayName`, `description` and `canStartWorkflow`. `name` is not offered: neither the designer's `ActivityNode` nor the backend node has a name field, and an activity's visible name is its `displayName` presentation record. Set-property requires an explicit property name and a target activity. Add requires an explicit scope path and slot.
- Preparation refuses everything the designer applier would refuse, so an approved batch never fails only at application: at most one set-root per batch, no add into or connection within an activity the same batch adds (the catalog does not disclose a new activity's slots), no add into or removal from a BPMN scope, and no new id that the document still holds in a layout or presentation record. A shared case file (`workflowBatchConformance.json`) runs through both the server preparation and the applier.
- The snapshot carries the structure in a compact form (activity indexes by version and by container slot), so a draft that fits the 64 KiB snapshot bound without it still fits; Studio leaves the structure out when the content would come within 2 KiB of the bound.
- Preparation validates each operation against the actor's authorized catalog and the activity's declared inputs. It refuses or clarifies, naming the failing operation (FR-013), and refuses credential literals (FR-017). It classifies risk with `AgentRisk.ReviewRequired`, or `Destructive` for remove and disconnect, and the batch takes the highest. It returns `RequiresApproval` in every mode.
- The existing `workflow_set_designer_position` tool stays, with its strict receipt.

**Studio, Studio Workflows**:
- The client tool handler accepts the new batch and maps it onto the existing `workflowGraphOperationBatch.ts` applier, extended with the same strict schemas: unknown fields are rejected, there is no default property name, and type, version and children are never rewritten.
- `compareAndReplaceDraftByBatch` compares the definition, the strengthened content revision, the editor generation and the selection.
- The content revision becomes SHA-256 over the canonical working-state signature. It is computed synchronously by a small, module-owned TypeScript implementation, tested against the NIST vectors. The compare-and-replace runs inside a synchronous reducer, and `crypto.subtle` is missing on non-secure origins, so Web Crypto cannot be used there. The 32-bit hash stays only for change detection.
- Before applying, the handler flushes pending history capture from the latest committed draft, read from a ref rather than the render-time value. It then pushes the batch result explicitly as its own entry, so a hand edit within the capture delay is never merged into the batch entry. The review card's Undo calls designer-history Undo, and is disabled with a reason once a later edit exists.
- Autosave suspension is keyed to history lineage, not to an index, so the 50-entry cap cannot shift it. Each history entry carries the set of batch identities applied in its line, inherited by every later entry. Autosave is held while the current entry's set contains an unsaved batch. Undo to an entry before the batch releases the hold. Redo holds it again, and later manual edits inherit the set. An explicit save marks the batches it covers as saved.
- After applying: normal validation, dirty marking, the scope-frame and selection rules of FR-008, the test-run result and published-artifact association cleared, and a receipt issued for the originating client binding before it rotates.
- Studio rechecks the server's risk classification against live working state. A disagreement fails closed into review or clarification.

**Studio, Weaver Workflows**: a `workflows.batch-result` renderer with each operation's risk label, registered under its own module identity for the Workflows module's results.

**Studio host and SDK** (generic, no workflow knowledge): a result renderer may declare `resourceModuleIds`, the resource modules whose results it presents, so a renderer contributed by another package is matched by resource module rather than by its own `moduleId`; and `api.agent.resultControls` lets the resource-owning module contribute an interactive control for a result type, which the host renders after whichever renderer presents it. Studio Workflows contributes the batch's designer-history Undo and its saved state as such a control.

**Tests**:
- schema refusal per operation kind;
- catalog, credential and ambiguity clarifications;
- atomic apply and stale refusal for content, binding, selection, remount and second-tab changes;
- the armed capture timer flush;
- Undo, Redo and autosave across Undo and Redo;
- scope-frame and selection rules.

**Probe**: `workflow-batch` (five operation kinds, one in a nested scope, with a hand edit in the base revision), plus negative controls for stale, selection, second tab, remount, missing activity, hidden activity, undeclared property, ambiguous target, credential literal, published version and no manage permission.

## Slice D: Explain and troubleshoot

- The active-draft snapshot adds the selected activity's declared properties. For expressions it adds the expression type and bounded content, with truncation marked. Credential literals are disclosed as present and withheld. Visible Secret References are disclosed by name, type and scope.
- Expression interpretation, semantic diagnostics and edits are unconditionally unavailable: the server stamps `interpretation: "unavailable"` on every non-literal expression, and expression edits are refused at preparation. No Expression Module registers Weaver tools and no contract maps an expression type to one; routing to the owning Expression Module is deferred to [#3711](https://github.com/elsa-workflows/elsa-foundation/issues/3711).
- The graph read addresses activities by scope path, and graph activities by definition identity and version.
- Diagnostics come only from the designer's current validation.
- Fix FR-019: remove the unused `workflows.proposal` renderer, and remove the explain-executable prompt until slice F registers its tool. A behaviour test asserts that every Workflows prompt, guidance text and renderer references a registered tool or result kind (SC-007).
- **Probe**: `workflow-explain`, with a canary search over every disclosed payload and audit record.
- **Packaged consumer limits**: its catalog has no Parallel activity (`Elsa.Activities.ControlFlow.Parallel` is not a consumer package), so the probe's parallel branches are a flowchart fan-out (a's Done port to b and to the nested flowchart), and scope paths through a Parallel container are proven by component tests (`WorkflowExplainContextTests`). It has no graph activity definition either, so graph activity references are proven by component tests only (server admission and Studio's bridge, sharing `workflowExplainSnapshot.json`); the spec's acceptance evidence records the same.
- **Size**: the snapshot is sized against the host's 32 KiB per-attachment budget on the admitted projection; drafts above it (about 126 activities on a chained flowchart) cannot be disclosed at all. The follow-up is [#3710](https://github.com/elsa-workflows/elsa-foundation/issues/3710).
- **Catalogs** (FR-034): the "Explain and troubleshoot" section of `Extensions/Agent/src/Workflows/Elsa.Agent.Workflows/EXTENSION_POINTS.md`, its link from `Extensions/Agent/src/Elsa.Agent/EXTENSION_POINTS.md`, and the Agent Workflows README.

## Slice E: Library, create-dialog intent and templates

Concrete file seams are recorded in [research.md](research.md).

- **List context**: `workflows.library` v1 from Studio Workflows: the visible query, the view's identity, owner kind and version, the columns, the selection and the native permission flags. It is unavailable with a reason when organization is disabled.
- **List state tool**: a client working-state tool for narrowing, sort and columns, reviewed in every mode and revertible.
- **Library write tools** in Agent Workflows: tag assignment (with an explicit tag creation), favorite, folder move, and view save or share. They go through `IWorkflowLibraryCommands` (D4), which carries the native granular authority checks extracted from the endpoints. A replacement `IWorkflowLibraryCommands` decides Weaver's library writes; the HTTP endpoints keep the built-in decisions. The tools return `RequiresApproval` from preparation and report outcomes per target.
- **Operation keys**: `CreateTagAsync` and `SaveViewAsync` gain default-interface-member overloads with an optional operation key. The default reports that the key is unsupported. The EF store stores the key in a new column. Its unique index is scoped by the tenant scope key for tags, and by the tenant scope key and owning subject for views, in the same way as the existing tag index (`WorkflowLibraryEntityConfigurations.cs:71`). The store returns the existing resource for the same key and input, and refuses the same key with different input as a conflict. Recovery rereads the resource by its key.
- **Create dialog**: `workflows.create-dialog` v1, with a prefill client tool for name, description and a root activity from the catalog or a template. It is reviewed in every mode.
- **Templates**: `workflows.template-list` v1 and `workflows.template-editor` v1, with identity and persisted revision. Template batches reuse slice C's tool against the template editor's adapter, which declares the template-editor resource. Persistence remains the user's save with the expected revision.
- **Refusals**: delete and permanent delete are refused with a pointer to the library actions.
- **Probe**: `workflow-library` and `workflow-templates`, including dropped responses for the view save and the tag creation, organization disabled, and an actor without tag-management or view-sharing authority.

## Slice F: Versions and Executables

- **Versions** (Agent Workflows): `workflows.version` v1 and a read-only version tool over `IWorkflowVersionReader` (D4), bound to exact version identities, with Weaver's own two-version comparison computed over two reads from the owning design services and labelled as such (R6). Edit requests are refused with a pointer to the draft.
- **Executables** (`Elsa.Agent.Workflows.Runtime`, over `IWorkflowExecutableInspection` from D4): `workflows.executable` v1 and `workflow_read_executable`, which returns identity, Artifact Hash, graph and the visible Source References. The explain-executable prompt is restored against this tool.
- **Deployment shapes**: the Design-only, Runtime-only and combined hosts each start and report unsupported journeys as unavailable. The Runtime-only proof asserts, AppDomain-wide, that no `Elsa.Workflows.Design*` assembly is loaded. It therefore needs its own test project, `Elsa.Agent.Workflows.Runtime.Tests`, because `Elsa.Agent.Tests` loads Design assemblies.
- **Normal host**: Playwright journeys for the version history and the Executable Inspector.
- **Probe**: `workflow-versions` and `workflow-executables`, in all three shapes.

## Slice G: One exact Run

- `workflows.run` v1 and `workflow_read_run` in `Elsa.Agent.Workflows.Runtime`, over `IWorkflowRunInspection` (D4). They are bound to one Workflow Instance, and one activity execution where selected, under existing run access.
- Execution-evidence redaction is applied, and unavailable data is reported by reason: not captured, pruned, unsupported or not permitted.
- **Stricter redaction (decided in slice G review).** `IWorkflowRunInspection` never releases or resolves a sensitive value, even one a capture policy captured, and withholds the incident messages and state view of an activity with a sensitive input binding, because Weaver discloses to a model provider, a wider audience than the actor's own run page. The trade-off: Weaver never sees values the run page itself can show; the run page's own payload read stays the way to see them. A retained Run read is withheld once the actor loses the value inspection or payload resolution it was read under, and the payload read audits only the values a result discloses.
- Retry, cancel, delete and dispatch are refused with a pointer to the run controls.
- **Normal host**: a Playwright journey on the run page.
- **Probe**: `workflow-run`, with a canary search. Then the full package conformance witness and the composed Studio suite on the final source.
  - **Deviation (recorded for T046's acceptance).** The spec's independent test asks for pruned evidence and a backend without activity execution inspection natively. The packaged consumer cannot do either: its in-memory runtime stores keep a Run's evidence for the process lifetime, and activity execution inspection is always composed by `WorkflowsRuntimeApi`. Both are proven by unit tests instead (`WorkflowRunInspectionTests` in the Runtime API tests, `WorkflowRunAgentTests` and the started Runtime-only shell in `Elsa.Agent.Workflows.Runtime.Tests`); natively the selected execution's state view is reported unavailable by reason and the Runtime-side agent feature is deselected for the deployment-shape control.
  - **Native canary.** A test-only host seam (`QualificationRunCapture`) makes the runtime capture the sensitive input's payload, so the canary really is in execution evidence and a regression that released captured sensitive payloads would leak natively; the browser journey selects that activity execution.

## Public surface and SemVer

| Item | Slice | Class |
| --- | --- | --- |
| The `errorCode` property on design error bodies, the 503 outcome, and the public constant `WorkflowDefinitionAuthorizationException.UnavailableCode`, which is compiled into callers and so never changes value | A | Minor |
| The removed legacy contracts, implementations and models in `Elsa.Agent.Workflows` | B | Major (pre-GA cleanup) |
| `workflow_apply_operation_batch` v1 and the new context kinds | C, D, E, F, G | Minor |
| The working-state content revision becomes `sha256:` plus 64 hex digits instead of an 8-hex 32-bit hash. Consumers compare it for equality only. A proposal prepared before the upgrade and recovered after it carries the old format, so it fails closed as stale and is never applied | C | Minor |
| The SDK's optional `resourceModuleIds` on result renderers, the `resultControls` registry with `StudioAgentResultControlContribution`, the `studio.weaver.result-controls` slot and `presentsResourceModule` | C | Minor, additive |
| The removed `workflows.proposal` renderer | D | Major |
| The removed explain-executable prompt and the test-only `getExecutableDetail` export of Weaver Workflows; slice F restores the prompt against its registered tool | D | Major (pre-GA cleanup) |
| The active-draft snapshot's optional `diagnostics`, `diagnosticsOmitted`, `selectedActivity` and `graphActivities`, the read result's `addresses`, and the optional diagnostic `agent.workflow.property_context_unverified` | D | Minor, additive |
| The removed explain-instance prompt of Weaver Workflows; slice G restores it against the Run context and tool | D | Major (pre-GA cleanup) |
| `ActivityAuthoringCatalogInput.UiHint` and the computed `IsMasked` in `Elsa.Activities.Design.Core` | D | Minor, additive (init-only property; the positional constructor is unchanged). Record value equality now includes `UiHint`: two inputs that differ only in their hint are no longer equal |
| `workflow.catalog` v1 inputs gain `uiHint` and `isMasked` | D | Minor, additive |
| The removed `ai-context-providers` and `ai-tools` capability tags of the Weaver Workflows Studio module manifest | D | Patch (correction): the tags described contributions the module never registered. The module's scope (frontend) and every contribution it does register are unchanged; a host that filtered modules by those tags found a module without such contributions |
| The operation-key overloads on `IWorkflowLibraryStore` | E | Minor, additive |
| `IWorkflowLibraryStore.FindTagByOperationKeyAsync` and `FindViewByOperationKeyAsync`, default interface members like the keyed overloads, and the fixed-message `WorkflowLibraryOperationKeyUnsupportedException` that their defaults throw (501 on the API) | E | Minor, additive |
| The EF library store's nullable `OperationKey` and `OperationFingerprint` columns on the tag and view rows, their unique indexes, and the `WorkflowLibraryOperationKeys` migration for SQLite, SQL Server, PostgreSQL and MySQL | E | Minor (additive schema) |
| The optional `operationKey` on the `POST tags` and `POST views` bodies, bound to the new `CreateWorkflowTag` and `CreateWorkflowView` request records (every former body field kept) | E | Minor, additive on the wire |
| `TagCreateEndpoint` and `ViewCreateEndpoint` bind the new `CreateWorkflowTag` and `CreateWorkflowView` request records (the base type's request argument changes). Every library endpoint keeps its constructor and the built-in logic as on main; routes, permissions and outcomes are unchanged | E | Major (pre-GA cleanup) for direct C# use of those two classes' base type; none is known |
| `IWorkflowLibraryCommands` and its Core models (`WorkflowLibraryActor`, `WorkflowLibraryCommandResult<T>`, `WorkflowLibraryCommandStatus`, `WorkflowLibraryTargetOutcome`, the create shapes with deprecation and value identities, and the snapshot records with the view schema version) in `Elsa.Workflows.Design.Core` | E | Minor, additive |
| `ShellReloader` logs a failed reload (`ReloadResult.Error`) at Error with its cause; the count it returns is unchanged | E | Patch (diagnostics) |
| The `workflows.library-context` source with the `workflows.library`, `workflows.create-dialog`, `workflows.template-list`, `workflows.library-organization` and `workflows.library-guidance` kinds; the `workflows.template-editor` kind of `workflows.context`; the tools `workflow_library_assign_tags`, `workflow_library_set_favorite`, `workflow_library_move_to_folder`, `workflow_library_save_view`, `workflow_library_set_list_state` and `workflow_create_dialog_prefill`; the capabilities `workflow.library-explain` and `workflow.library-organize`; and a template target for `workflow_apply_operation_batch`, whose preview then names `templateId` and `templateRevision` instead of the definition and draft | E | Minor, additive |
| `workflow.permissions` for a template target names `templateId` (reference and content) instead of `workflowDefinitionId` | E | Minor, additive (a new target kind) |
| Studio Workflows: the optional `agentResource` of `WorkflowEditorDocumentAdapter` and of the context bridge, the optional `library` state of `WorkflowAgentController`, `onViews` of `WorkflowLibraryControls`, the `workflows.library-view` context provider, the `workflows.library-state` tool slot with its two client tool contracts and the `workflows.agent.page-revert` result control; Weaver Workflows: the `weaver.workflows.library-result`, `weaver.workflows.list-state-result` and `weaver.workflows.create-dialog-result` renderers | E | Minor, additive |
| `IWorkflowVersionReader` and `WorkflowVersionDetails` in `Elsa.Workflows.Design.Core`, implemented by the internal `WorkflowVersionReader` in the Design API with a single-registration guard | F | Minor, additive |
| `IWorkflowExecutableInspection` and the `WorkflowExecutableInspection*` models in `Elsa.Workflows.Runtime.Core` (recorded in its `PublicAPI.Unshipped.txt`), implemented by the internal `WorkflowExecutableInspection` in the Runtime API with a single-registration guard | F | Minor, additive |
| The `workflows.version-context` source with the `workflows.version` and `workflows.version-guidance` kinds, the tool `workflow_read_version` with result kind `workflows.version`, and the capability `workflow.version-explain` (Agent Workflows) | F | Minor, additive |
| The source location of the `Elsa.Agent.Workflows` project, moved into `Extensions/Agent/src/Workflows/Elsa.Agent.Workflows/` (package id, assembly and namespaces unchanged) | F | None (not public surface) |
| The package `Elsa.Agent.Workflows.Runtime` with the shell feature `FoundationWorkflowsRuntimeAgent`, the `workflows.executable-context` source with the `workflows.executable` and `workflows.executable-guidance` kinds, the tool `workflow_read_executable` with result kind `workflows.executable`, and the capability `workflow.executable-explain` | F | Minor, additive |
| Weaver Workflows: the restored `weaver.workflows.explain-executable` prompt (against `workflow_read_executable`) and the new `weaver.workflows.explain-version` prompt; Studio Workflows: the `workflows.executable-inspector` and `workflows.version-history` context providers, the version selection in the editor's Versions list, the optional `weaver` controller on the Executable Inspector page, and the new public exports `workflowExecutableAttachment` and `workflowVersionAttachment` of `@elsa-workflows/studio-workflows` (Weaver Workflows builds its prompt attachments with them) | F | Minor, additive |
| `IWorkflowRunInspection` and the `WorkflowRun*` models (`WorkflowRunInspectionRequest`, `WorkflowRunInspectionDetails` and its instance, incident, activity page and summary, selection, activity execution, bookmark, value and state view records, and the `WorkflowRunUnavailableReason` and `WorkflowRunValueDisclosure` enums, whose zero value is `Withheld`; the request names the evidence to resolve in `ResolveEvidenceIds`, the details carry `CanInspectValues` and `CanResolveValues`, incidents `SensitiveInputs` and the state view `Withheld`) in `Elsa.Workflows.Runtime.Core` (recorded in its `PublicAPI.Unshipped.txt`), implemented by the internal `WorkflowRunInspection` in the Runtime API over the run endpoints' services, with a single-registration guard (`WorkflowRunInspectionStartupTask`) | G | Minor, additive |
| The `workflows.run-context` source with the `workflows.run` and `workflows.run-guidance` kinds, the tool `workflow_read_run` with result kind `workflows.run`, and the capability `workflow.run-explain` (`Elsa.Agent.Workflows.Runtime`); the feature description of `FoundationWorkflowsRuntimeAgent` names Runs | G | Minor, additive |
| Weaver Workflows: the restored `weaver.workflows.explain-instance` prompt (against `workflow_read_run`; label "Explain run", placement `selection`, the run page attaches the Run itself). Studio Workflows: `workflowRunAttachment`, the `workflows.run-inspector` context provider, the optional `runs` map of `WorkflowInspectionAgentController`, the `run` kind of the inspection context hook, the optional `weaver` controller on the run page, whose Explain action now dispatches the Run's identity (`workflowExecutionId`, `activityExecutionId`, `status`, `attached`) instead of the whole instance details, and its `data-run-selection` attribute | G | Minor, additive; the explain-instance prompt's dispatch context changes shape, which only the restored prompt consumes |
| The new `.Core` contracts of D4 | B, E, F, G | Minor, additive |
| The Preview property `AgentViolation.OwnerUnavailable`, and its 503 mapping in the tool invoker, proposal execution and context admission | A | Minor |

## Project Structure

```text
specs/1193-weaver-workflow-journeys/
├── spec.md
├── plan.md          # this file
├── research.md      # seams and evidence per slice
└── tasks.md

Essentials/src/Workflows/Design/Api/          # A, E
Essentials/src/Workflows/Design/Persistence/  # E (EF operation key)
Extensions/Agent/src/Workflows/Elsa.Agent.Workflows/  # A–E, F (versions); moved into Workflows/ by slice F
Extensions/Agent/src/Workflows/Runtime/       # F, G (new)
Studio/src/essentials/Elsa.Studio.Workflows/  # C, D, E, G
Extensions/Weaver/studio/Elsa.Studio.Weaver.Workflows/  # C, D, E, F
tools/studio/Weaver.Conformance.Probe/        # every slice
```

## Risks

- **History and autosave in slice C carry the most behaviour.** It is proven by component tests first, with the capture timer armed, before any Playwright journey.
- **The library operation-key migration in slice E touches four engines.** It runs the EF container tier.
- **Runtime-only hosts in slice F** may need qualification-host wiring that does not exist yet. That wiring is in scope for slice F.
