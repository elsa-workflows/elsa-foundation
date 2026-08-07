# Research: File-based workflow deployment at startup

**Feature**: `specs/147-file-workflow-deployment` | **Date**: 2026-08-06

All findings verified against source on branch `780-file-workflow-deployment` (post `Elsa.Server` → `Elsa.Workbench` rename, `main` @ `928f05c99`).

## Verified current state (delta on top of spec.md's Context)

1. **No reconciliation-completed event exists.** The only event in the workflow-reconciliation domain is `OnWorkflowVersionsReconciling` ([Core/OnWorkflowVersionsReconciling.cs](../../src/Elsa/Workflows/Design/Reconciliation/Core/OnWorkflowVersionsReconciling.cs)) — a *pre-pass* Sequential fan-in event whose payload (`ICollection<IWorkflowDefinitionVersion> Versions`) carries **no source provenance**: `WorkflowVersionsReconcilingHandler` flattens all sources into one bag, and `SourceId`/`SourceKind` are lost at the event boundary. Spec FR-006's trigger must be authored from scratch.
2. **Provenance is not persisted.** `WorkflowDefinition` persists `IsSourceOwned` (bool) but no `SourceId`; `IWorkflowDefinitionVersion` exposes `Id`, `Version`, `DefinitionId`, `Definition`, `State` — no source identity. Which source owns which definition is knowable only at contribution time, inside the reconciling handler.
3. **The XOR validation idiom does not extend.** `JsonWorkflowReconciliationFeature.ValidateOptions()` uses `hasSingleFile == hasFileList` (boolean-equality trick, throws `InvalidOperationException`); a third option forces a count-of-configured-options rewrite.
4. **Startup-task failure fails shell activation.** `TaskManager.Start` rethrows any startup-task exception; a reconcile failure leaves `/health/ready` at 503 `shell_activation_failed`. FR-009's "publish failure must not fail shell activation" means the publish step must swallow per-definition failures itself.
5. **Readiness ordering is already correct.** `IStartupTask`s run via `RunShellTasksInitializer` during shell activation; `DefaultShellWarmup` marks ready only after `GetOrActivateAsync` returns the `Active` shell. Whatever runs inside the reconcile pass completes before `/health/ready` flips.
6. **`PublishWorkflow` takes a `VersionId`** (`Elsa.Workflows.Publishing.Core.Requests.PublishWorkflow(string VersionId, …) : IRequest<PublishedWorkflowView>`), returns `PublishedWorkflowView` incl. `ArtifactId` and `WasCreated`. `PublishWorkflowRequestHandler` already contains an idempotent short-circuit (active slot + same artifact + retained triggers ⇒ `WasCreated=false`, no writes).
7. **Slot check API**: `IPublicationSlotStore.ListByDefinitionAsync(definitionId)` → `slot.ActivePublicationId` → `IPublicationRecordStore.FindAsync(id)` → `record.Status == PublicationStatus.Active`, `record.WorkflowDefinitionVersionId`, `record.ArtifactId` (exactly what `ListPublicationSlotsEndpoint` does).
8. **The seam permits Publishing → Design.** Guard rules forbid Runtime→Design/Publishing and Publishing→Runtime.Api. `Elsa.Workflows.Publishing` (engine) already references `Elsa.Workflows.Design.Persistence.Core` (incl. `IWorkflowDefinitionVersionStore.FindLatestVersionAsync/ListByDefinitionAsync`), `Elsa.Events.Core`, `Elsa.Locking.Core`, and the *activities* reconciliation Core. Adding `Elsa.Workflows.Design.Reconciliation.Core` violates nothing. Do **not** touch `Elsa.Workflows.Publishing.Persistence.Groundwork.csproj` — its reference list is pinned by exact equality (`ReusableActivityArchitectureTests`).
9. **Activities precedent**: `IActivitySourceVersionPublisher` (contract in `Activities.Design.Reconciliation.Core`, impl `SourceOwnedActivityVersionPublisher` in `Publishing.Api`) is an optional bridge invoked inline per version by `ActivityVersionReconciler`; opt-in is *feature presence*, no flag.
10. **Git source is not an ordering precedent.** `GitWorkflowReconciliationSource` enumerates with `Directory.EnumerateFiles(dir, "*.json")` — top-level only but **no comparer**; ordering doesn't matter there because version identity comes from the file name. FR-003's ordinal ordering is written new.
11. **e2e-tests run the server from source** (no docker): `dotnet run`/direct DLL launch on `http://localhost:5095`, repo's own `shells.json` as composition, auth via cookie login. The durability suite (`_DurabilityCommon.ps1`) owns server lifecycle (start/stop/restart with env vars) — the precedent for a suite that needs its own composition. `/health/live` + `/health/ready` exist and are guard-pinned.
12. **Workbench catalog template** (guard-pinned style): csproj `ProjectReference` + `typeof(XFeature).Assembly` line in `Program.cs` + feature name in shells files *when enabled by default*. `.WithHostAssemblies()` would discover the feature from the reference alone, but explicit `typeof` lines are the repo convention the guard tests grep for.
13. **`CatalogParityTests`** scans `Elsa.Workflows.Design.Reconciliation.Core` for `IEvent` types and asserts each has an `### On…` heading in that domain's `EXTENSION_POINTS.md` — a new event must land in the catalog in the same change.

## Decisions

### D1 — Publish trigger: new `OnWorkflowVersionsReconciled` completion event + Publishing-side subscriber

**Decision**: Author `OnWorkflowVersionsReconciled` in `Elsa.Workflows.Design.Reconciliation.Core`, published **Sequentially** (`IInlineEventPublisher`) by `WorkflowsVersionReconciler.Reconcile` after the pass completes without error. The Publishing engine (`WorkflowsPublishingFeature`) registers an independent subscriber `PublishReconciledWorkflowVersions : IEventHandler<OnWorkflowVersionsReconciled>` (verb-named, matching the Publishing feature's own `CollectExecutableCompilation` style) that performs the publish step.

**Rationale**:
- Sequential delivery inside the reconciler means the publish step runs **inside** `WorkflowsVersionReconcilerStartupTask` — so it inherits `[SingleNodeTask]` + the distributed lock (FR-008 satisfied without a second task/lock) and completes **before** `/health/ready` flips (SC-001's "ready ⇒ published" ordering).
- An independent `IEventHandler<T>` subscription is explicitly sanctioned (framework §2.6.1: "features are free to register `IEventHandler<T>` for independent subscriptions"); it is not the fan-in contribution axis, so the single-aggregating-handler convention does not apply to this new event.
- The framework forbids attaching a business-critical subscriber to a Background notification; SC-001 makes this subscriber business-critical ⇒ Sequential is the constitutionally forced choice. The corollary (a Sequential handler throw fails the pass) is neutralized by D5.

**Alternatives rejected**:
- *Activities-style bridge* (`IWorkflowSourceVersionPublisher` analogue, invoked inline per version): opt-in is feature presence, not the per-source `PublishOnReconcile` flag the spec pins; it publishes per contributed version mid-pass rather than the latest version once after a successful pass; and workflows publish through the mediator `PublishWorkflow` request (spec 145's seam), not a commit command.
- *Publishing-domain startup task `[Order(3+)]`*: requires a new `Elsa.Tasks.Core` reference on the engine, duplicates lock machinery, and — decisive — has no way to discover *which* definitions to publish: provenance is not persisted (finding 2), so a task ordered after the reconciler cannot scope to "definitions owned by sources that opted in" without coupling to the Json feature's options.
- *Second subscriber on `OnWorkflowVersionsReconciling`*: pre-persistence timing (nothing exists to publish yet) and it is the fan-in event with exactly one sanctioned aggregating handler.

### D2 — Provenance + opt-in flow: source claims carried through the event pipeline

**Decision**:
- `IWorkflowReconciliationSource` gains a default interface member `bool RequestsPublication => false` (additive, MINOR). `JsonWorkflowReconciliationSource` returns `Options.PublishOnReconcile`.
- `OnWorkflowVersionsReconciling` gains a second get-only collection `ICollection<WorkflowVersionSourceClaim> Claims { get; } = [];` (additive). `WorkflowVersionsReconcilingHandler` populates one claim per contributed entry alongside the version object: `(DefinitionId, Version, SemVerSortKey, SourceId, SourceKind, PublishRequested, Deleted)` — `DefinitionId` recorded *after* the definition factory resolves/generates it, so generated ids are correct.
- `WorkflowsVersionReconciler` publishes `OnWorkflowVersionsReconciled` carrying the claims (read-only) after the pass succeeds. `WorkflowVersionSourceClaim` is a `sealed record` in `Reconciliation.Core`.

**Rationale**: provenance exists only at contribution time (finding 2); the claims collection is the minimal additive carrier. The Publishing subscriber stays decoupled from the Json feature — it sees only the neutral claim contract, so a future Git/other source setting `RequestsPublication` composes for free.

**Alternative rejected**: the subscriber reading `IOptions<JsonWorkflowReconciliationOptions>` directly — couples the Publishing engine to one source implementation, breaking the provider-neutral seam and framework §2.20 rule 3.

### D3 — Latest-version semantics and the publish algorithm

**Decision**: per definition, the subscriber groups claims by `DefinitionId`, takes the claim with the highest `SemVerSortKey` (ordinal compare — the repo-canonical `SemVer.ToSortKey` ordering), and:
1. Skips when `Deleted` (or the loaded definition has `DeletedAt != null`) — never publish deleted definitions.
2. Resolves the target version row via `IWorkflowDefinitionVersionStore.ListByDefinitionAsync(definitionId)` filtered on `SemVerSortKey` (there is no find-by-sort-key port; list+filter matches spec assumption "latest **reconciled** version", not store-latest, so a Studio-promoted higher version is never hijacked).
3. Idempotency pre-check (FR-007), scoped to the slot the publish request will actually update: `IPublicationPolicyStore` + `IPublicationPolicyResolver` (request intent `null`, workflow policy → host policy → synthesized `default`) resolve the slot name exactly as `PublishWorkflowRequestHandler` does, then `IPublicationSlotStore.FindAsync(definitionId, slotName)` → active `PublicationRecord`; when `record.Status == Active && record.WorkflowDefinitionVersionId == targetVersion.Id` ⇒ skip (log debug). Deliberately not `ListByDefinitionAsync`: a version active only in a non-target slot (a side-by-side `canary`) must not suppress publishing into a target slot that is empty or stale. A policy that cannot resolve a slot (`RequireExplicitSlot`) is treated as "no pre-check" — the pre-check is an optimization, and `PublishWorkflow` owns the authoritative failure. `PublishWorkflow`'s internal `WasCreated=false` short-circuit remains the second net for races.
4. Otherwise `IRequestSender.Send(new PublishWorkflow(targetVersion.Id), ct)`.

### D4 — Failure policy (FR-009)

**Decision**: `PublishReconciledWorkflowVersions` **never throws**. Each definition is processed in its own try/catch; failures produce a structured error log naming `DefinitionId`, `Version`, `SourceId`, and the exception (publishing's typed exceptions — `PublicationPreflightConflictException`, `ExpressionPublicationValidationException`, `PublicationActivationException` — carry codes worth logging). Remaining definitions continue. The subscriber's failure classification is documented in the event's `EXTENSION_POINTS.md` entry. Reconcile-pass failures keep today's semantics (pass aborts, shell activation fails) — FR-009 scopes only the publish step.

### D5 — `FolderPath` scan semantics (FR-002/003/004)

**Decision**:
- `JsonWorkflowReconciliationOptions.FolderPath` (string?, default null). Validation rewritten as a count: exactly one of `FilePath` / `Files` / `FolderPath` configured, else `InvalidOperationException` naming all three (same registration-time placement, before `base.ConfigureServices`). `SourceId` check unchanged.
- Scan in `JsonWorkflowReconciliationSource.EffectiveFiles()`: `Directory.EnumerateFiles(folder, "*.json", SearchOption.TopDirectoryOnly)` ordered by `Path.GetFileName` with `StringComparer.Ordinal`, mapped to sequential `JsonWorkflowReconciliationFileOption`s feeding the existing reader path. Non-recursive (spec assumption: Kubernetes ConfigMap `..data` symlink layouts double-read under recursion).
- Missing folder ⇒ `InvalidWorkflowCatalogJsonException(folderPath, "the folder does not exist.")` at read time (actionable, fails activation — same class of error as a missing `FilePath`). Empty folder / no `*.json` matches ⇒ info log + empty contribution (startup succeeds).
- Malformed file keeps the existing fail-fast semantics of the Json reader (whole pass aborts with a file-naming exception) — this **is** the "existing multi-file semantics" the spec pins, and it is documented; the Git source's skip-and-warn is a different source's policy.
- Additionally the source logs a **warning per envelope with a null/blank `definitionId`** (spec edge case: random-id-per-restart duplicates).

### D6 — Workbench packaging (FR-001)

**Decision**: add `ProjectReference`s for `Elsa.Workflows.Design.Reconciliation` and `…Reconciliation.Json` (Core arrives transitively) with the conventional "why" comment; add `using`s + `typeof(WorkflowsDesignReconciliationFeature).Assembly` / `typeof(JsonWorkflowReconciliationFeature).Assembly` to the `.WithAssemblies` block in `Program.cs`. **Do not enable the feature in the default `shells.json`/`shells.baseline.json`** — it requires a non-empty `SourceId` + path and would otherwise throw at registration and fail every dev shell. Add a new architecture guard test in the established "Server_catalogs_…" style pinning csproj + `Program.cs` (not shells enablement). Docs (D8) carry the enablement example. If the EF-surface ratchet baseline shifts from the new transitive references, regenerate it (`ef-core-surface.json`) — expected no-op since the reconciliation family is EF-free.

### D7 — e2e test shape (FR-011, SC-001/002/005)

**Decision**: new suite `e2e-tests/file-deployment/` (`README.md`, `_FileDeploymentCommon.ps1`, `Test-FileBasedDeployment.ps1`) following the **durability** suite's server-lifecycle precedent:
1. Phase A (id resolution): with a normally-running server, resolve the target activity's `actver_*` id via the existing `Get-ActivityVersionId` helper; write the definition file (pinned `definitionId`, resolved id) into a temp definitions folder.
2. Phase B: stop the server; start it durability-style with env vars composing the feature — `CShells__Shells__default__Features__JsonWorkflowReconciliation__Options__SourceId`, `…__Options__FolderPath`, `…__Options__PublishOnReconcile=true` (env vars layer above `shells.json`, guard-pinned precedence).
3. Poll `GET /health/ready` until 200 (not `/` — the readiness note is part of the deliverable).
4. Assert: `GET /design/workflows/definitions?name=…` (definition present), `GET /publishing/workflows/{definitionId}/slots` (active publication + `artifactId`), `POST /runtime/workflows/executables/{artifactId}/execute` completes (pass `sourceReferenceId` — content-addressed artifacts can be shared).
5. Restart the server with unchanged files; assert no new definition versions and the same `ActivePublicationId` (SC-002).
6. Register the suite in `e2e-tests/README.md`'s categorization + scripts tables and document the composition mechanism.

Docker (`SC-001` literal) remains the documented acceptance flow (docs example, D8); e2e-tests are source-run by convention (no docker anywhere in `e2e-tests/`).

### D8 — Docs placement (FR-010)

**Decision**:
- New `src/Elsa/Workflows/Design/Reconciliation/Json/README.md` (per `Clr/README.md` precedent): what the feature provides, options table (incl. `FolderPath` scan rules + `PublishOnReconcile`), registration snippet, definition-file authoring guidance (envelope shape, **pin `definitionId`**, `actver_*` recipe: `actver_` + base64url(SHA-256(typeKey + U+001F + SemVer sort key)) via `ActivityCatalogStableIds`, or query `/design/activities` at runtime), constitutional basis.
- `docs/docker-hub-quickstart.md`: new `##` section after "Mount it — two modes" — mounted definitions folder + `shells.json` feature snippet + readiness note (`/health/ready` gates deployment completion; `/` does not).
- `docs/docker.md`: add the definitions-folder row to the "### Mounts" table.
- `EXTENSION_POINTS.md` updates (same unit of work, §2.22.1): Reconciliation catalog gains `### OnWorkflowVersionsReconciled` (+ `RequestsPublication` on the `IWorkflowReconciliationSource` entry, + claims on the Reconciling entry); Publishing feature `README.md` gains a **Cross-domain contributions** section (it now subscribes to a Design-reconciliation event); Publishing `EXTENSION_POINTS.md` notes the subscriber under cross-domain seams consumed.
- Regenerate `docs/maps/*` via `tools/maps` (feature/dependency/project-reference maps shift).

### D9 — Versioning (§4.2)

All changes are **MINOR**: additive options (`FolderPath`, `PublishOnReconcile` default `false` — no-option behaviour unchanged), additive default interface member (`RequestsPublication`), additive event + payload collection, additive subscriber registration (`TryAdd`-safe). No feature ids change. No wire identifiers change (`SourceKind` stays `"Json"` — the `"Json"`/`"git"` casing inconsistency is noted but not fixed here; persisted/wire values are rename-exempt per §E6 scope).

### D10 — Known pre-existing hazards (documented, not fixed here)

- Enabling both `JsonWorkflowReconciliation` and `WorkflowsDesignGitReconciliation` in one shell double-registers `IStartupTask → WorkflowsVersionReconcilerStartupTask` (both call `base.ConfigureServices`) — pre-existing; out of scope, noted for a follow-up issue.
- Only one `JsonWorkflowReconciliation` feature instance is possible per shell (one source, one `SourceId`) — `FolderPath` mitigates the practical need for multiples.
- The silent skip of unknown feature names in the runtime catalog (issue #1157's side observation) is a Modularity concern, out of scope.
