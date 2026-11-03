# Feature Specification: Module-owned Weaver support for the current Studio inventory

**Feature Branch**: `agent/claude/3443-weaver-module-support`

**Created**: 2026-11-03

**Status**: Draft

**Input**: [Feature #3443](https://github.com/elsa-workflows/elsa-foundation/issues/3443) is M4 ([#3433](https://github.com/elsa-workflows/elsa-foundation/issues/3433)) of [Program #3428](https://github.com/elsa-workflows/elsa-foundation/issues/3428). It is [in progress](https://github.com/elsa-workflows/elsa-foundation/issues/3443#issuecomment-6612633571).

The Program owner's standing authorization covers autonomous delivery of #3443's six acceptance criteria, within the issue's boundaries. The owner decisions under Assumptions amend those boundaries. The current inventory is the finite matrix. The authorization grants no blanket administration permission, no arbitrary workspace editing, and no package promotion or activation. Consequential decisions outside that scope go to the Program owner (#3428, 29 October continuation).

The scope comprises:
- the M0 inventory and its targets in [docs/reports/weaver-baseline.md](../../docs/reports/weaver-baseline.md), including Activity definitions;
- the issue's 30 and 31 October planning comments;
- the rows that the [claim](https://github.com/elsa-workflows/elsa-foundation/issues/3443#issuecomment-6605270638) adds: `/user-tasks` and `/agents`;
- the screens merged since then: `/settings/backends` (PR #3600) and the BPMN pages (PR #3603).

Prerequisites #3440 and #3441 are Done. #3442 is complete, and [spec 1193](../1193-weaver-workflow-journeys/spec.md) is Implemented (#3736).

**What this spec covers**:
1. **The M4 delivery matrix (FR-001):** every row, with its target, outcome and grounds.
2. **The cross-cutting requirements** that bind every leaf.
3. **Three leaves, fully specified:**
   - User Story 1 is leaf 0: the generic changes.
   - User Story 2 is leaf 1: Secrets read, metadata update and value test.
   - User Story 3 is leaf 2: Service connections, plus secret revoke and delete.

Leaves 1 and 2 depend on leaf 0, and leaf 2 builds on leaf 1. Each later leaf gets its own spec (FR-025).

This spec's plan PR, the PR that adds `plan.md`, also adds `contracts/privilege-policy.md` and `contracts/outcomes.md`. Leaf 0's PR is the first implementation PR.

**Terminology**:
- **Glossary terms:**
  - Studio terms keep their meanings in [Studio/CONTEXT.md](../../Studio/CONTEXT.md).
  - *Service connection* keeps its meaning in the [Elsa glossary](../../docs/glossary/elsa.md).
  - FR-021 lists the glossary changes this spec makes.
- **Outcome words:**
  - [Spec 1186](../1186-weaver-effects/spec.md) defines *proposed*, *approved*, *persisted*, *refused* and *uncertain*.
  - Spec 1186 FR-003 and #3428 define *applied locally*.
  - Spec 1193 FR-001 defines *owner-unavailable*.
  - [Spec 1189](../1189-weaver-recovery/spec.md) defines *unavailable*, for an effect whose module or client is gone.
  - *Stale* is a refusal because the target changed after review (spec 1193 FR-015).
  - Leaf 0 adds *performed*: the approved action ran, and the owning module persisted no change to its resource.
- **"Owner"** is always qualified:
  - the *owning module* owns a row's resource;
  - the *Program owner* is Sipke Schoorstra;
  - the *session owner* resolves an uncertain effect (spec 1189);
  - a Service connection's *Owner field* is part of its metadata.
- **Module code:** a code that a tool reports from its declared set (FR-016).
- **Secret consumer:** a resource that uses a secret, such as a Service connection field bound to it. This term is distinct from Studio's *Secret Reference*.
- **Host attachment budget:** the per-collection attachment count, the per-attachment bounds, and the session disclosure budget of Weaver's context admission ([#3710](https://github.com/elsa-workflows/elsa-foundation/issues/3710)).

**Hosts**:
- **Normal host:** the Workbench backend (`Elsa.Workbench`) with Studio (`Elsa.Studio.Web`), Design and Runtime combined, in the Development configuration.
- **Durable host:** the normal host with the overlay the Workbench's durable-store test applies, which adds the Agent EF store. Secrets EF is already composed. In leaf 2, the durable host also adds the Connections EF store, and leaf 2 adds it to the Workbench project and the Workbench feature catalog.
- **Probe host:** the conformance probe's packaged consumer.
  - Its baseline does not compose the Agent EF store.
  - Its restart runs enable the Agent EF store together with Secrets through runtime configuration.
  - Its restart helper refuses a baseline that already selects Secrets.

  Leaves 1 and 2 change the probe host (FR-002).
- **Test overlay:** one definition per leaf, applied to whichever of the normal, durable and probe hosts that leaf's cases need. It adds the leaf's test-only fixtures (FR-002).

## User Scenarios & Testing

Each story's Independent Test also covers:
- the **common controls** (FR-009);
- for each of its effects, the **common effect outcomes** (FR-014);
- the **canary search** (FR-023).

Every contributed tool and effect has a conformance-probe case on both provider adapters (FR-022). Each step runs in one host.

### User Story 1 - Explicit answers, the privilege policy and *performed* (leaf 0) (Priority: P1)

A user asks about a module that is not installed or is disabled, or about a page this feature does not support. Weaver answers from two live sources: the Studio module registry, which module load status shows, and the Weaver contributions each host registers. It never infers support from a route, or from M0's historical module list.

The user can also see which privilege classes the integrator has enabled, and each effect's classes in its review. An approved effect that persists nothing shows as *performed*.

**Why this priority**: Without it, every narrowing would be silent, and no later leaf could use the privilege policy, *performed* or module codes.

**Independent Test**:
1. In the normal host, run leaf 0's inventory refresh. ExtensionBuilder is disabled by default.
2. Deselect the structured logs features: the backend feature, its EF store and its Studio module. The EF store is its only dependant.
3. Ask Weaver about structured logs and the extension builder. Open `/diagnostics/structured-logs` and ask again.
4. Open the Weather sample and ask about the forecast.
5. Enable ExtensionBuilder: set its backend configuration, add its Studio module to Studio's shells, then open it and ask again.
6. In the probe host, use the fixture module. Leaf 0 extends it with:
   - an administrative effect;
   - an administrative and irrecoverable effect;
   - an effect that persists nothing;
   - a tool that declares a module-code set and emits a code outside that set, or a code that echoes a canary.
7. Propose each effect three ways:
   - with the privilege classes unconfigured;
   - with only administrative enabled, as the Workbench's base configuration does;
   - with all three classes enabled, as the Workbench's Development configuration does, and with a class switched off at each step: between review and approval, between approval and execution, and before recovery.
8. In the probe host, deselect the fixture module while a page nests its widget.
9. In the probe host's restart configuration, leave an effect pending, restart, remove the fixture module, and approve.

**Acceptance Scenarios**:

1. **Given** a module the registry does not list, **When** the user asks about it or opens its route, **Then** Weaver answers from the registry that it is not installed. The only context it discloses is the route.
2. **Given** a disabled module, or an enabled module without Weaver contributions, such as the Weather sample, **When** the user asks for help on its page, **Then** Weaver says so and offers no tool.
3. **Given** a deselected module, **When** a page that would nest its widget is open, **Then** nothing of that module is offered, and other modules' support is unchanged.
4. **Given** unconfigured privilege classes, **When** any classed effect is proposed, **Then** it is refused before review.
5. **Given** only the administrative class enabled, **When** an administrative and irrecoverable effect is proposed, **Then** it is refused before review. An administrative-only effect is reviewed.
6. **Given** all three classes enabled, **When** the effects are proposed, **Then** each one is reviewed, and its review shows its classes.
7. **Given** the host's bootstrap request, which carries no session, **When** it is answered, **Then** it reports every class's state, and Studio shows those states read-only.
8. **Given** an effect with a class that is off at the step being taken, **When** that step is approval, execution or recovery, **Then** the effect is refused at that step. At execution, nothing is dispatched.
9. **Given** an approved effect whose owning module reports that it persisted nothing, **When** the outcome is recorded, **Then** it is *performed* with the no-persisted-effect scope, and the review shell shows it.
10. **Given** a tool with a declared module-code set, **When** it emits a code outside the set, **Then** the outcome is reported with a generic code, and a canary in the code appears nowhere.
11. **Given** a pending effect whose module is removed, before or after a restart, **When** it is approved or recovered, **Then** it resolves as unavailable with zero effect (spec 1189).
12. **Given** an approval pending in a durable session from before leaf 0's upgrade, **When** it is approved after the upgrade, **Then** it is stale and refused, and never executed (FR-018).

---

### User Story 2 - Inspect, describe and test Secrets without exposing a value (leaf 1) (Priority: P1)

A user on the Secrets page (`/security/secrets`, or the alias `/secrets`) asks which secrets exist, why one is expired, or whether one is usable, or asks Weaver to improve a description.

Weaver reads each secret's metadata:
- name, display name and description;
- type, store, scope, tags and status;
- the effective condition, such as expired, and the latest version's expiry, both computed by the owning module (FR-006);
- current version, revision, and the created and updated times.

It never discloses a value. It can propose a metadata update, or the owning module's value test.

**Why this priority**: This is the smallest row with the highest exclusion risk, and it sets the pattern for the others.

**Independent Test**: In the normal host with leaf 1's test overlay, seed:
- an active secret, one whose latest version has expired, and a revoked secret, each holding a canary value;
- a secret whose test message echoes its value;
- a secret in another tenant.

Ask about the list on both routes and about each secret. Improve one description, test one secret, approve each, and compare each receipt with the owning module's readback.

Negative cases:
- an actor without `secrets:read`, for reads;
- an actor without `secrets:write`, and one without `secrets:test`;
- a target in another tenant;
- an edit or a rotation in another tab between review and approval;
- a test of a secret that does not exist or is inactive;
- the Secrets EF store deselected, so the repository cannot save at a revision;
- the Secrets API feature not composed;
- the administrative class off at each step;
- in the durable host, a restart with an approved effect pending;
- requests to create, rename, reveal, rotate, import, export, revoke or delete.

**Acceptance Scenarios**:

1. **Given** the list with a filter and a selection, **When** Weaver reads context, **Then** it receives the filter, the selected identities and their metadata, bounded by `secrets:read` and the tenant. An expired secret shows as expired.
2. **Given** an approved display-name or description update, **When** it is applied, **Then** the owning module saves it at the reviewed revision, and the receipt carries the readback.
3. **Given** a repository that cannot save at a revision, **When** an update is proposed, **Then** it is unavailable.
4. **Given** an approved test, **When** it runs at the reviewed version, **Then** it is *performed*. The receipt carries only success or failure and the module code. The value and the message are redacted.
5. **Given** a test that could not run, because the secret does not exist or is inactive, **When** the outcome is recorded, **Then** it is refused with the module code.
6. **Given** the Secrets API feature not composed, **When** a tool runs, **Then** the outcome is owner-unavailable (FR-006).
7. **Given** a request to create, rename, reveal, rotate, import or export, **When** Weaver receives it, **Then** it refuses on the grounds in FR-001.
8. **Given** a request to revoke or delete, **When** Weaver receives it, **Then** it says this is not yet supported, and offers no tool.

---

### User Story 3 - Service connections, and secret revoke and delete (leaf 2) (Priority: P1)

A user on the Connections page, in its creation form, or at a connection picker in a workflow asks what a connection is or why it fails. The user may also ask Weaver to prepare, update, rebind, test, revoke or delete a connection. On the Secrets page, the user may ask Weaver to revoke or delete a secret.

Through a new owning-module read (FR-006), Weaver reads:
- name, display name, type, authentication scheme and status;
- settings field names and secret-bound field names;
- managing party and Owner field;
- tags, last test time and version.

Bound secret names and named-credential names that the connection does not own are disclosed only under `secrets:read`. Otherwise only field names are disclosed. Settings values never are.

Weaver can propose:
- a prefill of the visible creation form;
- an update of the display name and tags;
- a rebind of a secret field;
- the owning module's test, which may contact an external service, depending on the type's provider;
- a connection revoke or delete;
- a secret revoke or delete.

**Why this priority**: "Why does my integration fail" is the most common Connections question.

**Independent Test**: In the normal host with leaf 2's test overlay, which provides a test connection type and a secret-repository save fault:
- The test connection type records each test invocation and carries canary token fields. Its test message echoes a resolved value.
- The save fault decorates the Secrets EF store and stays revision-aware.

Seed:
- a connection with a canary settings value, a canary secret value and canary token values;
- a connection whose field uses an inline secret that the connection owns;
- an active, an expired and an inactive text secret in the tenant;
- another connection's owned secret;
- a secret in the connection-owned name namespace for an OAuth grant;
- a connection in another tenant.

Explain each connection. Then prefill the form, update a connection, rebind a field, test it, revoke it and delete it. From the Secrets page, revoke and delete secrets that connections use. Approve each, and compare the receipts with the recorded invocation and the owning modules' readback.

Negative cases:
- an edit, or a user-run test that actually ran, between proposal and approval: both advance the version;
- a binding added or removed between review and approval of a secret revoke or delete;
- for reads, an actor with neither `workflows/design:read` nor `connections:manage`;
- an actor without `secrets:read`, for bound non-owned names and for a rebind; that rebind is refused before review, exactly as for an unknown secret;
- an actor without `connections:manage` (for updates, rebinds, revokes and deletes), one without `connections:bind-secret`, one without `connections:test`, and one without `secrets:delete`;
- the Connections API feature not composed;
- a target in another tenant;
- a rebind to an expired, inactive or non-text secret, or to another connection's owned secret;
- a rebind away from the inline secret, whose cleanup then fails after commit;
- a test that cannot run, a test that runs and fails, and a test whose write fails after the external call;
- a test whose message or module code echoes a resolved value;
- a dropped test response;
- a connection delete that removes owned secrets but then fails to remove the connection;
- a secret delete whose value is removed but whose metadata save fails;
- a consumer query that is truncated;
- a secret's revoke or delete review, for an actor who cannot read connections;
- a revoke or delete, from the Secrets page, of a secret in the connection-owned name namespace;
- the creation form closed before the prefill completes;
- the administrative or irrecoverable class off at each step;
- in the durable host, a restart with an approved effect pending;
- requests to create a connection, authorize OAuth, change the redirect URI, bind a field to a named credential, replace an owned inline secret's value, or set a settings value.

**Acceptance Scenarios**:

1. **Given** a connection, **When** Weaver reads it, **Then** it receives the metadata and version, and bound non-owned secret and named-credential names only under `secrets:read`.
2. **Given** the creation form, **When** Weaver reads it, **Then** it receives the type, scheme, field names and validation errors, and no entered settings or secret value.
3. **Given** an approved prefill, **When** it is applied, **Then** only the form's name, type and scheme change, and the prefill is applied locally. Creating the connection stays the user's action.
4. **Given** an approved update or rebind, **When** it is applied, **Then** it applies only at the reviewed version, under `connections:manage`. A rebind also needs `connections:bind-secret`, `secrets:read`, and an active text secret in the tenant outside the connection-owned name namespace. Candidates shown to the client grant nothing.
5. **Given** a rebind away from an inline secret the connection owns, **When** it is reviewed, **Then** it needs the administrative and irrecoverable classes. The review states that the secret is deleted after commit unless another connection still uses it, and that the deletion cannot be undone. If cleanup fails after commit, the effect is persisted with a warning.
6. **Given** an approved test, **When** it runs, **Then** the reviewed version is checked before the external call. When the type's test runs, the owning module records the test time and advances the version, and other pending proposals for that connection become stale. The receipt is persisted with whether the test ran, whether it succeeded, and the module code. The message is redacted.
7. **Given** a test that cannot run, **When** the outcome is recorded, **Then** it is refused with the module code. **Given** a test whose write fails after the external call, **Then** it is *uncertain*.
8. **Given** a connection revoke or delete, **When** it is reviewed, **Then** the review states that it cannot be undone, and that workflows using the connection will fail. For a delete, it also lists, by field name, the owned secrets that will be deleted. A secret that another connection still uses is kept and is not listed. Only other connections are checked.
9. **Given** a connection revoke or delete, **When** it is prepared and run, **Then** it needs the administrative and irrecoverable classes. It applies only at the reviewed version, which is checked before any side effect. A delete's receipt is an absence readback: the row is gone. A delete that removes owned secrets but then fails to remove the connection is *uncertain*.
10. **Given** a secret revoke or delete, **When** it is reviewed, **Then** for a revoke, the review states that the revoked version's value cannot be restored, and that rotation reactivates the secret with a new value. For a delete, it states that the delete cannot be undone.
11. **Given** a secret revoke or delete, **When** it is reviewed, **Then** it needs `secrets:delete` and the administrative and irrecoverable classes. The review lists, as advisory, the connections whose fields bind the secret. They are listed by name when the actor may read connections, and as a count otherwise, and a truncated query says so. The review states that workflow references, Nuplane feed credentials and OAuth grants are not detected.
12. **Given** a secret in the connection-owned name namespace, which covers owned inline secrets and OAuth grant secrets, **When** a revoke or delete is proposed from the Secrets page, **Then** Weaver refuses it before review. This is stricter than the native page: the Secrets module has no notion of connection ownership. The user can still act on the native page, or remove the binding from the connection first.
13. **Given** an approved secret revoke or delete, **When** it is approved and again when it runs, **Then** the advisory list and the namespace check are recomputed. A changed list makes the proposal stale. The reviewed revision is checked before any side effect.
14. **Given** a secret delete, **When** it runs, **Then** its receipt is an absence readback: the secret is hidden. If the value is removed but the metadata save fails, the outcome is *uncertain*, and it is never replayed.
15. **Given** the Connections API feature not composed, **When** a Connections tool runs, **Then** the outcome is owner-unavailable (FR-006).

### Edge Cases

- **A module unloads during a turn.** Its context is re-admitted without it. FR-010 covers host attribution and nested pickers.

## Requirements

### Functional Requirements

#### Delivery matrix

- **FR-001**: Leaf 0 MUST refresh the normal host's live installed and composed module inventory, because M0's loaded-module list is historical (#3443, 30 October). It records the result on #3443.
  - **M0 rows:** each one records M0's inventory, which the refresh confirms. A row the refresh finds absent or changed is updated by leaf 0's PR, either with ground A or with its new outcome.
  - **New screens:** a screen the refresh finds that the matrix lacks is added by leaf 0's PR, and assigned under FR-025.
  - **Outcomes and grounds:** every row MUST end at its outcome and evidence. A narrowing of its target, and each existing owner operation that Weaver does not offer, MUST state its grounds.

  Outcome words:
  - **Read:** read and explain.
  - **Reviewed:** a reviewed effect.
  - **Local:** a reviewed edit of visible, unsaved state.
  - **Delivered by #3442:** M3 delivered it.
  - **None in M4:** not supported in M4.

  Grounds:
  - **A:** absent from, or disabled in, the refreshed inventory.
  - **B:** the Program boundary: secret values; package promotion and package activation; module and feed administration; and the workflow run controls that spec 1193's Story 9 refuses. Feature selection apply is outside B where the integrator enables the activation class (owner decision 2).
  - **C:** a credential, file or value would pass through Weaver.
  - **D:** the owning module does not hold the setting durably or cluster-wide.
  - **H:** a human's decision.
  - **N:** no owner operation exists, or no server owner exists.
  - **O:** another issue, spec or open decision owns it.
  - **S:** the screen does not show it.
  - **T:** outside the row's target. For an M0 row, this is its M0 target. For a post-M0 row, it is the target stated here, a Program-authority choice that is open to owner review.
  - **V:** the owning module's write is reachable only from the browser, so the server cannot verify its result.
  - **W:** needs a module-owned working-state adapter.
  - **X:** needs its own exclusion design before any disclosure.

  This spec specifies the Weather, ExtensionBuilder, Secrets and Service connections rows. Every other row states its outcome here. Its leaf spec states its effects, contracts and scenarios.

  | Row | Owning module | Target | Outcome; narrowings (grounds) |
  | --- | --- | --- | --- |
  | Weather | Samples (Studio-only) | Qualify when installed (M0) | None in M4: A when absent; N when installed, because the Studio-only sample has no server owner to admit its context |
  | ExtensionBuilder | ExtensionBuilder | Optional inspection and guidance (M0) | None in M4: A while disabled; X once enabled, because its state includes workspace source |
  | Secrets | Secrets | Reference, metadata and availability inspection; domain effects (M0) | Read; Reviewed metadata update and value test (story 2); Reviewed revoke and delete (story 3). Value-bearing create and rotate (B); configuration-key create and rotate (T); rename, import and export (N); the workflow property's Secrets editor (O: #3711) |
  | Service connections | Connections | Metadata, bindings and diagnostics; changes and tests (M0) | Read; Local prefill; Reviewed update, rebind, test, revoke and delete (story 3). Settings values (X: no field declares its sensitivity); replacing an owned inline secret's value (B); create and OAuth authorize (C); redirect URI (N: a read-only public URL); binding a field to a named credential (T) |
  | Structured logs, traces | StructuredLogs, OpenTelemetry | Filters, records and correlation; trace, span and query (M0) | Read (its leaf spec). Time range and retention, metrics and OpenTelemetry logs (S) |
  | Diagnostics overview, console, module load status | Studio Hosting; `Elsa.Studio.ConsoleStream` (client) with console log streaming (server) | Visible results; console source and records; module state (M0) | Read (its leaf spec). Server reads of console records (X); lines the user attaches are the user's own client-asserted disclosure (FR-009); module administration (B) |
  | Home | Studio Dashboard, Studio Preferences, Attention, Workflows.Dashboard | Snapshots, filters and freshness; supported preferences (M0) | Read; Reviewed personal layout change and Attention Item snooze (its leaf spec). Default layout (T); new widgets and saved queries (O: #3604) |
  | Workflow Runtime Diagnostics Setting | Workflows Runtime | Explain the capture policy and exact proposed differences (M0) | Read; Reviewed change within today's capture rules (its leaf spec). Full payloads (O: Studio ADR 0009's open decision); a durable, cluster-wide setting (D) |
  | Activity availability | Activities Design | Effective availability and diagnostics; supported settings changes (M0) | Read; Reviewed change (its leaf spec) |
  | Features | FeatureManagement (client), Modularity (server) | Host, revision, filters and draft; reviewed differences (M0) | Read; Local draft; Reviewed Server-host apply in the activation class (its leaf spec). Studio-host apply (V); configuration values and edits (C) |
  | Appearance, theme builder | Studio Hosting, Hosting.Themes | Preference and theme edits (M0) | Read; Local appearance and builder edits (its leaf spec). Theme Store writes (V) |
  | Modules, feeds | Hosting.Nuplane (client: Studio Hosting) | Host, package, feed and reconcile state (M0) | Read (its leaf spec). Package and feed operations (B); feed credentials are never disclosed (#3735) |
  | Backends | Studio Backends | Read the profiles (post-M0) | Read (its leaf spec). Profile edits (V) |
  | Settings sections | Studio Hosting | The resolved section and host (#3443, 31 October refinement, item 4) | Read (its leaf spec) |
  | ETL definitions, connectors, runs | ETL | Drafts, filters, validation and authoring; run and partition; explicit cancellation (M0) | Read; Reviewed draft update and run cancellation (its leaf spec). Create (W); delete and start (N); publish (H); completing a run (T: the M0 target names only cancellation) |
  | Activity definitions | Activities Design; Workflows.Publishing (test runs, publish); Elsa3 (import) | Inspection; draft, import and upgrade proposals (M0) | Read; Reviewed draft add, update and discard, and upgrade plans (its leaf spec). Import (C); publish (H); other definition and version operations, and test runs (T); contract proposals (W) |
  | User tasks, forms | `Elsa.Studio.UserTasks` (client), Workflows Runtime (server) | Read; claim, release and reassign (post-M0) | Read; Reviewed claim, release and reassign (its leaf spec). Decisions (H); prefill values and decision responses (C); form authoring (W) |
  | Agents, reviews | Agent Specialists | Read (post-M0) | Read (its leaf spec). Review decisions (H); the identity of a review's decider (C); authoring (O: #3519) |
  | BPMN | Activities BPMN | Read (post-M0) | Read (its leaf spec). Run and work operations, artifact registration, interchange management (T); direct publish (H); process authoring (W) |
  | Expressions | Expression Modules | Language-owned guidance and exact expression edits (M0) | Read; Reviewed expression edits (its leaf spec, coordinated with #3711) |
  | New workflow | Workflows, Weaver.Workflows | Caller attachment; typed suggestions or reviewed creation (M0) | Delivered by #3442 |
  | Workflow rows: definitions, draft, templates, library and tags, versions, Executables, Runs | Workflows | M3 rows | Delivered by #3442. Run controls (retry, cancel, delete, re-dispatch) (B, per spec 1193 Story 9); library deletes and edits to published or historical versions and Executables (O: spec 1193) |

#### Packaging and ownership

- **FR-002**: **Server packages.** This spec decides, under Program authority, that there is one Agent package per owning module. #3428 allows optional integrations to be "separately packaged with their owning module"; the guards below rule out putting them inside Essentials owners.
  - **Name and path:** each package is named `Elsa.Agent.<Domain>`, at the path the architecture guard derives.
  - **Why the Agent bucket:** the ExtensionBoundary guard forbids every core project from referencing an extension. Framework §2.2's secondary-domain sub-rule covers features with no models of their own, and these packages define their own Context Kind, tool and result models.
  - **References:** a package references its owning module's `.Core` where one exists, and the Agent packages in its own bucket. It registers the server-side descriptor of every client tool its module offers.
  - **Owning modules without a `.Core`:** the package admits only client-asserted kinds. It exists only where SC-004's per-package deselection needs it.
  - **Cross-bucket edges:** where the owning module sits in another extension bucket, such as ETL or ExtensionBuilder, the reference is declared in the ExtensionBoundary guard's `DeclaredExtensionEdges`, with a justification comment.
  - **Leaf 1** adds `Elsa.Agent.Secrets`, which reads only Secrets `.Core`. It changes the probe host:
    - it packages the Secrets API;
    - it composes Secrets, the Secrets API, the Secrets EF store (on SQLite, like the probe's packaged EF store) and `Elsa.Agent.Secrets` in the baseline, so that revision-guarded effects can succeed there;
    - it adjusts the restart helper.
  - **Leaf 2** adds `Elsa.Agent.Connections`. It serves the Connections module, and is distinct from the existing `Elsa.Agent.Specialists.Connections`. Leaf 2 also adds Connections, the Connections API and `Elsa.Agent.Connections` to the probe host.
  - **Test overlays:** each leaf's test overlay is one definition, applied to the normal, durable and probe hosts as needed.
    - Leaf 1's overlay holds the test secret store.
    - Leaf 2's overlay holds the test connection type, and a secret-repository save fault that decorates the Secrets EF store and stays revision-aware. A second repository backend would be refused.

  `Elsa.Agent.Workflows.Runtime` already exists from #3442 slice F (#3732), and later leaves extend it.
- **FR-003**: **Studio packages.** A Studio contribution MUST live in its owning Studio module, through the public SDK.
  - **Satellites:** a satellite in the shipped form `Elsa.Studio.Weaver.<Domain>`, under `Extensions/Weaver/studio/`, is added only where a host must be able to keep the module but drop its Weaver support. As in FR-002, framework §2.2's sub-rule does not apply, because a satellite defines its own renderers and Weaver-facing models.
  - **Built-in Studio Screens** are such a case, because every host keeps the shell. They are owned by `Elsa.Studio.Hosting`, and their contributions go in `Elsa.Studio.Weaver.Hosting`.
  - **Leaf 4**, the first leaf that needs that package, creates it with its SDK read seams.
- **FR-004**: **Result Renderers and module guidance.** Each module MUST ship, in its own packages, the Result Renderers for its tool result kinds, and its own bounded, versioned module guidance. Module guidance is trusted installed-module text, kept distinct from resource content. Shared Weaver code holds neither.

#### Generic changes

- **FR-005**: Weaver core, the shell's collector and review shell, and the provider adapters MUST gain no row's domain knowledge. The generic changes are:
  - the privilege policy (FR-015), including each class's state in the host's bootstrap response;
  - the review shell showing an effect's classes and the *performed* outcome;
  - *performed*, with its no-persisted-effect receipt scope and the matching executor and integrity validation (FR-016);
  - declared module-code sets, and their validation (FR-016);
  - a principal seam that hands a tool the caller's authenticated principal at preparation, approval and execution, and at the explicit authenticated execute that runs a recovered approval. Today, a tool's authorization receives only the actor, the tenant and the target;
  - the registry-based answer of story 1;
  - the probe fixture's classed, *performed* and module-code effects;
  - doc comments on the risk enum that match FR-013;
  - SDK read seams for `Elsa.Studio.Weaver.Hosting`, delivered by leaf 4 (FR-003);
  - new outcome codes, only where no existing code fits.

  Leaf 0's PR delivers all of these except the SDK read seams. It also adds a dated one-line pointer, "Amended by spec 1203", to spec 1186's session-policy contract and to spec 1189's recovery contract.

#### Owner prerequisites

- **FR-006**: Where a row needs a read, an authorization or an operation that today exists only behind the owning module's HTTP endpoints, or outside its `.Core`, the owning module MUST supply it in a form that Weaver can call under the same principal.
  - **Shape:** a `.Core` contract, implemented in the owning module's `.Api` package, which already references the identity contracts.
  - **Authorization:** the contract takes the caller's authenticated principal from the principal seam (FR-005). It checks that the actor matches that principal, and decides permissions through `IPermissionAuthorizationService`.
  - **Missing composition:** if the `.Api` feature or the authorization service is not composed, the contract is absent and the outcome is owner-unavailable. A permission gate that defaults to allowing everything is never relied on.
  - **Exceptions:** infrastructure exceptions are wrapped (framework §2.23.5).
  - **Writes:** a write to an existing resource takes an expected revision and checks it before any side effect. A composition that cannot honor the revision reports the write as unsupported, so the write never runs unconditionally. A create, where no resource exists, uses the owning module's create-if-absent.
  - **Native pages:** the shipped native pages keep working, each with a regression test.

  The named types are contract decisions. The plan owns their signatures.

  | Owning module | Prerequisite |
  | --- | --- |
  | Secrets (leaf 1) | A metadata read with the revision, the effective condition and the latest version's expiry. A revisioned metadata update. A value test at an expected version that reports whether it ran. |
  | Secrets (leaf 2) | Revoke and delete at the revision, each unavailable without a revision-aware repository. A check for the connection-owned name namespace (`connection:{name}:…`). A fan-in (framework §2.6.1): the source contract `ISecretConsumerSource` and the named Sequential event `SecretConsumersCollecting`, which carries the caller's principal, both in Secrets `.Core`; and its single aggregating handler, `CollectSecretConsumers`, in `Elsa.Secrets`. Secrets `.Core` gains a reference to `Elsa.Events.Core` for the event. |
  | Connections (leaf 2) | A read that adds the version and the bindings, omits settings values, and shows bound non-owned and named-credential names only under `secrets:read`. An update (display name and tags) and a rebind, each at an expected version. The rebind checks `secrets:read`, refuses as for an unknown secret, and refuses any secret in the connection-owned name namespace. A test at an expected version, checked before the external call, whose result adds a "ran" flag and a module code. Revoke at an expected version. A delete that checks the version before deleting any owned secret, then removes the row with the repository's version check. A bounded repository query that lists the connections whose fields bind a given secret, with an explicit truncation flag. An `ISecretConsumerSource` implementation in `Elsa.Connections.Api`, which gives names or a count under the Connections read rule (`workflows/design:read` or `connections:manage`). |

- **FR-007**: **Read authority.** A rule that is exactly one permission MAY be checked through the tool's declared permission, which `IPermissionAuthorizationService` decides for the caller's principal. Any other rule MUST be decided by the owning module's contract, never reimplemented in a Weaver package. A review that shows another module's names follows that module's read rule, and otherwise shows a count (story 3, scenario 11).
- **FR-008**: Deployment shapes MUST keep working (constitution §E2.2.3). Design-side contributions register only where Design is composed, and Runtime-side contributions only where Runtime is. An absent side is explicitly unavailable.

#### Context and disclosure

- **FR-009**: **Common controls.** Every Context Kind and tool MUST have a negative case for each control below, tested at the cheapest level that proves it (ADR 0080 D1, framework §2.23):
  - **Module removed:** nothing of the module is offered.
  - **Denied, or a cross-tenant target:** nothing is disclosed, not even existence. A denied effect is refused before review.
  - **Owner-unavailable:** nothing from that owning module is disclosed.
  - **Stale target:** zero effect, and navigation never retargets.
  - **Excluded data:** seeded canaries appear nowhere.
  - **Untrusted content:** resource content and every client-asserted kind are marked untrusted, distinct from module guidance.

  Content the user explicitly attaches, such as selected console lines, is the user's own disclosure. It is client-asserted and untrusted, and it is excluded from the canary search.
- **FR-010**: Each Context Kind MUST be module-owned, versioned, bounded and authorized. It names the host it describes. It is attributed to an exact identity:
  - the owning module's revision, where one exists;
  - for a list, the visible query and selection;
  - for unsaved state, the client binding and base revision.

  Server context providers admit only their own kinds, and produce their permission and catalog kinds from their own reads. A tool never takes data from another package's kind. A nested picker contributes its own kind from its own module: the Connections picker in a workflow property form is a Connections contribution. A form's own fields belong to the form's module. A nested picker's context never changes a pending Operation Batch's target.
- **FR-011**: Each Context Kind MUST declare its bounds. Each leaf adds tests proving two things: its densest pages, nested pickers included, stay within the attachment count at the largest set each page shows by default; and each kind stays within the per-attachment bounds. A session that reaches its disclosure budget is refused, as that budget defines.
- **FR-012**: Each disclosure allowlist MUST be authored by the module that defines the data's meaning, and ship in that module's Agent package. Unlisted fields are excluded by default. Each allowlist is proven by a canary in an unlisted field that the owning module's own redaction does not already handle.

#### Effects

- **FR-013**: Every effect MUST declare its risk, its privilege classes, its stale check and its receipt, and each leaf spec states them explicitly.
  - **Risk order:** risks follow the risk enum's order: ReadOnly < ReviewRequired < Destructive < Admin. Destructive means the effect removes or replaces existing state. Admin means it touches sensitive or administrative state.
  - **Ownership of classification:** [ADR 0003](../../docs/adr/0003-weaver-direct-apply-risk-classification-is-elsa-owned.md) keeps risk classification Elsa-owned. Gating comes from the classes (FR-015).
  - **Client working-state effects:** spec 1193's client working-state Destructive batch operations are unaffected.

  For leaves 1 and 2:

  | Effect | Leaf | Risk | Privilege classes | Stale check | Receipt |
  | --- | --- | --- | --- | --- | --- |
  | Secret metadata update | 1 | `Admin` | administrative | revision | readback; persisted |
  | Secret value test | 1 | `Admin` | administrative | version | *performed*, with success or failure and the module code; refused when it could not run |
  | Secret revoke | 2 | `Admin` | administrative, irrecoverable | revision; advisory list and namespace, recomputed | readback; persisted |
  | Secret delete | 2 | `Admin` | administrative, irrecoverable | revision; advisory list and namespace, recomputed | absence readback; persisted; *uncertain* when partial |
  | Connection prefill | 2 | `ReviewRequired` | none | client binding | applied locally |
  | Connection update, or a rebind of a field whose secret the connection does not own | 2 | `Admin` | administrative | version | readback; persisted |
  | Rebind of a field whose inline secret the connection owns | 2 | `Admin` | administrative, irrecoverable | version | readback; persisted, with a warning if cleanup failed |
  | Connection test | 2 | `Admin` | administrative | version, checked before the external call | persisted, with whether the test ran, its result and the module code; refused when it could not run; *uncertain* when the write failed after the external call |
  | Connection revoke | 2 | `Admin` | administrative, irrecoverable | version | readback; persisted |
  | Connection delete | 2 | `Admin` | administrative, irrecoverable | version | absence readback; persisted; *uncertain* when partial |

- **FR-014**: **Common effect outcomes.** Every effect MUST be tested for each of these, at the cheapest level that proves it:
  - **Review:** it is reviewed in every autonomy mode, including `full-auto`.
  - **Refusal at a step:** the policy is read again at every step. An effect is refused at preparation, approval, execution or recovery whenever one of its classes is off at that step. A refusal at one step does not carry over: the next step reads the policy afresh. At execution, nothing is dispatched. An effect without the owning module's permission is refused before review.
  - **Owner unavailable at approval:** owner-unavailable, with zero effect.
  - **Module removed at approval:** unavailable, with zero effect.
  - **Restart:** in the durable host, a pending approval survives a restart. A recovered approval runs only on an explicit authenticated execute, which rechecks the revision, the permission and the classes.
  - **Lost or uncertain response:** an effect whose response is lost after dispatch, or which may have run before a restart, is *uncertain*. It is never replayed, and the session owner resolves it, as spec 1189's recovery contract defines.
  - **Local edits:** these carry correlated acknowledgements, and are unavailable with zero effect if their page is gone.
- **FR-015**: **Privilege policy** (owner decisions 1, 2 and 4). The Agent module MUST gain an integrator-configured privilege policy. It is a backend host option, never a session setting. It has three classes:
  - **administrative:** every `Admin` effect requires this class. A module may add classes to an effect, but never remove this one.
  - **irrecoverable:** a module MUST declare this class for its own irrecoverable effects, as leaves 1 and 2 do. An irrecoverable third-party effect that is not `Admin` and declares no class is not caught. This is a recorded limitation.
  - **activation:** feature selection apply on the Server host.

  The policy MUST also behave as follows:
  - **Class sets:** each effect declares a set of classes, and needs every class in it.
  - **Declaration:** the owning module declares the set at preparation, because core cannot infer consequence. The set is frozen into the approval digest.
  - **Defaults:** every class is off by default, so a host that leaves the policy unconfigured refuses every classed effect. The Workbench, as the reference integrator, enables the administrative class in its base configuration (owner decision 4), and the irrecoverable and activation classes in its Development configuration only (a Program-authority choice, listed under Assumptions).
  - **Enforcement:** the policy is read live at each step (FR-014).
  - **Display:** Studio shows the policy read-only, as part of its Host Policy, from the host's bootstrap response.
- **FR-016**: **Outcomes.**
  - ***Performed*:** leaf 0 MUST add *performed* (owner decision 3). The approved action ran, and the owning module persisted no change.
    - It is a new proposal status, and it reports the action as executed.
    - Its receipt uses a new effect scope, `NoPersistedEffect`, and carries only what the owning module reports.
    - The executor's validation and the invocation integrity check accept that scope for *performed*, and only for *performed*.
    - Every server and Studio status mapping handles *performed*. The plan names the sites.
  - **Module codes:** a tool's descriptor MAY declare a closed set of module codes, and every M4 tool declares one.
    - For a tool with a declared set, Agent core validates every code in receipts, refusals and preparation errors. A code outside the set is reported as a generic code.
    - Each `Elsa.Agent.<Domain>` package maps its owning module's native codes into its set. This also resolves native collisions, such as the two "not found" codes of the Secrets manager and its configuration store.
    - Mapped codes are dotted ASCII identifiers outside `agent.`. A receipt carries its code in a new member, `ModuleCode`.
    - Tools that declare no set pass their codes through unchanged, and existing tools' codes are unchanged.
  - **Existing outcomes:** every other outcome reuses the existing outcomes and codes. *Uncertain* keeps its shape and generic message, and the session owner resolves it (spec 1189). A new code is added only where none fits, such as the privilege-policy refusal. A status alone never stands as evidence of an effect.

#### Contracts, catalogs and records

- **FR-017**: Replacement contracts MUST detect a second registration (framework §2.6.2). The Agent-wide gap for existing contracts remains #3633. Visibility follows framework §2.23.3 and ADR 0080 D3.
- **FR-018**: **SemVer.** Every change MUST be classified under framework §4.2. Pre-GA Majors follow ADR 0080 D4 and Elsa constitution §E5, as spec 1193 FR-033 did.

  **Major:**
  - *performed*: the new proposal-status value and its string, the new `NoPersistedEffect` effect-scope member, and the executor and integrity validation. Together these change model semantics, and owner decision 3 amends them.
  - the privilege policy, for two reasons:
    - with every class off by default, it refuses `Admin` tools, including third-party tools, that ran before;
    - freezing the class set into the approval digest makes approvals that are pending in durable sessions across the upgrade stale. They are refused, and never executed (story 1, scenario 12).

  **Minor:**
  - opt-in module-code sets, and the `ModuleCode` receipt member;
  - new members on existing stores and contracts, added so that existing implementations keep compiling (the plan names the mechanism);
  - Secrets `.Core`'s new reference to `Elsa.Events.Core`;
  - new contracts, events, model members, packages, Context Kinds, tools, renderers, codes and permissions.

  Each leaf spec classifies its own changes by these rules.
- **FR-019**: **Catalogs** MUST change in the same unit of work as the code (framework §2.22.1):
  - **Leaf 0:** `Extensions/Agent/src/Elsa.Agent/EXTENSION_POINTS.md` gains the privilege policy, class sets, module-code sets, the principal seam, *performed* and its effect scope.
  - **Leaf 1:**
    - `Essentials/src/Secrets/Core/EXTENSION_POINTS.md`;
    - `Essentials/src/Secrets/Elsa.Secrets/EXTENSION_POINTS.md`;
    - `Essentials/src/Secrets/Persistence/EntityFrameworkCore/EXTENSION_POINTS.md`.
  - **Leaf 2:**
    - the Secrets Core catalog gains the `SecretConsumersCollecting` event and the `ISecretConsumerSource` contract, with the Connections implementation recorded as a cross-domain Known implementation;
    - the `Elsa.Secrets` catalog gains the `CollectSecretConsumers` handler;
    - `Essentials/src/Connections/Api/EXTENSION_POINTS.md`, and a "Cross-domain contributions" note in its README;
    - `Essentials/src/Connections/Core/EXTENSION_POINTS.md`;
    - `Essentials/src/Connections/Elsa.Connections/EXTENSION_POINTS.md`;
    - `Essentials/src/Connections/Persistence/EntityFrameworkCore/EXTENSION_POINTS.md`, for the new bounded query.
  - **Leaf 4:** `Studio/src/essentials/Elsa.Studio.Api/EXTENSION_POINTS.md` gains the SDK read seams.
  - **Every leaf:**
    - its contributors, as cross-domain Known implementations in the Agent catalog;
    - each new package's own catalog, where it exposes extension points;
    - the root index (framework §2.22.2);
    - each package README, listing its cross-domain contributions.
- **FR-020**: **Records.** Evidence goes on each PR, on #3443 and on #3428, which owns the matrix and evidence ([program goal](../../docs/program-goals/weaver-across-studio.md)).

  Leaf 0 MUST file these follow-ups as `needs-triage` (AGENTS.md; [repository hygiene](../../docs/contributing/repository-hygiene.md#issues)):
  - the ExtensionBuilder exclusion design (X);
  - field-level sensitivity for connection settings values (X);
  - a server-verifiable path for browser-only writes (V);
  - a durable, cluster-wide Workflow Runtime Diagnostics Setting (D);
  - console record reads (X);
  - a log time range and retention view (S);
  - working-state adapters for ETL creation, task forms, BPMN processes and activity contract proposals (W);
  - irrecoverable-class coverage for third-party effects that declare no class (FR-015);
  - detection of workflow, feed-credential and OAuth-grant secret consumers (story 3, scenario 11).

  These rules apply to the follow-ups:
  - Follow-ups that are this Program's work are triaged under the 29 October delegation.
  - Every follow-up is recorded on #3428.
  - A follow-up in a parked program's area is filed without a parent.
  - Nothing that already exists is filed. Context-budget progress goes on #3710, and Full payloads are recorded as Studio ADR 0009's open decision.

  Leaf 0 also records on #3428:
  - every narrowing;
  - the deferrals to other programs: #3604, and #3519's authoring;
  - the decisions open to owner review.
- **FR-021**: **Glossary.** Leaf 0 MUST make these glossary changes.
  - **In the [Elsa glossary](../../docs/glossary/elsa.md)**, beside its Weaver and agent terms, add the following entries, each linked from Studio/CONTEXT.md:
    - *Privilege Class* (FR-015);
    - *Performed*, beside a pointer to spec 1186's outcome definitions (FR-016);
    - *Module code* (FR-016);
    - *Secret consumer*.
  - **In Studio/CONTEXT.md:**
    - *Module Load Status*: replace its state list, "discovered, accepted, activated, disabled, or rejected", with the states the host reports: available, loaded, disabled, incompatible and failed, plus the registry's pending and unknown. Its _Avoid_ line, "Diagnostics signal, observability widget", stays.
    - *Privileged Tool*: widen it to a Weaver Tool whose effect needs a privilege class.
    - Add *Built-in Studio Screen*: a screen that the `Elsa.Studio.Hosting` client renders itself.
    - Add *Client-Asserted Kind*: a Context Kind whose content only the browser supplies. It is untrusted, confers no authority, and the server checks only its schema and bounds.

#### Evidence

- **FR-022**: Each story MUST have end-to-end evidence. Fixture cases run in the probe host, and the restart cases of stories 2 and 3 run in that leaf's durable host. Every contributed tool and effect MUST have a conformance-probe case on both provider adapters: Copilot's delegated loop and Anthropic's host-driven loop. Negative cases are tested at the cheapest level that proves them.
- **FR-023**: The canary search MUST cover HTTP bodies, SSE frames, adapter payloads, tool results, module codes, review cards and audit records.
- **FR-024**: Evidence MUST keep three categories distinct:
  - deterministic component tests;
  - deterministic provider-protocol native evidence on the actual Copilot and Anthropic adapters, with packaged consumers;
  - opt-in live-provider evidence (none claimed).

  Probe evidence is frozen at the exact reviewed head of its PR. Failures, skips and quarantines are recorded as they occurred.

#### Later leaves

- **FR-025**: Each later leaf MUST get its own spec, numbered at the time with the next free number. It is written and reviewed when the previous leaf merges, because #3428 elaborates later leaves only when their slice is concrete. A leaf spec is bound by FR-002 to FR-024 and by its FR-001 rows. It specifies those rows' effects, owner prerequisites, risks, classes and scenarios.

  Leaf 0's PR assigns each row its refresh adds, either to an existing leaf or to a new one. #3443 cannot close while a row is unassigned.

  | Leaf | FR-001 rows | Scope |
  | --- | --- | --- |
  | 3 | Structured logs, traces | allowlisted log and trace reads; correlation |
  | 4 | Diagnostics overview, console, module load status | screen reads; console lines the user attaches; creates `Elsa.Studio.Weaver.Hosting` and its SDK read seams |
  | 5 | Home | widget snapshots, personal layout, snooze |
  | 6 | Workflow Runtime Diagnostics Setting, activity availability | host-scoped settings changes |
  | 7 | Features, appearance, theme builder | feature draft and Server-host apply; local appearance and builder edits; the *Theme Selection Preference* glossary widening |
  | 8 | Modules, feeds, backends, settings sections | administration reads |
  | 9 | ETL | exact design and runtime reads |
  | 10 | ETL | draft update and run cancellation |
  | 11 | Activity definitions | reads, drafts and upgrade plans |
  | 12 | User tasks, agents, BPMN | reads; task claim, release and reassign. That leaf's spec escalates reassign if it stays `Admin` |
  | 13 | Expressions | language-owned guidance and exact expression edits, coordinated with #3711 |

### Key Entities

- **Coverage row:** an inventory screen with its owning module, target, outcome, narrowings and evidence (FR-001).
- **Disclosure allowlist:** the fields whose values may be disclosed, as authored by the owning module (FR-012).
- **Effect:** a reviewed operation, with its risk, privilege classes, stale check, receipt and common outcomes (FR-013, FR-014).
- **Narrowing:** a recorded gap between a target and the delivered outcome, with its grounds (FR-001).

## Success Criteria

### Measurable Outcomes

- **SC-001**: Every FR-001 row has linked evidence on #3428, either from this spec's leaves or from its leaf spec. Every narrowing has its grounds, and no row is unassigned.
- **SC-002**: Stories 1–3 pass their acceptance scenarios. The hosts are:
  - the normal host for the read and effect cases;
  - the durable host for the restart cases of stories 2 and 3;
  - the probe host for the fixture cases and for every both-adapter case.

  A host that leaves the privilege classes unconfigured refuses every classed effect.
- **SC-003**: Seeded canaries appear in none of the channels FR-023 lists. Content the user explicitly attaches is excluded from the canary search (FR-009).
- **SC-004**: For each contributing package, a composition test that deselects it removes exactly that package's contributions, and leaves every other row's contributions unchanged.
- **SC-005**: Each leaf PR's review evidence includes a diff check that lists the changed projects against FR-005's list. It shows that Weaver core, the shell's collector and review shell, the provider adapters and other modules changed only through those generic changes.
- **SC-006**: After leaf 0's generic changes, the independent package witness is re-run and passes. The [program goal](../../docs/program-goals/weaver-across-studio.md) requires later lifecycle changes to have their own qualification.
- **SC-007**: The native-page regression tests of FR-006 and the attachment tests of FR-011 pass.
- **SC-008**: Each leaf PR passes the CI and review gates, as spec 1193 SC-010 did: the [merging](../../docs/contributing/merging.md) PR evidence, the Elsa constitution §E7 test-tier manifest for each new test project, locked-mode restore, and the solution filters.

## Assumptions and Boundaries

- **Delivery:** each leaf is its own independently reviewed PR. Leaf PRs say "Refs #3443", and the closing leaf says "Fixes #3443". This is an exception that follows spec 1193's Refs-then-Fixes precedent.
  - **Order:** leaves 1 and 2 depend on leaf 0, and leaf 2 builds on leaf 1.
  - **Status:** the PR that merges this spec sets *Approved*. Leaf 0's PR sets *In progress*. The closing leaf's PR sets *Implemented* ([spec lifecycle](../../docs/reference/spec-lifecycle.md)).
  - **Bookkeeping:** every leaf comments on #3428 and #3443 at each state transition, and reconciles #3443 and its Project item (AGENTS.md program bookkeeping).
- **Program owner decisions, 2026-11-03** (Sipke Schoorstra; [receipt on #3428](https://github.com/elsa-workflows/elsa-foundation/issues/3428#issuecomment-6612627295)). The receipt says "spec 1202" because it was written before the spec was renumbered; it means this spec ([#3443 note](https://github.com/elsa-workflows/elsa-foundation/issues/3443#issuecomment-6614698826)).
  1. **Irrecoverable deletes and revokes** of secrets and Service connections: "Let the integrator decide what privileges weaver has via configuration." These use the irrecoverable class (FR-015).
  2. **Feature selection apply (activation):** "Let the integrator decide via configuration." It uses the activation class on the Server host. This widens #3443's activation boundary ("a manage claim alone cannot expand it", 31 October), but only for hosts that enable the class.
  3. **A generic outcome for an approved action that persists nothing:** "Add it in leaf 0." This is *performed* (FR-016).
  4. **The privilege default:** "Off; Workbench always on." Every class is off by default, and the Workbench enables the administrative class in its base configuration.
- **Decisions taken under Program authority, open to owner review** (as spec 1193 records its own):
  - **Packaging:** one Agent package per owning module (FR-002).
  - **Workbench Development configuration:** the irrecoverable and activation classes are enabled there, and only there.
  - **Class sets:** they are declared by consequence, satisfied in full, and frozen into the approval digest. Every `Admin` effect requires the administrative class.
  - **Secret revoke and delete:**
    - The consumer list is advisory, and names only what Connections can report.
    - The review states which consumers are not detected.
    - Secrets in the connection-owned name namespace are refused before review, which is stricter than the native page.
    - The list and the namespace check are recomputed at approval and at execution.
  - **Module codes:** declared, closed sets, mapped by each Agent package.
  - **Weather:** None in M4, because the Studio-only sample has no server owner.
  - **ExtensionBuilder:** narrowed from M0's "optional inspection and guidance" to None in M4, with ground X once enabled.
  - **Console:** server reads of records are narrowed (X); only lines the user attaches are disclosed.
  - **Workflow run controls:** these stay refused, under ground B, as spec 1193's Story 9 decided.
  - **Expressions:** the Expressions leaf delivers the M0 target, coordinated with #3711.
  - **Post-M0 targets:**
    - User tasks get read, claim, release and reassign, with reassign flagged for escalation.
    - Agents, BPMN and backends are read-only.
  - **Runtime capture semantics:** these stay untouched, and Full payloads wait for Studio ADR 0009.
  - **Browser-only writes:** these are narrowings (V). They are Theme Store writes, backend profile edits and the Studio host's feature apply.
  - **Home layouts:** personal layout changes are delivered, and the default layout stays administration (T).
  - **ETL and BPMN cancellation:** ETL cancellation is reviewed, because its M0 target names it. BPMN run operations are outside BPMN's post-M0 target (T).
- **Plan decisions:**
  - **P1:** each package's shell feature.
  - **P2:** the signatures of the FR-006 contracts and the principal seam, the revision mechanisms, and where native permission constants live.
  - **P3:** every non-atomic compare-then-write window, and how it is bounded.
- **Overlap**, reconciled without reparenting:
  - ETL: #2894, #2906 and the open ETL defects (#3709, #3719, #3721, #3724 and #3562).
  - Home dashboards: #3604.
  - The parked Diagnostics Observability Readiness and Runtime Execution Evidence goals stay parked.
  - User Tasks: #3688, which is complete.
  - Studio Expression Developer Experience: #2310, with #3711 and #2349. The Expressions leaf adds no children under #2310.
  - Elsa 3.8/3.9 Parity: #2771, which owns Elsa 3 import.
  - The [BPMN Engine program](../../docs/program-goals/bpmn-engine.md).
  - Agents: #3519. Runtime agents: #2892.
  - Weaver: #3710, #3720 and #3761.
  - Feed credentials: #3735.
- **Out of scope:**
  - releases, deployments and provider purchases;
  - live-provider credentials;
  - blanket administration and arbitrary workspace edits;
  - package promotion and package activation. Feature selection apply is in scope only through the activation class.
  - integrated qualification, which remains #3444.

## Issue Traceability

| #3433 criterion | Requirements and stories |
| --- | --- |
| Home/dashboard, ETL, Connections, Secrets metadata, Diagnostics, runtime evidence, activity availability and installed settings/administration surfaces have module-owned support | FR-001 to FR-004, FR-025; stories 2–3 |
| Each supported action has context, tool, renderer/effect and negative-case evidence appropriate to its domain; absent mutation support is explicit | FR-001, FR-004, FR-013, FR-014, FR-022 |
| Secret values and unrelated data are not exposed; runtime/model semantics and existing domain ownership remain intact | FR-006, FR-007, FR-009, FR-012; Assumptions (runtime capture semantics); FR-002. Owner decision 3 amends the model semantics for *performed* |
| Scope is derived from the actual installed/composed module inventory, including nested surfaces and optional module removal; no absent module is claimed supported | FR-001 (inventory refresh), FR-008, FR-010; story 1; SC-004 |

| #3443 acceptance or task | Requirements and stories |
| --- | --- |
| Each M0 row becomes an owning-module context, tool and outcome entry with acceptance and negative-case evidence | FR-001, FR-025; stories 1–3; SC-001, SC-002 |
| Cover Home, ETL, Connections, Secrets metadata, Diagnostics, runtime evidence, activity availability and installed settings and administration surfaces, including nested contexts | FR-001, FR-010, FR-025; stories 2–3, with nested contexts in story 3 |
| Same generic contribution path; module knowledge, renderers and guidance in the owning module or its optional package | FR-002 to FR-008; SC-004 to SC-006 |
| Read and explain where mutation is unavailable; review and apply where allowed; secret values and unrelated data excluded | FR-012 to FR-016; stories 2–3; SC-003 |
| Normal-host composition, disabled or removed modules, authority and stale targets; no claim for absent modules | FR-001, FR-008, FR-009, FR-014, FR-022 to FR-024; story 1; SC-002, SC-004 |
| Per-module leaves; reconcile overlap without reparenting | FR-025; Delivery; Overlap; FR-020 |
| Boundaries: finite inventory; no blanket administration, workspace editing, promotion or activation | FR-001, FR-015; owner decisions 1, 2 and 4 |
| Task: refresh source, owning-module contracts, claims and inputs | FR-001 (inventory refresh), FR-006, FR-007, FR-025; plan decision P2 |
| Task: scoped implementation and evidence | FR-001, FR-020, FR-022 to FR-024 |
| Task: review and CI gates; reconcile the issue and Project | SC-008; Delivery; FR-020 |
