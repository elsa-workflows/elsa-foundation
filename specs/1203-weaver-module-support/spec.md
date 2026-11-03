# Feature Specification: Module-owned Weaver support for the current Studio inventory

**Feature Branch**: `agent/claude/3443-weaver-module-support`

**Created**: 2026-11-03

**Status**: Draft

**Input**: [Feature #3443](https://github.com/elsa-workflows/elsa-foundation/issues/3443) is M4 ([#3433](https://github.com/elsa-workflows/elsa-foundation/issues/3433)) of [Program #3428](https://github.com/elsa-workflows/elsa-foundation/issues/3428), and it is [in progress](https://github.com/elsa-workflows/elsa-foundation/issues/3443#issuecomment-6612633571).

The Program owner's standing authorization covers autonomous delivery of #3443's six acceptance criteria, within the issue's boundaries as the owner decisions under Assumptions amend them. The current inventory is the finite matrix. The authorization grants no blanket administration permission, no arbitrary workspace editing, and no package promotion or activation. Consequential decisions outside that scope go to the Program owner (#3428, 29 October continuation).

The scope is:
- the M0 inventory and its targets in [docs/reports/weaver-baseline.md](../../docs/reports/weaver-baseline.md), including Activity definitions;
- the issue's 30 and 31 October planning comments;
- the rows the [claim](https://github.com/elsa-workflows/elsa-foundation/issues/3443#issuecomment-6605270638) adds: `/user-tasks` and `/agents`;
- the screens merged since: `/settings/backends` (PR #3600) and the BPMN pages (PR #3603).

Prerequisite #3440 is Done. All leaf code, leaf 0 included, waits for #3442's last slice ([spec 1193](../1193-weaver-workflow-journeys/spec.md)), because the Program runs one implementation leaf at a time.

**What this spec covers**:
1. **The M4 delivery matrix (FR-001):** every row, with its target, outcome and grounds.
2. **The cross-cutting requirements** that bind every leaf.
3. **Leaves 0–2, fully specified:**
   - leaf 0 is story 1, the generic changes;
   - leaf 1 is story 2, Secrets;
   - leaf 2 is story 3, Service connections.

Each later leaf gets its own spec (FR-025). The plan writes this spec's contracts.

**Review**: Twelve review rounds on 2026-11-03, each with two independent lenses (correctness and standards). Every finding was verified against `origin/main`. The Program owner's decisions of the same day are recorded under Assumptions.

**Terminology**:
- **Studio terms:** *Weaver*, *Weaver Tool*, *Context Kind*, *Operation Batch*, *Result Renderer*, *Host Policy*, *Module Load Status*, *Privileged Tool* and *Workflow Runtime Diagnostics Setting* keep their meanings in [Studio/CONTEXT.md](../../Studio/CONTEXT.md). FR-021 lists the glossary changes this spec makes.
- **Service connection** keeps its meaning in the [Elsa glossary](../../docs/glossary/elsa.md).
- **Outcome words:**
  - [Spec 1186](../1186-weaver-effects/spec.md) defines *proposed*, *approved*, *persisted*, *refused* and *uncertain*.
  - #3428 and the [program goal](../../docs/program-goals/weaver-across-studio.md) define *applied locally*.
  - Leaf 0 adds *performed*: the approved action ran, and the owning module persisted no change to its resource.
  - A conflict is *refused* when nothing changed, and *uncertain* when something may have.
- **Naming:** domain nouns keep their current names, such as Attention, until [ADR 0080 D5](../../docs/adr/0080-elsa-4-simplification-decisions.md#d5--plain-language-renamed-now) renames them in that domain's batch. New contributor-style interfaces use the `I<X>Extension` suffix now (ADR 0080 D5; framework §2.6.1; Elsa §E6 R4).
- **"Owner"** always comes with a qualifier:
  - the *owning module* owns a row's resource;
  - the *Program owner* is Sipke Schoorstra;
  - the *session owner* resolves an uncertain effect ([spec 1189](../1189-weaver-recovery/spec.md));
  - a Service connection's *Owner field* is part of its metadata.
- **Owner code:** a code that an owning module reports in a receipt. It is a dotted ASCII identifier outside the `agent.` namespace, and the Agent module validates it.
- **Owner-unavailable:** the outcome when a tool's or context's owning module cannot decide. It carries the owning module's code and no cause text.
- **Host attachment budget:** the limits Weaver's context admission enforces ([#3710](https://github.com/elsa-workflows/elsa-foundation/issues/3710)): a count of attachments per collection, plus per-attachment bounds. Overflow denies the whole collection. There is also a session disclosure budget, which caps the total disclosed per session.

**Hosts**:
- **Normal host:** the Workbench backend (`Elsa.Workbench`) with Studio (`Elsa.Studio.Web`), Design and Runtime combined, in its Development configuration. It keeps Weaver sessions and Connections in memory.
- **Durable host:** the normal host plus two overlays. The first is the overlay that the Workbench's durable-store test applies, which adds the Agent EF store (`FoundationAgentEntityFrameworkCore`). The second adds the Connections EF store (`ConnectionsEntityFrameworkCore`). Leaf 2 adds the Connections EF project reference and catalog entry to the Workbench. The Secrets EF store is already composed.
- **Probe host:** the conformance probe's packaged consumer, which composes the Agent EF store. It runs the cases that need the probe's effect-bearing fixture module.

## User Scenarios & Testing

Each story's Independent Test also covers:
- the **common controls** (FR-009);
- the **common effect outcomes**, for each of its effects (FR-014);
- the **canary search** (FR-023).

Every contributed tool and effect has a conformance-probe case on both provider adapters (FR-022). Each step runs in one host only.

### User Story 1 - Explicit answers, the privilege policy and the *performed* outcome (Priority: P1)

A user asks about a module that is not installed or is disabled, or about a page this feature does not support. Weaver answers from two live sources: the Studio module registry, which module load status shows, and the Weaver contributions each host registers. It never infers support from a route or from M0's historical module list.

The user can also see which privilege classes the integrator has enabled. Each review shows the effect's class, and an approved effect that persists nothing shows as *performed*. Where the refresh finds the Weather sample installed, Weaver explains its displayed forecast.

**Why this priority**: Without it, every narrowing would be silent, and no later leaf could use the privilege policy or *performed*.

**Independent Test**:
1. In the normal host, run leaf 0's inventory refresh. Then deselect the structured logs features (the backend feature, its EF store and the Studio module) and disable ExtensionBuilder. Structured logs has no dependants other than its own EF store, so nothing else is deselected with it.
2. Ask Weaver about structured logs and the extension builder. Open `/diagnostics/structured-logs` and ask again.
3. Enable ExtensionBuilder, open it, and ask again.
4. Select the Weather sample in the normal host, and ask about the displayed forecast.
5. In the probe host, use the fixture module, which leaf 0 extends with an administrative effect, an irrecoverable effect and an effect that persists nothing:
   - propose each effect with the privilege classes unconfigured;
   - propose each again with all three classes enabled, as the Workbench configures them;
   - propose each again with a class switched off at each step: between review and approval, between approval and execution, and before recovery;
   - include an effect whose owner code echoes a canary value.
6. In the probe host, deselect the fixture module while a page nests its widget.
7. In the probe host, leave an effect pending, restart, remove the fixture module, and approve.

**Acceptance Scenarios**:

1. **Given** a module that the registry does not list, **When** the user asks about it, **Then** Weaver answers from the registry that it is not installed. On the module's route, it discloses only the route.
2. **Given** a disabled module, or an enabled module without Weaver contributions, **When** the user asks for help on its page, **Then** Weaver says so and offers no tool.
3. **Given** the Weather sample installed, **When** the user asks about the displayed forecast, **Then** Weaver explains it from the sample's client-asserted kind.
4. **Given** a deselected module, **When** a page that would nest its widget is open, **Then** nothing of that module is offered, and other modules' support is unchanged.
5. **Given** unconfigured privilege classes, **When** any classed effect is proposed, **Then** it is refused before review.
6. **Given** all three classes enabled, **When** the same effects are proposed, **Then** each is reviewed, and its review shows its class.
7. **Given** a session, **When** it bootstraps, **Then** the server reports every class's state, and Studio shows the states read-only.
8. **Given** an effect whose class is off at the step being taken, **When** that step is approval, execution or recovery, **Then** the effect is refused at that step. At execution, nothing is dispatched.
9. **Given** an approved effect whose owning module reports that it persisted nothing, **When** the outcome is recorded, **Then** it is *performed*, and the review shell shows it.
10. **Given** an owner code that is not a dotted ASCII identifier outside `agent.`, or that echoes a canary value, **When** the receipt is recorded, **Then** the code is rejected, and the canary appears nowhere.
11. **Given** a pending effect whose module is removed, before or after a restart, **When** it is approved or recovered, **Then** it resolves as unavailable with zero effect ([spec 1189](../1189-weaver-recovery/spec.md)).

---

### User Story 2 - Inspect, describe, test, revoke and delete Secrets without exposing a value (Priority: P1)

A user on the Secrets page (`/security/secrets`, or its alias `/secrets`) asks which secrets exist, why one is expired or whether one is usable. The user may also ask Weaver to improve a description, or to revoke or delete a secret.

Weaver reads each secret's metadata:
- name, display name and description;
- type, store and scope;
- tags and status;
- the effective condition, such as expired, and the latest version's expiry, both computed by the owning module (FR-006);
- current version and revision;
- created and updated times.

It never discloses a value. Weaver can propose:
- a metadata update;
- the owning module's value test;
- a revoke or delete, where the integrator enables the irrecoverable class.

**Why this priority**: It is the smallest row with the highest exclusion risk, and it sets the pattern for the rest.

**Independent Test**: In the normal host, with a test-only secret store, seed:
- an active secret, one whose latest version has expired, and a revoked secret, each holding a canary value;
- a secret whose test message echoes its value;
- a secret whose delete removes the value and then fails to save the metadata;
- a Service connection that references one of the secrets;
- an inline secret that a connection owns;
- a secret in another tenant.

Ask about the list on both routes, and about each secret. Improve one description, test one secret, revoke one and delete one. Approve each, and compare each receipt with the owning module's readback.

Negative cases:
- an actor without `secrets:read`, for reads;
- an actor without `secrets:write`, one without `secrets:test`, and one without `secrets:delete`;
- a target in another tenant;
- an edit or a rotation in another tab between review and approval;
- a test of a secret that does not exist, or is inactive;
- a repository that cannot save at a revision;
- the administrative or irrecoverable class off at each step;
- the partial delete;
- a revoke or delete of the inline secret a connection owns;
- in the durable host, a restart with an approved effect pending;
- requests to create, rename, reveal, rotate, import or export.

**Acceptance Scenarios**:

1. **Given** the list with a filter and a selection, **When** Weaver reads context, **Then** it receives the filter, the selected identities and their metadata, bounded by `secrets:read` and the tenant. An expired secret shows as expired.
2. **Given** an approved display-name or description update, **When** it is applied, **Then** the owning module saves it at the reviewed revision, and the receipt carries the readback. A repository that cannot save at a revision makes the update, revoke and delete unavailable.
3. **Given** an approved test, **When** it runs at the reviewed version, **Then** it is *performed*, and the receipt carries only success or failure and the owner code. The value and the message are redacted.
4. **Given** a test that could not run, because the secret does not exist or is inactive, **When** it is recorded, **Then** it is refused with the owner code. The owner codes for "not found" in the manager and in the configuration store are distinct.
5. **Given** a revoke, **When** it is reviewed, **Then** the review states that the revoked version's value cannot be restored and that rotating reactivates the secret with a new value. It also lists the resources that reference the secret, as an advisory list (FR-006).
6. **Given** a delete, **When** it is reviewed, **Then** the review states that the delete cannot be undone, and lists the same advisory references.
7. **Given** a revoke or delete, **When** it is prepared, **Then** it needs `secrets:delete`, and is refused unless the irrecoverable class is enabled. The reviewed revision is checked before any side effect. A delete's receipt is an absence readback: the secret is hidden.
8. **Given** the inline secret a connection owns, **When** a revoke or delete is proposed from the Secrets page, **Then** Weaver refuses it, which is stricter than the native page: the Secrets module has no notion of connection ownership and allows the operation. The refusal relies on the owning connection that `ISecretReferenceExtension` reports, because the operation would break that connection irrecoverably. The user can still act on the native page or remove the binding from the connection first.
9. **Given** the partial delete, **When** the outcome is recorded, **Then** it is *uncertain*, and it is never replayed.
10. **Given** a request to create, rename, reveal, rotate, import or export, **When** Weaver receives it, **Then** Weaver refuses on the grounds in FR-001.

---

### User Story 3 - Understand, prepare, update, rebind, test, revoke and delete a Service connection (Priority: P1)

A user on the Connections page, in its creation form, or at a connection picker in a workflow asks what a connection is or why it fails. The user may also ask Weaver to prepare, update, rebind, test, revoke or delete a connection.

Through a new owning-module read (FR-006), Weaver reads the connection's:
- name, display name, type, authentication scheme and status;
- settings field names and secret-bound field names;
- managing party and Owner field;
- tags, last test time and version.

Bound secret names and named-credential names are disclosed only under `secrets:read`; otherwise only the field names are. Settings values never are.

Weaver can propose:
- a prefill of the visible creation form;
- an update of the display name and tags;
- a rebind of a secret field;
- the owning module's test, which may contact an external service, depending on the type's provider;
- a revoke or delete, where the integrator enables the irrecoverable class.

**Why this priority**: "Why does my integration fail" is the most common Connections question.

**Independent Test**: In the normal host, use a test-only connection type. Its test records each invocation, it has canary token fields, and its test message echoes a resolved value. Seed:
- a connection with a canary settings value, a canary secret value and canary token values;
- a connection whose field uses an inline secret that the connection owns;
- an active text secret, an expired one and an inactive one in the tenant;
- another connection's owned secret;
- a connection in another tenant.

Explain each connection. Then prefill the form, update a connection, rebind a field, test it, revoke it and delete it. Approve each, and compare the receipts with the recorded invocation and the connection readback.

Negative cases:
- an edit or a user-run test between proposal and approval, either of which advances the version;
- for reads, an actor with neither `workflows/design:read` nor `connections:manage`;
- an actor without `secrets:read`, for bound names and for a rebind. The rebind is refused before review exactly as for an unknown secret;
- an actor without `connections:manage`, for updates, rebinds, revokes and deletes;
- an actor without `connections:bind-secret`, and one without `connections:test`;
- Connections composed without its API, where the default permission gate allows everything. The new contract's own decision still refuses;
- a target in another tenant;
- a rebind to an expired, inactive or non-text secret, or to another connection's owned secret;
- a rebind away from the inline secret whose cleanup then fails after commit;
- a test that cannot run, a test that runs and fails, and a test whose write fails after the external call;
- a test message, or a test owner code, that echoes a resolved value;
- a dropped test response;
- a delete that removes owned secrets and then fails to remove the connection;
- the creation form closed before the prefill completes;
- the administrative or irrecoverable class off at each step;
- in the durable host, a restart with an approved effect pending;
- requests to create a connection, authorize OAuth, change the redirect URI, or set a settings value.

**Acceptance Scenarios**:

1. **Given** a connection, **When** Weaver reads it, **Then** it receives the metadata and version, and receives bound and named-credential names only under `secrets:read`.
2. **Given** the creation form, **When** Weaver reads it, **Then** it receives the type, scheme, field names and validation errors, and no entered settings or secret value.
3. **Given** an approved prefill, **When** it is applied, **Then** only the form's name, type and scheme change, applied locally. Creating the connection stays the user's action.
4. **Given** an approved update or rebind, **When** it is applied, **Then** it applies only at the reviewed version, under `connections:manage`. A rebind also needs `connections:bind-secret`, `secrets:read`, and an active text secret in the tenant that no other connection owns. The candidates shown to the client grant nothing.
5. **Given** a rebind away from an inline secret the connection owns, **When** it is reviewed, **Then** it belongs to the irrecoverable class, because the connection owns that secret. The review states that the secret is deleted after commit if nothing else references it, and cannot be restored. If the cleanup fails after commit, the effect is persisted with a warning.
6. **Given** an approved test, **When** it runs, **Then** the reviewed version is checked before the external call. When the type's test runs, the owning module records the test time and advances the version, and other pending proposals for that connection become stale. The receipt is persisted with whether the test ran, whether it succeeded, and the owner code. The message is redacted.
7. **Given** a test that cannot run, **When** the outcome is recorded, **Then** it is refused with the owner code. **Given** a test whose write fails after the external call, **Then** it is *uncertain*.
8. **Given** a revoke or delete, **When** it is reviewed, **Then** the review states that it cannot be undone and that workflows using the connection will fail. For a delete, it also lists the owned secrets that will actually be deleted. Secrets that something else still references are kept, and are not listed.
9. **Given** a revoke or delete, **When** it is prepared and run, **Then** it is refused before review unless the irrecoverable class is enabled. It applies only at the reviewed version, which is checked before any side effect. A delete's receipt is an absence readback: the row is gone.
10. **Given** a delete that removes owned secrets and then fails to remove the connection, **When** the outcome is recorded, **Then** it is *uncertain*.

### Edge Cases

- **A module unloads during a turn.** Its context is re-admitted without it.
- **The Studio host and the backend differ.** Each context names the host it describes.
- **A nested picker is open while an Operation Batch is pending.** The picker's own context never changes the batch's target.

## Requirements

### Functional Requirements

#### Delivery matrix

- **FR-001**: Leaf 0 MUST refresh the normal host's live installed and composed module inventory, because M0's loaded-module list is historical (#3443, 30 October). It MUST record the result on #3443.
  - Each M0 row records M0's inventory, which the refresh confirms.
  - A row the refresh finds absent or changed is updated by leaf 0's PR, either with ground A or with its new outcome.
  - A screen the refresh finds that the matrix lacks is added by leaf 0's PR as a new row, and assigned under FR-025.
  - Every row MUST end at its outcome and evidence.
  - A narrowing of a row's target, and each existing owner operation that Weaver does not offer, MUST state its grounds.

  **Outcome words:**
  - **Read:** read and explain.
  - **Reviewed:** a reviewed effect.
  - **Local:** a reviewed edit of visible, unsaved state.
  - **Delivered by #3442:** M3 delivers it.
  - **None in M4:** not supported in M4.

  **Grounds:**
  - **A:** absent from, or disabled in, the refreshed inventory.
  - **B:** the Program boundary: secret values, package promotion and package activation, and module and feed administration. Feature selection apply is outside B where the integrator enables the activation class (owner decision 2).
  - **C:** a credential, file or value would pass through Weaver.
  - **D:** the owning module does not hold the setting durably or cluster-wide.
  - **H:** a human's decision.
  - **N:** no owner operation exists.
  - **O:** another issue or open decision owns it.
  - **S:** the screen does not show it.
  - **T:** outside the row's target. For an M0 row, that is its M0 target. For a post-M0 row, it is the target stated here.
  - **V:** the owning module's write is reachable only from the browser, so the server cannot verify its result.
  - **W:** needs a module-owned working-state adapter.
  - **X:** needs its own exclusion design before any disclosure.

  This spec specifies the Weather, ExtensionBuilder, Secrets and Service connections rows. Each other row states its outcome here; its leaf spec states the effects, contracts and scenarios.

  | Row | Owning module | Target | Outcome; narrowings (grounds) |
  | --- | --- | --- | --- |
  | Weather | Samples | Qualify when installed (M0) | Read when installed (story 1); None in M4 when absent (A) |
  | ExtensionBuilder | ExtensionBuilder | Optional inspection and guidance (M0) | None in M4: ground A while it is disabled, and ground X once it is enabled, because its state includes workspace source |
  | Secrets | Secrets | Reference, metadata and availability inspection; domain effects (M0) | Read; Reviewed metadata update, value test, revoke and delete (story 2). Value-bearing create and rotate (B); configuration-key create and rotate (T); rename, import and export (N); the workflow property's Secrets editor (O: #3711) |
  | Service connections | Connections | Metadata, bindings and diagnostics; changes and tests (M0) | Read; Local prefill; Reviewed update, rebind, test, revoke and delete (story 3). Settings values (X: no field declares its sensitivity, so values cannot be disclosed safely); create and OAuth authorize (C); redirect URI (N: a read-only public URL) |
  | Structured logs, traces | StructuredLogs, OpenTelemetry | Filters, records and correlation; trace, span and query (M0) | Read (its leaf spec). Time range, retention, metrics and OpenTelemetry logs (S) |
  | Diagnostics overview, console, module load status | Studio Hosting; `Elsa.Studio.ConsoleStream` (client) with console log streaming (server) | Visible results; console source and records; module state (M0) | Read (its leaf spec). Server reads of console records (X); lines the user attaches are the user's own client-asserted disclosure (FR-009); module administration (B) |
  | Home | Studio Dashboard, Studio Preferences, Attention, Workflows.Dashboard | Snapshots, filters and freshness; supported preferences (M0) | Read; Reviewed personal layout change and Attention Item snooze (its leaf spec). Default layout (T); new widgets and saved queries (O: #3604) |
  | Workflow Runtime Diagnostics Setting | Workflows Runtime | Explain the capture policy and exact proposed differences (M0) | Read; Reviewed change within today's capture rules (its leaf spec). Full payloads (O: Studio ADR 0009's open decision); a durable, cluster-wide setting (D) |
  | Activity availability | Activities Design | Effective availability and diagnostics; supported settings changes (M0) | Read; Reviewed change (its leaf spec) |
  | Features | FeatureManagement (client), Modularity (server) | Host, revision, filters and draft; reviewed differences (M0) | Read; Local draft; Reviewed Server-host apply in the activation class (its leaf spec). Studio-host apply (V); configuration values and edits (C) |
  | Appearance, theme builder | Studio Hosting, Hosting.Themes | Preference and theme edits (M0) | Read; Local appearance and builder edits (its leaf spec). Theme Store writes (V) |
  | Modules, feeds | Hosting.Nuplane (client: Studio Hosting) | Host, package, feed and reconcile state (M0) | Read (its leaf spec). Package and feed operations (B); feed credentials are never disclosed (#3735) |
  | Backends | Studio Backends | Read the profiles (post-M0) | Read (its leaf spec). Profile edits (V) |
  | Settings sections | Studio Hosting | The resolved section and host (#3443, 31 October refinement, item 4) | Read (its leaf spec) |
  | ETL definitions, connectors, runs | ETL | Drafts, filters, validation and authoring; run and partition; explicit cancellation (M0) | Read; Reviewed draft update and run cancellation (its leaf spec). Create (W); delete and start (N); publish (H); complete (T) |
  | Activity definitions | Activities Design; Workflows.Publishing (test runs, publish); Elsa3 (import) | Inspection; draft, import and upgrade proposals (M0) | Read; Reviewed draft add, update and discard, and upgrade plans (its leaf spec). Import (C); publish (H); other definition and version operations, and test runs (T); contract proposals (W) |
  | User tasks, forms | `Elsa.Studio.UserTasks` (client), Workflows Runtime (server) | Read; claim, release and reassign (post-M0) | Read; Reviewed claim, release and reassign (its leaf spec). Decisions (H); prefill values and decision responses (C); form authoring (W) |
  | Agents, reviews | Agent Specialists | Read (post-M0) | Read (its leaf spec). Review decisions (H); the decider (C); authoring (O: #3519) |
  | BPMN | Activities BPMN | Read (post-M0) | Read (its leaf spec). Run and work operations, artifact registration, interchange management (T); direct publish (H); process authoring (W) |
  | Expressions | Expression Modules | Language-owned guidance and exact expression edits (M0) | Read; Reviewed expression edits (its leaf spec, coordinated with #3711) |
  | New workflow | Workflows, Weaver.Workflows | Caller attachment; typed suggestions or reviewed creation (M0) | Delivered by #3442 |
  | Workflow rows: definitions, draft, templates, library and tags, versions, Executables, Runs | Workflows | M3 rows | Delivered by #3442 |

#### Packaging and ownership

- **FR-002**: **Server packages.** This spec decides, under Program authority, that there is one Agent package per owning module. #3428 allows optional integrations to be "separately packaged with their owning module", but the guards below rule out putting them inside Essentials owners.
  - **Name:** each package is named `Elsa.Agent.<Domain>`, at the path the architecture guard derives.
  - **Why the Agent bucket:** the ExtensionBoundary guard forbids every core project from referencing an extension. Framework §2.2's secondary-domain sub-rule covers features with no models of their own, and these packages define their own Context Kind, tool and result models.
  - **References:** a package references its owning module's `.Core` where one exists, plus the Agent packages in its own bucket. It registers the server-side descriptor of every client tool its module offers.
  - **No `.Core`:** a package for a module without one admits only client-asserted kinds. Such a package exists only where SC-004's per-package deselection needs it.
  - **Cross-bucket edges:** where the owning module sits in another extension bucket, such as ETL or ExtensionBuilder, the reference is declared in the ExtensionBoundary guard's `DeclaredExtensionEdges`, with a justification comment.

  Leaves 1 and 2 add `Elsa.Agent.Secrets`, which reads only Secrets `.Core`, and `Elsa.Agent.Connections`. The latter serves the Connections module. It is distinct from the existing `Elsa.Agent.Specialists.Connections`, which serves specialist model connections. #3442 slice F (#3732, merged) added `Elsa.Agent.Workflows.Runtime`, which later leaves extend.
- **FR-003**: **Studio packages.** A Studio contribution MUST live in its owning Studio module, through the public SDK. A satellite package, in the shipped form `Elsa.Studio.Weaver.<Domain>` under `Extensions/Weaver/studio/`, is added only where a host must be able to keep the module but drop its Weaver support. The Built-in Studio Screens are such a case, because every host keeps the shell. They are owned by `Elsa.Studio.Hosting`, and their contributions live in `Elsa.Studio.Weaver.Hosting`, beside `Elsa.Studio.Weaver.Workflows`.
- **FR-004**: **Result Renderers and module guidance.** Each module MUST ship, in its own packages, the Result Renderers for its tool result kinds and its own bounded, versioned module guidance. Module guidance is trusted installed-module text, kept distinct from resource content. Shared Weaver code holds neither.

#### Generic changes (leaf 0)

- **FR-005**: Weaver core, the shell's collector and review shell, and the provider adapters MUST gain no row's domain knowledge. Leaf 0 delivers every generic change:
  - the privilege policy (FR-015), including the server's report of every class's state at bootstrap;
  - the review shell showing each effect's class and the *performed* outcome;
  - *performed* (FR-016), and validation of owner codes;
  - the registry-based answer of story 1, and the Weather qualification;
  - SDK read seams for `Elsa.Studio.Weaver.Hosting`;
  - the conformance probe fixture's administrative, irrecoverable and *performed* effects;
  - new outcome codes, only where no existing code fits.

  This spec's contracts record these decisions. Leaf 0 adds a dated one-line pointer, "Amended by spec 1203", to spec 1186's session-policy contract and to spec 1189's recovery contract.

#### Owner prerequisites

- **FR-006**: Some rows need a read, an authorization or an operation that today exists only behind the owning module's HTTP endpoints, or outside its `.Core`. For each, the owning module MUST supply a `.Core` contract, implemented by its feature package, that Weaver can call under the same principal.
  - **Principal:** the contract takes the caller's authenticated principal. The Agent host supplies it from the authenticated request at preparation, approval and execution, and at the explicit authenticated execute that runs a recovered approval.
  - **Authorization:** the contract checks that the actor matches the principal, and decides permissions through `IPermissionAuthorizationService`. It never relies on a module's permission gate, which may default to allowing everything.
  - **Exceptions:** infrastructure exceptions are wrapped (framework §2.23.5).
  - **Writes:** a write to an existing resource takes an expected revision, checked before any side effect. A composition that cannot honor the revision reports the write as unsupported, so it never runs unconditionally. A create, where no resource exists, uses the owning module's create-if-absent instead.
  - **Native pages:** the shipped native pages keep working, each with a regression test.

  Plan decision P2 owns the type shape. Leaves 1 and 2 need:

  | Owning module | Prerequisite |
  | --- | --- |
  | Secrets | A metadata read with the revision, the effective condition and the latest version's expiry. A revisioned metadata update. A value test at an expected version that reports whether it ran, with distinct owner codes. Revoke and delete at the revision, each unavailable without a revision-aware repository. An extension contract, `ISecretReferenceExtension`, that reports which owner resources reference a secret. Connections implements it. The list is advisory, because reference checks are not atomic, and it is empty when Connections is deselected. |
  | Connections | A read that adds the version and the bindings, omits settings values, and shows bound and named-credential names only under `secrets:read`. An update (display name and tags) and a rebind, each at an expected version; the rebind checks `secrets:read` and refuses as for an unknown secret. A test at an expected version, checked before the external call, whose result adds a "ran" flag and an owner code. Revoke at an expected version. A delete that checks the version before deleting any owned secret, then removes the row with the repository's version check. |

  Each later leaf spec lists its own prerequisites under this rule.
- **FR-007**: **Read authority.** A rule that is exactly one permission MAY be checked through the tool's declared permission, which `IPermissionAuthorizationService` decides for the caller's principal. Any other rule MUST be decided by the owning module's contract (FR-006), never reimplemented in a Weaver package.
- **FR-008**: Deployment shapes MUST keep working (constitution §E2.2.3). Design-side contributions register only where Design is composed, and Runtime-side contributions only where Runtime is. An absent side is explicitly unavailable.

#### Context and disclosure

- **FR-009**: **Common controls.** Every Context Kind and tool MUST have a negative case for each control below, tested at the cheapest level that proves it (ADR 0080 D1, framework §2.23):
  - **Module removed:** nothing of the module is offered.
  - **Denied:** nothing is disclosed, not even existence. A denied effect is refused before review.
  - **Cross-tenant target:** treated as denied.
  - **Owner-unavailable:** nothing from that owning module is disclosed.
  - **Stale target:** zero effect, and navigation never retargets.
  - **Excluded data:** seeded canaries appear nowhere, including in owner codes.
  - **Untrusted content:** resource content and every client-asserted kind are marked untrusted, distinct from module guidance. Resource content includes log and console text, attributes, titles, review content and agent instructions.

  Content the user explicitly attaches, such as selected console lines, is the user's own disclosure. It is client-asserted and untrusted, and the excluded-data control does not apply to it.
- **FR-010**: Each Context Kind MUST be module-owned, versioned, bounded and authorized. It MUST be attributed to an exact identity:
  - the owning module's revision, where one exists;
  - for a list, the visible query and selection;
  - for unsaved state, the client binding and base revision.

  Server context providers admit only their own kinds, and produce their permission and catalog kinds from their own reads. A tool never takes data from another package's kind. A nested picker contributes its own kind from its own module. For example, the Connections picker in a workflow property form is a Connections contribution. A form's own fields belong to the form's module, including the creation form's secret field, which belongs to Connections.
- **FR-011**: Each Context Kind MUST declare its bounds. Each leaf adds tests that prove two things:
  - its densest pages, nested pickers included, stay within the host's attachment count at the largest set each page shows by default;
  - each kind stays within the per-attachment bounds.

  A session that reaches its disclosure budget is refused as that budget defines.
- **FR-012**: Each disclosure allowlist MUST be authored by the module that defines the data's meaning, and ship in that module's Agent package. Unlisted fields are excluded by default. Each allowlist is proven by a canary in an unlisted field that the owning module's own redaction does not already handle.

#### Effects

- **FR-013**: Every effect MUST declare its risk, privilege class, stale check and receipt. Each leaf spec states them explicitly. For leaves 1 and 2:

  | Effect | Risk | Privilege class | Stale check | Receipt |
  | --- | --- | --- | --- | --- |
  | Secret metadata update | `Admin` | administrative | revision | readback; persisted |
  | Secret value test | `Admin` | administrative | version | *performed*, with success or failure and the owner code; refused when it could not run |
  | Secret revoke | `Admin` | irrecoverable | revision | readback; persisted |
  | Secret delete | `Admin` | irrecoverable | revision | absence readback; persisted; *uncertain* when partial |
  | Connection prefill | `ReviewRequired` | none | client binding | applied locally |
  | Connection update; rebind of a field whose secret the connection does not own | `Admin` | administrative | version | readback; persisted |
  | Rebind of a field whose inline secret the connection owns | `Admin` | irrecoverable | version | readback; persisted, with a warning if cleanup failed |
  | Connection test | `Admin` | administrative | version, checked before the external call | persisted, with whether the test ran, its result and the owner code; refused when it could not run; *uncertain* when the write failed after the external call |
  | Connection revoke | `Admin` | irrecoverable | version | readback; persisted |
  | Connection delete | `Admin` | irrecoverable | version | absence readback; persisted; *uncertain* when partial |

- **FR-014**: **Common effect outcomes.** Every effect MUST be tested for each of these, at the cheapest level that proves it:
  - **Review:** the effect is reviewed in every autonomy mode, including `full-auto`.
  - **Refusal at a step:** the policy is read live at every step. The effect is refused at preparation, approval, execution or recovery whenever its class is off at that step, so a refusal is not sticky. At execution, nothing is dispatched. Without the owning module's permission, the effect is refused before review.
  - **Owner unavailable at approval:** owner-unavailable, with zero effect.
  - **Module removed at approval:** unavailable, with zero effect.
  - **Restart:** in the durable host, a pending approval survives a restart. A recovered approval runs only on an explicit, authenticated execute, which rechecks the revision, the permission and the class.
  - **Lost or uncertain response:** an effect whose response is lost after dispatch, or that may have run before a restart, is *uncertain*. It is never replayed, and the session owner resolves it as spec 1189's recovery contract defines.
  - **Local edits:** these carry correlated acknowledgements, and are unavailable, with zero effect, if their page is gone.
- **FR-015**: **Privilege policy** (owner decisions 1, 2 and 4). The Agent module MUST gain an integrator-configured privilege policy. It is a backend host option, never a session setting. It has three classes, each enabled independently; a host may enable the irrecoverable class alone, and that is intended.
  - **administrative:** `Admin` effects. An `Admin` effect that declares no other class belongs here by default.
  - **irrecoverable:** effects whose consequence cannot be undone. A module MUST declare this class for its own irrecoverable effects, whatever their risk label. Leaves 1 and 2 do so. Limitation: a third-party effect that is irrecoverable but declares no class, and is not `Admin`, is not caught.
  - **activation:** feature selection apply on the Server host.

  The policy also MUST meet these rules:
  - **Declaration:** the owning module declares the class at preparation, because core cannot infer consequence (FR-005). The declared class is frozen into the approval digest.
  - **Defaults:** every class is off by default, so a host that leaves the policy unconfigured refuses every classed effect. The Workbench, as the reference integrator, enables all three in its base configuration, and so in every environment.
  - **Enforcement:** the policy is read live at each step (FR-014).
  - **Display:** Studio shows the policy read-only, as part of its Host Policy, from the state the server reports at bootstrap.
- **FR-016**: **Outcomes.** Leaf 0 MUST add *performed* (owner decision 3): the approved action ran, and the owning module persisted no change. *Performed* is a new proposal status. It reports the action as executed, with no persisted effect scope, and its receipt carries only what the owning module reports. Owner codes in receipts are validated (Terminology).

  Every other outcome reuses an existing one. *Uncertain* keeps its existing shape and generic message, and the session owner resolves it (spec 1189). The existing outcome codes are:
  - `agent.proposal.executor_unavailable`;
  - `agent.tool.not_found`;
  - `agent.tool.context_changed`, at admission;
  - `agent.proposal.refused`, at apply time;
  - `agent.tool.client_unavailable` and `agent.proposal.client_unavailable`;
  - `agent.context.budget_exceeded`;
  - `agent.capability.unavailable`;
  - the denied and owner-unavailable outcomes.

  A new code is added only where none of these fits, such as the privilege-policy refusal. A status alone never stands as evidence of an effect.

#### Contracts, catalogs and records

- **FR-017**: Replacement contracts MUST detect a second registration (framework §2.6.2). The Agent-wide gap for existing contracts remains #3633. Visibility follows framework §2.23.3 and ADR 0080 D3.
- **FR-018**: **SemVer.** Every change MUST be classified under framework §4.2. Pre-GA Majors follow ADR 0080 D4 and Elsa constitution §E5, as spec 1193 FR-033 did.
  - **Major:**
    - *Performed*, as a new value appended to the proposal-status enum and its status string. This changes model semantics. Leaf 0 updates every Studio site that maps statuses, in the same change: the positional mapping in the Weaver session hook, the string allowlists in the agent client, the numeric fallback (which already disagrees with the C# enum), and the client's status union.
    - The privilege policy. With every class off by default, it refuses `Admin` tools, third-party ones included, that ran before.
  - **Minor:**
    - new members on existing stores and contracts, added so that existing implementations keep compiling (the plan names the mechanism);
    - new contracts, model members, packages, Context Kinds, tools, renderers, codes and permissions.

  Each leaf spec classifies its own changes by these rules.
- **FR-019**: **Catalogs** MUST change in the same unit of work as the code (framework §2.22.1).
  - **Leaf 0:** the Agent catalog (`Extensions/Agent/src/Elsa.Agent/EXTENSION_POINTS.md`) gains the privilege policy option, the class declaration, the *performed* status and the SDK read seams. The Studio SDK and Hosting documentation are updated to match.
  - **Leaf 1:**
    - `Essentials/src/Secrets/Core/EXTENSION_POINTS.md`, including `ISecretReferenceExtension`;
    - `Essentials/src/Secrets/Elsa.Secrets/EXTENSION_POINTS.md`;
    - `Essentials/src/Secrets/Persistence/EntityFrameworkCore/EXTENSION_POINTS.md`.
  - **Leaf 2:**
    - `Essentials/src/Connections/Core/EXTENSION_POINTS.md`;
    - `Essentials/src/Connections/Elsa.Connections/EXTENSION_POINTS.md`, including its `ISecretReferenceExtension` implementation as a cross-domain contribution.
  - **Every leaf:**
    - the new contributors, entered in the Agent catalog as cross-domain Known implementations;
    - each new package's own catalog, where the package exposes extension points;
    - the root index (framework §2.22.2);
    - each package README, listing its cross-domain contributions.
- **FR-020**: **Records.** Evidence goes on each PR, on #3443 and on #3428, which owns the matrix and evidence ([program goal](../../docs/program-goals/weaver-across-studio.md)). This spec's PR records the owner's boundary amendment in the program goal: activation is in scope only for hosts that enable the activation class.

  Leaf 0 MUST file the following follow-ups as `needs-triage` (AGENTS.md; [repository hygiene](../../docs/contributing/repository-hygiene.md#issues)):
  - the ExtensionBuilder exclusion design (X);
  - a server-verifiable path for browser-only writes (V);
  - a durable, cluster-wide Workflow Runtime Diagnostics Setting (D);
  - console record reads (X);
  - a log time range and retention view (S);
  - working-state adapters for ETL creation, task forms, BPMN processes and activity contract proposals (W);
  - irrecoverable-class coverage for third-party effects that declare no class (FR-015).

  Follow-ups that are this Program's work are then triaged under the 29 October delegation, and each is recorded on #3428. Nothing that already exists is filed: context-budget progress goes on #3710, and Full payloads are recorded as Studio ADR 0009's open decision. Leaf 0 also records on #3428:
  - every narrowing;
  - the deferrals to other programs: #3604, and #3519's authoring;
  - the decisions open to owner review.
- **FR-021**: **Glossary.** Leaf 0 MUST change Studio/CONTEXT.md:
  - **Replaced:** *Module Load Status* lists the states the host reports: available, loaded, disabled, incompatible and failed, plus the registry's pending and unknown. This replaces "discovered, accepted, activated, disabled, or rejected". Its _Avoid_ line, "Diagnostics signal, observability widget", stays.
  - **Widened:** *Privileged Tool* means a Weaver Tool whose effect belongs to a privilege class.
  - **Added:**
    - *Privilege Class*, beside *Privileged Tool* and *Host Policy*: a class of effects that the integrator enables in the backend Agent module's configuration, which Studio displays as part of its Host Policy (FR-015);
    - *Performed* (FR-016);
    - *Built-in Studio Screen*: a screen the `Elsa.Studio.Hosting` client renders itself, such as module load status, appearance or the settings section resolver;
    - *Client-Asserted Kind*: a Context Kind whose content only the browser supplies. It is untrusted, confers no authority, and the server checks only its schema and bounds.

#### Evidence

- **FR-022**: Each story MUST have end-to-end evidence. Fixture cases run in the probe host, and the restart cases of stories 2 and 3 run in the durable host. Every contributed tool and effect MUST have a conformance-probe case on both provider adapters: Copilot's delegated loop and Anthropic's host-driven loop. Negative cases are tested at the cheapest level that proves them.
- **FR-023**: The canary search MUST cover HTTP bodies, SSE frames, adapter payloads, tool results, owner codes, review cards and audit records.
- **FR-024**: Evidence MUST keep three categories distinct:
  - deterministic component tests;
  - deterministic provider-protocol native evidence on the actual Copilot and Anthropic adapters, with packaged consumers;
  - opt-in live-provider evidence (none claimed).

  Probe evidence is frozen at the exact reviewed head of its PR. Failures, skips and quarantines are recorded as they occurred.

#### Later leaves

- **FR-025**: Each later leaf MUST get its own spec, numbered at the time with the next free number. It is written and reviewed when the previous leaf merges, because #3428 elaborates later leaves only when their slice is concrete. A leaf spec is bound by this spec's cross-cutting requirements (FR-002 to FR-024) and by its FR-001 rows. It specifies those rows' effects, owner prerequisites, risks, classes and scenarios.

  **Unassigned rows:** a row that leaf 0's refresh adds is assigned to an existing leaf, or to a new one, by leaf 0's PR. #3443 cannot close while a row is unassigned.

  | Leaf | Rows | Owning modules | Scope |
  | --- | --- | --- | --- |
  | 3 | Structured logs, traces | StructuredLogs, OpenTelemetry | allowlisted log and trace reads; correlation |
  | 4 | Diagnostics overview, console, module load status | Studio Hosting; `Elsa.Studio.ConsoleStream` with console log streaming | screen reads; console lines the user attaches |
  | 5 | Home | Studio Dashboard, Studio Preferences, Attention, Workflows.Dashboard | widget snapshots, personal layout, snooze |
  | 6 | Workflow Runtime Diagnostics Setting, activity availability | Workflows Runtime, Activities Design | host-scoped settings changes |
  | 7 | Features, appearance, theme builder | FeatureManagement, Modularity, Studio Hosting, Hosting.Themes | feature draft and Server-host apply; local appearance and builder edits; the *Theme Selection Preference* glossary widening |
  | 8 | Modules, feeds, backends, settings | Hosting.Nuplane, Studio Backends, Studio Hosting | administration reads |
  | 9 | ETL read | ETL | exact design and runtime reads |
  | 10 | ETL effects | ETL | draft update, run cancellation |
  | 11 | Activity definitions | Activities Design, Workflows.Publishing, Elsa3 | reads, drafts, upgrade plans |
  | 12 | User tasks, agents, BPMN | `Elsa.Studio.UserTasks` with Workflows Runtime, Agent Specialists, Activities BPMN | reads; task claim, release and reassign. If reassign stays `Admin`, the leaf spec escalates it |
  | 13 | Expressions | Expression Modules | language-owned guidance and exact expression edits, coordinated with #3711 |

### Key Entities

- **Coverage row:** an inventory screen with its owning module, target, outcome, narrowings and evidence (FR-001).
- **Disclosure allowlist:** the fields whose values may be disclosed, authored by the owning module (FR-012).
- **Effect:** a reviewed operation with its risk, privilege class, stale check, receipt and common outcomes (FR-013, FR-014).
- **Narrowing:** a recorded gap between a target and the delivered outcome, with its grounds (FR-001).

## Success Criteria

### Measurable Outcomes

- **SC-001**: Every FR-001 row has linked evidence on #3428, from this spec's leaves or from its own leaf spec. Every narrowing has its grounds, and no row is unassigned.
- **SC-002**: Stories 1–3 pass their acceptance scenarios in the hosts FR-022 names. A host that leaves the privilege classes unconfigured refuses every classed effect.
- **SC-003**: Seeded canaries appear in none of the channels FR-023 lists. Content the user explicitly attaches is excluded (FR-009).
- **SC-004**: For each contributing package, a composition test that deselects it removes exactly its contributions, and leaves every other row's contributions unchanged.
- **SC-005**: An architecture or diff check over each leaf's PR finds that Weaver core, the shell's collector and review shell, the provider adapters and other modules changed only through FR-005's generic changes.
- **SC-006**: After leaf 0's generic changes, the independent package witness is re-run and passes. The [program goal](../../docs/program-goals/weaver-across-studio.md) requires later lifecycle changes to have their own qualification.
- **SC-007**: The native-page regression tests of FR-006 and the attachment tests of FR-011 pass.
- **SC-008**: Each leaf PR passes the CI and review gates, as spec 1193 SC-010 did:
  - the [merging](../../docs/contributing/merging.md) PR evidence;
  - the Elsa constitution §E7 test-tier manifest for each new test project;
  - locked-mode restore;
  - the solution filters.

## Assumptions and Boundaries

- #3440 and #3441 are Done. #3442's shared seams are reused, except where FR-005 changes them.
- **Delivery.** Each leaf is its own independently reviewed PR. Leaf PRs say "Refs #3443", and the leaf that closes #3443 says "Fixes #3443" ([merging](../../docs/contributing/merging.md)), as spec 1193 did.
  - **Status:** the PR that merges this spec sets *Approved*. Leaf 0's PR sets *In progress*. The closing leaf's PR sets *Implemented*, because it completes the last FR-001 row ([spec lifecycle](../../docs/reference/spec-lifecycle.md)).
  - **Bookkeeping:** every leaf comments on #3428 and #3443 at each state transition, and reconciles #3443 and its Project item (AGENTS.md program bookkeeping).
- **Program owner decisions, 2026-11-03** (Sipke Schoorstra; [receipt on #3428](https://github.com/elsa-workflows/elsa-foundation/issues/3428#issuecomment-6612627295)). These are owner decisions, not open to review here:
  1. **Irrecoverable deletes and revokes** of secrets and Service connections: "Let the integrator decide what privileges weaver has via configuration." They are delivered in the irrecoverable class (FR-015).
  2. **Feature selection apply (activation):** "Let the integrator decide via configuration." It is delivered on the Server host in the activation class. This widens #3443's activation boundary ("a manage claim alone cannot expand it", 31 October), but only for hosts that enable the class. Package promotion and package activation stay ground B, and the Studio host's apply stays ground V.
  3. **A generic outcome for an approved action that persists nothing:** "Add it in leaf 0." This is *performed* (FR-016).
  4. **The privilege default:** "Off; Workbench always on." Every class is off by default. The Workbench enables all three in its base configuration, so in Production too.
- **Decisions taken under Program authority, open to owner review** (as spec 1193 records its own):
  - **Packaging:** one Agent package per owning module (FR-002).
  - **Privilege classes:**
    - membership is by consequence, declared by the owning module and frozen into the approval digest;
    - the classes are independent;
    - a rebind's class follows whether the connection owns the secret it leaves.
  - **Secret references:** they are reported through `ISecretReferenceExtension`, as an advisory list (FR-006).
  - **Naming:** domain nouns keep their current names; new contributor-style interfaces use `I<X>Extension`.
  - **Expressions:** the Expressions leaf delivers the M0 target, coordinated with #3711.
  - **Post-M0 targets:**
    - user tasks get read, claim, release and reassign, and reassign is flagged for escalation in its leaf spec;
    - agents, BPMN and backends are read-only.
  - **Capture semantics:** runtime capture semantics stay untouched, and Full payloads wait for Studio ADR 0009.
  - **Browser-only writes:** the Theme Store, backend profiles and the Studio host's feature apply are narrowings (V).
  - **Home layout:** personal layout changes are delivered, and the default layout stays administration (T).
  - **Run controls:** ETL cancellation is reviewed, because its M0 target names explicit cancellation. BPMN run operations are outside BPMN's post-M0 target (T), as spec 1193 refuses workflow run controls.
- **Plan decisions:**
  - **P1:** each package's shell feature.
  - **P2:** the FR-006 contract types, the principal's shape, the revision mechanisms, and where native permission constants live.
  - **P3:** every non-atomic compare-then-write window, and how it is bounded.
- **Overlap**, reconciled without reparenting:
  - ETL: #2894, #2906, and the open ETL defects #3709, #3719, #3721, #3724 and #3562.
  - Home dashboards: #3604.
  - The parked Diagnostics Observability Readiness and Runtime Execution Evidence goals stay parked.
  - User Tasks: #3688, which is complete.
  - Studio Expression Developer Experience: #2310 (#3711, #2349). The Expressions leaf adds no children under #2310.
  - Elsa 3.8/3.9 Parity: #2771, which owns Elsa 3 import.
  - The [BPMN Engine program](../../docs/program-goals/bpmn-engine.md).
  - Agents: #3519. Runtime agents: #2892.
  - Weaver: #3710 and #3720.
  - Feed credentials: #3735.
- **Out of scope:**
  - releases, deployments and provider purchases;
  - live-provider credentials;
  - blanket administration;
  - arbitrary workspace edits;
  - package promotion and package activation. Feature selection apply is in scope only through the activation class (owner decision 2).

  Integrated qualification remains #3444.

## Issue Traceability

| #3433 criterion | Requirements and stories |
| --- | --- |
| Home/dashboard, ETL, Connections, Secrets metadata, Diagnostics, runtime evidence, activity availability and installed settings/administration surfaces have module-owned support | FR-001 to FR-004, FR-025; stories 2–3 |
| Each supported action has context, tool, renderer/effect and negative-case evidence appropriate to its domain; absent mutation support is explicit | FR-001, FR-004, FR-013, FR-014, FR-022 |
| Secret values and unrelated data are not exposed; runtime/model semantics and existing domain ownership remain intact | FR-006, FR-007, FR-009, FR-012; Assumptions, Capture semantics; FR-002 |
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
