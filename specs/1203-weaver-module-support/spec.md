# Feature Specification: Module-owned Weaver support for the current Studio inventory

**Feature Branch**: `agent/claude/3443-weaver-module-support`

**Created**: 2026-11-03

**Status**: Draft

**Input**: [Feature #3443](https://github.com/elsa-workflows/elsa-foundation/issues/3443) is M4 ([#3433](https://github.com/elsa-workflows/elsa-foundation/issues/3433)) of [Program #3428](https://github.com/elsa-workflows/elsa-foundation/issues/3428), and is [in progress](https://github.com/elsa-workflows/elsa-foundation/issues/3443#issuecomment-6612633571).

The Program owner's standing authorization covers autonomous delivery of #3443's six acceptance criteria, within the issue's boundaries as the owner decisions under Assumptions amend them. The current inventory is the finite matrix. The authorization grants no blanket administration permission, arbitrary workspace editing, package promotion or activation. Consequential decisions outside that scope go to the Program owner (#3428, 29 October continuation).

The scope is:
- the M0 inventory and its targets in [docs/reports/weaver-baseline.md](../../docs/reports/weaver-baseline.md), including Activity definitions;
- the issue's 30 and 31 October planning comments;
- the rows the [claim](https://github.com/elsa-workflows/elsa-foundation/issues/3443#issuecomment-6605270638) adds: `/user-tasks` and `/agents`;
- the screens merged since then: `/settings/backends` (PR #3600) and the BPMN pages (PR #3603).

Prerequisite #3440 is Done. All leaf code waits for #3442's last slice ([spec 1193](../1193-weaver-workflow-journeys/spec.md)), because the Program runs one implementation leaf at a time.

**What this spec covers:**
1. **The M4 delivery matrix (FR-001):** every row, with its target, outcome and grounds.
2. **The cross-cutting requirements** that bind every leaf.
3. **Three leaves, fully specified:**
   - User Story 1 is leaf 0: the generic changes.
   - User Story 2 is leaf 1: Secrets.
   - User Story 3 is leaf 2: Service connections.

   Leaves 1 and 2 each depend on leaf 0. Leaf 2 also builds on leaf 1.

Each later leaf gets its own spec (FR-025). The plan writes this spec's contracts.

**Review:** Thirteen review rounds took place on 2026-11-03, each with two independent lenses: correctness and standards. Every finding was verified against `origin/main`. The Program owner's decisions of the same day are recorded under Assumptions.

**Terminology**
- **Glossary terms:** the Studio terms used here keep their meanings in [Studio/CONTEXT.md](../../Studio/CONTEXT.md), and *Service connection* keeps its meaning in the [Elsa glossary](../../docs/glossary/elsa.md). FR-021 lists the glossary changes.
- **Outcome words:**
  - *Proposed*, *approved*, *persisted*, *refused* and *uncertain* are defined by [spec 1186](../1186-weaver-effects/spec.md).
  - *Applied locally* is defined by spec 1186 FR-003 and #3428.
  - Leaf 0 adds *performed*: the approved action ran, and the owning module persisted no change to its resource.
- **Owner:** this word is always qualified:
  - the *owning module* owns a row's resource;
  - the *Program owner* is Sipke Schoorstra;
  - the *session owner* resolves an uncertain effect ([spec 1189](../1189-weaver-recovery/spec.md));
  - a Service connection's *Owner field* is part of its metadata.
- **Owner code:** a code that a tool reports from its declared set (FR-016).
- **Secret consumer:** a resource that uses a secret, such as a Service connection bound to it. The wording keeps this distinct from Studio's *Secret Reference*.
- **Host attachment budget:** the per-collection attachment count and per-attachment bounds that Weaver's context admission enforces, together with the session disclosure budget ([#3710](https://github.com/elsa-workflows/elsa-foundation/issues/3710)).

**Hosts**
- **Normal host:** the Workbench backend (`Elsa.Workbench`) and Studio (`Elsa.Studio.Web`), with Design and Runtime combined, in the Development configuration.
- **Durable host, per leaf:** the normal host with the overlay that the Workbench's durable-store test applies. That overlay adds the Agent EF store; the Secrets EF store is already composed. Leaf 2 adds the Connections EF store, along with the Workbench project reference and catalog entry for it.
- **Test overlay:** the overlay a leaf applies to the normal and durable hosts. It adds the leaf's test-only fixtures: a secret store for leaf 1, and a connection type for leaf 2.
- **Probe host:** the conformance probe's packaged consumer. It composes the Agent EF store, and each leaf adds its own Agent packages to it.

## User Scenarios & Testing

Each story's Independent Test also covers:
- the **common controls** (FR-009);
- the **common effect outcomes**, for each effect (FR-014);
- the **canary search** (FR-023).

Every contributed tool and effect has a conformance-probe case on both provider adapters (FR-022). Each step runs in one host.

### User Story 1 - Explicit answers, the privilege policy and *performed* (leaf 0) (Priority: P1)

A user asks about a module that is not installed or is disabled, or about a page that this feature does not support. Weaver answers from two live sources: the Studio module registry, which module load status shows, and the Weaver contributions each host registers. It never infers support from a route, or from M0's historical module list.

The user can also:
- see which privilege classes the integrator has enabled;
- see each effect's class in its review;
- see *performed* for an approved effect that persists nothing.

Where the Weather sample is installed, Weaver explains its displayed forecast.

**Why this priority**: Without it every narrowing would be silent, and no later leaf could use the privilege policy, *performed* or owner codes.

**Independent Test**:
1. In the normal host, run leaf 0's inventory refresh. Weather is composed in Studio, and ExtensionBuilder is disabled by default.
2. Deselect the structured logs features: the backend feature, its EF store and its Studio module. Its only dependant is its own EF store.
3. Ask Weaver about structured logs and the extension builder. Open `/diagnostics/structured-logs` and ask again.
4. Ask about the Weather forecast. Remove the Weather sample from Studio's shells, and ask again.
5. Enable ExtensionBuilder in the backend configuration, add its Studio module to Studio's shells, open it, and ask.
6. In the probe host, use the fixture module. Leaf 0 extends it with an administrative effect, an irrecoverable effect, an effect that persists nothing, and an effect whose owner code is outside its declared set or echoes a canary value. Propose each effect:
   - with the privilege classes unconfigured;
   - with all three classes enabled, as the Workbench configures them;
   - with a class switched off at each step: between review and approval, between approval and execution, and before recovery.
7. In the probe host, deselect the fixture module while a page nests its widget.
8. In the probe host, leave an effect pending, restart, remove the fixture module, and approve.

**Acceptance Scenarios**:

1. **Given** a module that the registry does not list, **When** the user asks about it, **Then** Weaver answers from the registry that it is not installed, and discloses only the route on the module's route.
2. **Given** a disabled module, or an enabled module without Weaver contributions, **When** the user asks for help on its page, **Then** Weaver says so and offers no tool.
3. **Given** the Weather sample is installed, **When** the user asks about the displayed forecast, **Then** Weaver explains it from the sample's client-asserted kind. **Given** it is removed, **Then** Weaver answers as for an absent module.
4. **Given** a deselected module, **When** a page that would nest its widget is open, **Then** nothing of that module is offered, and other modules' support is unchanged.
5. **Given** unconfigured privilege classes, **When** any classed effect is proposed, **Then** it is refused before review.
6. **Given** all three classes are enabled, **When** the same effects are proposed, **Then** each is reviewed, and its review shows its class.
7. **Given** a session, **When** it bootstraps, **Then** the server reports every class's state, and Studio shows the states read-only.
8. **Given** an effect whose class is off at the step being taken, **When** that step is approval, execution or recovery, **Then** the effect is refused at that step. At execution, nothing is dispatched.
9. **Given** an approved effect whose owning module reports that it persisted nothing, **When** the outcome is recorded, **Then** it is *performed*, and the review shell shows it as such.
10. **Given** an owner code outside the tool's declared set, **When** it is recorded, **Then** the outcome is reported with a generic code, and a canary in the code appears nowhere.
11. **Given** a pending effect whose module is removed, before or after a restart, **When** it is approved or recovered, **Then** it resolves as unavailable with zero effect ([spec 1189](../1189-weaver-recovery/spec.md)).

---

### User Story 2 - Inspect, describe, test, revoke and delete Secrets without exposing a value (leaf 1) (Priority: P1)

A user on the Secrets page (`/security/secrets`, or the alias `/secrets`) asks which secrets exist, why one is expired, or whether one is usable. The user may also ask Weaver to improve a description, or to revoke or delete a secret.

Weaver reads each secret's metadata:
- name, display name and description;
- type, store, scope, tags and status;
- the effective condition, such as expired, and the latest version's expiry, both computed by the owning module (FR-006);
- current version, revision, and created and updated times.

Weaver never discloses a value. It can propose:
- a metadata update;
- the owning module's value test;
- a revoke or delete, where the integrator enables the irrecoverable class.

**Why this priority**: This is the smallest row with the highest exclusion risk, and it sets the pattern for the others.

**Independent Test**: In the normal host with the test overlay, seed:
- an active secret, one whose latest version has expired, and a revoked secret, each holding a canary value;
- a secret whose test message echoes its value;
- a secret whose delete removes the value but then fails to save the metadata;
- a secret in another tenant.

Ask about the list on both routes, and about each secret. Improve one description, test one secret, revoke one and delete one. Approve each, and compare each receipt with the owning module's readback.

Negative cases:
- an actor without `secrets:read`, for reads;
- an actor without `secrets:write`, one without `secrets:test`, and one without `secrets:delete`;
- a target in another tenant;
- an edit or a rotation in another tab between review and approval;
- a test of a secret that does not exist, or that is inactive;
- a repository that cannot save at a revision;
- the Secrets API feature not composed;
- the administrative class or the irrecoverable class off at each step;
- the partial delete;
- in the durable host, a restart with an approved effect pending;
- requests to create, rename, reveal, rotate, import or export.

**Acceptance Scenarios**:

1. **Given** the list with a filter and a selection, **When** Weaver reads context, **Then** it receives the filter, the selected identities and their metadata, bounded by `secrets:read` and the tenant. An expired secret shows as expired.
2. **Given** an approved display-name or description update, **When** it is applied, **Then** the owning module saves it at the reviewed revision, and the receipt carries the readback.
3. **Given** a repository that cannot save at a revision, **When** an update, revoke or delete is proposed, **Then** it is unavailable.
4. **Given** an approved test, **When** it runs at the reviewed version, **Then** it is *performed*. The receipt carries only success or failure and the owner code. The value and the message are redacted.
5. **Given** a test that could not run because the secret does not exist or is inactive, **When** the outcome is recorded, **Then** it is refused with the owner code.
6. **Given** a revoke, **When** it is reviewed, **Then** the review states that the revoked version's value cannot be restored, and that rotating reactivates the secret with a new value.
7. **Given** a delete, **When** it is reviewed, **Then** the review states that it cannot be undone.
8. **Given** a revoke or delete review with no secret consumer source composed, **When** it is shown, **Then** it states "no consumers known".
9. **Given** a revoke or delete, **When** it is prepared, **Then** it needs `secrets:delete` and is refused unless the irrecoverable class is enabled. The reviewed revision is checked before any side effect. A delete's receipt is an absence readback, because a deleted secret is hidden.
10. **Given** the partial delete, **When** the outcome is recorded, **Then** it is *uncertain*, and it is never replayed.
11. **Given** the Secrets API feature is not composed, **When** a tool runs, **Then** the outcome is owner-unavailable (FR-006).
12. **Given** a request to create, rename, reveal, rotate, import or export, **When** Weaver receives it, **Then** Weaver refuses on the grounds in FR-001.

---

### User Story 3 - Understand, prepare, update, rebind, test, revoke and delete a Service connection (leaf 2) (Priority: P1)

A user on the Connections page, in its creation form, or at a connection picker in a workflow asks what a connection is or why it fails. The user may also ask Weaver to prepare, update, rebind, test, revoke or delete it.

Through a new owning-module read (FR-006), Weaver reads:
- name, display name, type, authentication scheme and status;
- settings field names and secret-bound field names;
- managing party and Owner field;
- tags, last test time and version.

Bound secret names and named-credential names are disclosed only under `secrets:read`. Otherwise only field names are disclosed. Settings values never are.

Weaver can propose:
- a prefill of the visible creation form;
- an update of the display name and tags;
- a rebind of a secret field;
- the owning module's test, which may contact an external service, depending on the type's provider;
- a revoke or delete, where the integrator enables the irrecoverable class.

Leaf 2 also makes Connections a secret consumer source, so that a secret's revoke or delete review can name the connections that use the secret.

**Why this priority**: "Why does my integration fail" is the commonest Connections question.

**Independent Test**: In the normal host with the test overlay, the test connection type records each test invocation, carries canary token fields, and returns a test message that echoes a resolved value. Seed:
- a connection with a canary settings value, a canary secret value and canary token values;
- a connection whose field uses an inline secret that the connection owns;
- an active, an expired and an inactive text secret in the tenant;
- an owned secret of another connection;
- a connection in another tenant.

Explain each connection. Then prefill the form, update a connection, rebind a field, test it, revoke it and delete it. Approve each, and compare the receipts with the recorded invocation and the connection's readback.

From the Secrets page, revoke and delete secrets that connections use.

Negative cases:
- an edit or a user-run test between proposal and approval, either of which advances the version;
- for reads, an actor with neither `workflows/design:read` nor `connections:manage`;
- an actor without `secrets:read`, for bound names, for owned-secret names in a delete review, and for a rebind. Such a rebind is refused before review, exactly as for an unknown secret;
- an actor without `connections:manage`, for updates, rebinds, revokes and deletes;
- an actor without `connections:bind-secret`, and one without `connections:test`;
- the Connections API feature not composed;
- a target in another tenant;
- a rebind to an expired, inactive or non-text secret, or to another connection's owned secret;
- a rebind away from the inline secret, whose cleanup then fails after commit;
- a test that cannot run, a test that runs and fails, and a test whose write fails after the external call;
- a test message or test owner code that echoes a resolved value;
- a dropped test response;
- a delete that removes owned secrets but then fails to remove the connection;
- a secret's revoke or delete review, for an actor who cannot read connections;
- the creation form closed before the prefill completes;
- the administrative class or the irrecoverable class off at each step;
- in the durable host, a restart with an approved effect pending;
- requests to create a connection, authorize OAuth, change the redirect URI, or set a settings value.

**Acceptance Scenarios**:

1. **Given** a connection, **When** Weaver reads it, **Then** it receives the metadata and version. It receives bound and named-credential names only under `secrets:read`.
2. **Given** the creation form, **When** Weaver reads it, **Then** it receives the type, scheme, field names and validation errors, and no entered settings or secret value.
3. **Given** an approved prefill, **When** it is applied, **Then** only the form's name, type and scheme change, applied locally. Creating the connection stays the user's action.
4. **Given** an approved update or rebind, **When** it is applied, **Then** it applies only at the reviewed version, under `connections:manage`. A rebind also needs `connections:bind-secret`, `secrets:read`, and an active text secret in the tenant that no other connection owns. The candidates shown to the client grant nothing.
5. **Given** a rebind away from an inline secret that the connection owns, **When** it is reviewed, **Then** it belongs to the irrecoverable class, because the connection owns that secret. The review states that the secret is deleted after commit, unless another connection still uses it, and that the deletion cannot be undone. If cleanup fails after commit, the effect is persisted with a warning.
6. **Given** an approved test, **When** it runs, **Then** the reviewed version is checked before the external call. When the type's test runs, the owning module records the test time and advances the version, and other pending proposals for that connection become stale. The receipt is persisted with whether the test ran, whether it succeeded, and the owner code. The message is redacted.
7. **Given** a test that cannot run, **When** the outcome is recorded, **Then** it is refused with the owner code. **Given** a test whose write fails after the external call, **Then** it is *uncertain*.
8. **Given** a connection revoke or delete, **When** it is reviewed, **Then** the review states that it cannot be undone and that workflows using the connection will fail. For a delete, the review also lists the owned secrets that will be deleted, by name under `secrets:read` and otherwise as a count. A secret that another connection still uses is kept and is not listed; only other connections are checked.
9. **Given** a connection revoke or delete, **When** it is prepared and run, **Then** it is refused before review unless the irrecoverable class is enabled. It applies only at the reviewed version, which is checked before any side effect. A delete's receipt is an absence readback, because the row is gone.
10. **Given** a delete that removes owned secrets but then fails to remove the connection, **When** the outcome is recorded, **Then** it is *uncertain*.
11. **Given** a secret's revoke or delete from the Secrets page, **When** it is reviewed, **Then** it lists the connections using the secret, as an advisory list. The list shows names when the actor may read connections, and otherwise a count.
12. **Given** an inline secret that a connection owns, **When** a revoke or delete is proposed from the Secrets page, **Then** Weaver refuses it. This is stricter than the native page: the Secrets module has no notion of connection ownership. Weaver's refusal relies on the owning connection that the secret consumer source reports, because the operation would irrecoverably break that connection. The user can still act on the native page, or remove the binding from the connection first.
13. **Given** the Connections API feature is not composed, **When** a tool runs, **Then** the outcome is owner-unavailable (FR-006).

### Edge Cases

- **A module unloads during a turn.** Its context is re-admitted without it.
- **The Studio host and the backend differ.** Each context names the host it describes.
- **A nested picker is open while an Operation Batch is pending.** The picker's own context never changes the batch's target.

## Requirements

### Functional Requirements

#### Delivery matrix

- **FR-001**: Leaf 0 MUST refresh the normal host's live installed and composed module inventory, and record the result on #3443. M0's loaded-module list is historical (#3443, 30 October).
  - **M0 rows:** each M0 row records M0's inventory, which the refresh confirms. Where the refresh finds a row absent or changed, leaf 0's PR updates it with ground A or with its new outcome.
  - **New screens:** a screen the refresh finds that the matrix lacks is added as a row by leaf 0's PR, and assigned under FR-025.
  - **Outcomes and grounds:** every row MUST end at its outcome and evidence. A narrowing of a row's target, and every existing owner operation that Weaver does not offer, MUST state its grounds.

  Outcome words:
  - **Read:** read and explain.
  - **Reviewed:** a reviewed effect.
  - **Local:** a reviewed edit of visible, unsaved state.
  - **Delivered by #3442:** M3 delivers the row.
  - **None in M4:** not supported in M4.

  Grounds:
  - **A:** absent from, or disabled in, the refreshed inventory.
  - **B:** the Program boundary, meaning secret values, package promotion and package activation, and module and feed administration. Feature selection apply is outside B where the integrator enables the activation class (owner decision 2).
  - **C:** a credential, file or value would pass through Weaver.
  - **D:** the owning module does not hold the setting durably or cluster-wide.
  - **H:** a human's decision.
  - **N:** no owner operation exists.
  - **O:** another issue or open decision owns it.
  - **S:** the screen does not show it.
  - **T:** outside the row's target. For an M0 row, the target is its M0 target. For a post-M0 row, it is the target stated here, which is this spec's Program-authority choice and is listed as open to owner review.
  - **V:** the owning module's write is reachable only from the browser, so the server cannot verify its result.
  - **W:** needs a module-owned working-state adapter.
  - **X:** needs its own exclusion design before any disclosure.

  This spec specifies the Weather, ExtensionBuilder, Secrets and Service connections rows. Each other row states its outcome here, and its leaf spec states its effects, contracts and scenarios.

  | Row | Owning module | Target | Outcome; narrowings (grounds) |
  | --- | --- | --- | --- |
  | Weather | Samples | Qualify when installed (M0) | Read when installed (story 1); None in M4 when absent (A) |
  | ExtensionBuilder | ExtensionBuilder | Optional inspection and guidance (M0) | None in M4 (A while disabled; X once enabled, because its state includes workspace source) |
  | Secrets | Secrets | Reference, metadata and availability inspection; domain effects (M0) | Read; Reviewed metadata update, value test, revoke and delete (story 2). Value-bearing create and rotate (B); configuration-key create and rotate (T); rename, import and export (N); the workflow property's Secrets editor (O: #3711) |
  | Service connections | Connections | Metadata, bindings and diagnostics; changes and tests (M0) | Read; Local prefill; Reviewed update, rebind, test, revoke and delete (story 3). Settings values (X: no field declares its sensitivity); create and OAuth authorize (C); redirect URI (N: a read-only public URL) |
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

- **FR-002**: **Server packages.** This spec decides, under Program authority, that there is one Agent package per owning module. #3428 allows optional integrations to be "separately packaged with their owning module"; the guards below rule out putting them inside Essentials owners.
  - **Name and path:** each package is named `Elsa.Agent.<Domain>`, at the path the architecture guard derives.
  - **Why the Agent bucket:** the ExtensionBoundary guard forbids every core project from referencing an extension.
  - **Why not framework §2.2's secondary-domain sub-rule:** that rule covers features with no models of their own, and these packages define their own Context Kind, tool and result models.
  - **References:** a package references its owning module's `.Core` where one exists, and the Agent packages in its own bucket. It registers the server-side descriptor of every client tool its module offers.
  - **Modules without a `.Core`:** a package for such a module admits only client-asserted kinds. It exists only where SC-004's per-package deselection needs it.
  - **Cross-bucket references:** where the owning module sits in another extension bucket, such as ETL or ExtensionBuilder, the reference is declared in the ExtensionBoundary guard's `DeclaredExtensionEdges`, with a justification comment.
  - **Probe host:** each leaf adds its packages, and any owning modules it needs, to the probe's packaged consumer. Leaf 1 adds `Elsa.Agent.Secrets`. Leaf 2 adds `Elsa.Agent.Connections` and Connections.

  Leaf 1 adds `Elsa.Agent.Secrets`, which reads only Secrets `.Core`. Leaf 2 adds `Elsa.Agent.Connections`, which serves the Connections module and is distinct from the existing `Elsa.Agent.Specialists.Connections`. #3442 slice F (#3732, merged) added `Elsa.Agent.Workflows.Runtime`, which later leaves extend.
- **FR-003**: **Studio packages.** A Studio contribution MUST live in its owning Studio module, through the public SDK.
  - **Satellites:** a satellite in the shipped form `Elsa.Studio.Weaver.<Domain>`, under `Extensions/Weaver/studio/` beside `Elsa.Studio.Weaver.Workflows`, is added only where a host must be able to keep the module but drop its Weaver support.
  - **Naming:** framework §2.2's secondary-domain sub-rule does not apply, for the same reason as in FR-002: a satellite defines its own renderers and Weaver-facing models.
  - **Built-in Studio Screens:** these need a satellite, because every host keeps the shell. They are owned by `Elsa.Studio.Hosting`, and their contributions go in `Elsa.Studio.Weaver.Hosting`. The first leaf that needs that package creates it, together with the SDK read seams it uses (leaf 4).
- **FR-004**: **Result Renderers and module guidance.** Each module MUST ship, in its own packages, the Result Renderers for its tool result kinds and its own bounded, versioned module guidance. Module guidance is trusted installed-module text, kept distinct from resource content. Shared Weaver code holds neither.

#### Generic changes

- **FR-005**: Weaver core, the shell's collector and review shell, and the provider adapters MUST gain no row's domain knowledge. The generic changes are:
  - **Privilege policy** (FR-015), including the server reporting every class's state at bootstrap.
  - **Review shell:** shows each effect's class and the *performed* outcome.
  - ***Performed*** (FR-016).
  - **Owner codes:** declared owner-code sets, and their validation (FR-016).
  - **Principal seam:** hands a tool the caller's authenticated principal at preparation, approval and execution, and at the explicit authenticated execute that runs a recovered approval (FR-006). Today a tool's authorization receives only the actor and tenant.
  - **Registry answer:** the registry-based answer of story 1.
  - **Probe fixture:** effects for the administrative class, the irrecoverable class, *performed*, and owner codes.
  - **SDK read seams** for `Elsa.Studio.Weaver.Hosting`, delivered by leaf 4 (FR-003).
  - **New outcome codes**, only where no existing code fits.

  Leaf 0 delivers every change on this list except the SDK read seams. Leaf 0 also qualifies Weather where the refresh finds it installed. This spec's contracts record these decisions. Leaf 0 adds a dated one-line pointer, "Amended by spec 1203", to spec 1186's session-policy contract and to spec 1189's recovery contract.

#### Owner prerequisites

- **FR-006**: Where a row needs a read, an authorization or an operation that today exists only behind the owning module's HTTP endpoints, or outside its `.Core`, the owning module MUST supply it as follows.
  - **Contract and implementation:** a `.Core` contract that Weaver can call under the same principal, implemented in the owning module's `.Api` package. That package already references the identity contracts.
  - **Principal:** the contract takes the caller's authenticated principal from the leaf 0 seam (FR-005), checks that the actor matches the principal, and decides permissions through `IPermissionAuthorizationService`.
  - **Absent contract:** if the `.Api` feature or the authorization service is not composed, the contract is absent and the tool's outcome is owner-unavailable. A permission gate that defaults to allowing everything is never relied on.
  - **Exceptions:** infrastructure exceptions are wrapped (framework §2.23.5).
  - **Writes:** a write to an existing resource takes an expected revision and checks it before any side effect. A composition that cannot honor the revision reports the write as unsupported, so the write never runs unconditionally. A create, where no resource exists, uses the owning module's create-if-absent.
  - **Native pages:** the shipped native pages keep working, each with a regression test.

  Plan decision P2 owns the type shape. Leaves 1 and 2 need the prerequisites below, and each later leaf spec lists its own under this rule.

  | Owning module | Prerequisite |
  | --- | --- |
  | Secrets (leaf 1) | A metadata read carrying the revision, the effective condition and the latest version's expiry. A revisioned metadata update. A value test at an expected version that reports whether it ran. Revoke and delete at the revision, each unavailable without a revision-aware repository. A fan-in for secret consumers: the return-style contributor `ISecretConsumerSource` and the named Sequential event `SecretConsumersCollecting`, both in Secrets `.Core`, with exactly one aggregating handler, `CollectSecretConsumers` (framework §2.6.1). The list it collects is advisory, because consumer checks are not atomic. |
  | Connections (leaf 2) | A read that adds the version and the bindings, omits settings values, and shows bound and named-credential names only under `secrets:read`. An update (display name and tags) and a rebind, each at an expected version. The rebind checks `secrets:read`, refuses as it would for an unknown secret, and refuses another connection's owned secret, which the native validation does not check. A test at an expected version, checked before the external call, whose result adds a "ran" flag. Revoke at an expected version. A delete that checks the version before deleting any owned secret, then removes the row with the repository's version check. An `ISecretConsumerSource` implementation that reports connections, both binding and owning ones. |

- **FR-007**: **Read authority.** A rule that is exactly one permission MAY be checked through the tool's declared permission, decided by `IPermissionAuthorizationService` for the caller's principal. Any other rule MUST be decided by the owning module's contract (FR-006), never reimplemented in a Weaver package.
  - **Cross-module names in reviews** follow the other module's read rule:
    - a secret's review names its consuming connections only when the actor may read connections, and otherwise gives a count;
    - a connection's delete review names owned secrets only under `secrets:read`, and otherwise gives a count.
- **FR-008**: Deployment shapes MUST keep working (constitution §E2.2.3). Design-side contributions register only where Design is composed, and Runtime-side contributions only where Runtime is. An absent side is explicitly unavailable.

#### Context and disclosure

- **FR-009**: **Common controls.** Every Context Kind and tool MUST have a negative case for each control below, tested at the cheapest level that proves it (ADR 0080 D1, framework §2.23):
  - **Module removed:** nothing of the module is offered.
  - **Denied, or a cross-tenant target:** nothing is disclosed, not even the target's existence. A denied effect is refused before review.
  - **Owner-unavailable:** nothing from that owning module is disclosed.
  - **Stale target:** zero effect, and navigation never retargets.
  - **Excluded data:** seeded canaries appear nowhere.
  - **Untrusted content:** resource content and every client-asserted kind are marked untrusted, distinct from module guidance.

  Content the user explicitly attaches, such as selected console lines, is the user's own disclosure. It is client-asserted and untrusted, and it is excluded from the canary search.
- **FR-010**: Each Context Kind MUST be module-owned, versioned, bounded and authorized. It MUST be attributed to an exact identity:
  - the owning module's revision, where one exists;
  - for a list, the visible query and selection;
  - for unsaved state, the client binding and base revision.

  Server context providers admit only their own kinds, and produce their permission and catalog kinds from their own reads. A tool never takes data from another package's kind. A nested picker contributes its own kind from its own module: for example, the Connections picker in a workflow property form is a Connections contribution. A form's own fields belong to the form's module.
- **FR-011**: Each Context Kind MUST declare its bounds. Each leaf adds tests proving two things:
  - its densest pages, nested pickers included, stay within the attachment count at the largest set each page shows by default;
  - each kind stays within the per-attachment bounds.

  A session that reaches its disclosure budget is refused, as that budget defines.
- **FR-012**: Each disclosure allowlist MUST be authored by the module that defines the data's meaning, and ship in that module's Agent package. It excludes unlisted fields by default. It is proven by a canary in an unlisted field that the owning module's own redaction does not already handle.

#### Effects

- **FR-013**: Every effect MUST declare its risk, privilege class, stale check and receipt, and each leaf spec states them explicitly. Risk follows its shipped meanings: `Destructive` removes or replaces existing state (spec 1193 FR-012; Studio ADR 0003), and `Admin` marks sensitive or administrative state. Gating comes from the privilege class, not from the risk label. For leaves 1 and 2:

  | Effect | Risk | Privilege class | Stale check | Receipt |
  | --- | --- | --- | --- | --- |
  | Secret metadata update | `Admin` | administrative | revision | readback; persisted |
  | Secret value test | `Admin` | administrative | version | *performed*, with success or failure and the owner code; refused when it could not run |
  | Secret revoke | `Destructive` | irrecoverable | revision | readback; persisted |
  | Secret delete | `Destructive` | irrecoverable | revision | absence readback; persisted; *uncertain* when partial |
  | Connection prefill | `ReviewRequired` | none | client binding | applied locally |
  | Connection update, or a rebind of a field whose secret the connection does not own | `Admin` | administrative | version | readback; persisted |
  | Rebind of a field whose inline secret the connection owns | `Destructive` | irrecoverable | version | readback; persisted, with a warning if cleanup failed |
  | Connection test | `Admin` | administrative | version, checked before the external call | persisted, with whether the test ran, its result and the owner code; refused when it could not run; *uncertain* when the write failed after the external call |
  | Connection revoke | `Destructive` | irrecoverable | version | readback; persisted |
  | Connection delete | `Destructive` | irrecoverable | version | absence readback; persisted; *uncertain* when partial |

- **FR-014**: **Common effect outcomes.** Every effect MUST be tested for each of these, at the cheapest level that proves it:
  - **Review:** the effect is reviewed in every autonomy mode, including `full-auto`.
  - **Refusal at a step:** the policy is read live at each step. The effect is refused at preparation, approval, execution or recovery whenever its class is off at that step, and a refusal is not sticky. At execution, nothing is dispatched. Without the owning module's permission, the effect is refused before review.
  - **Owner unavailable at approval:** owner-unavailable, with zero effect.
  - **Module removed at approval:** unavailable, with zero effect.
  - **Restart:** in the durable host, a pending approval survives a restart. A recovered approval runs only on an explicit, authenticated execute, which rechecks the revision, the permission and the class.
  - **Lost or uncertain response:** an effect whose response is lost after dispatch, or that may have run before a restart, is *uncertain*. It is never replayed, and the session owner resolves it as spec 1189's recovery contract defines.
  - **Local edits:** these carry correlated acknowledgements, and are unavailable with zero effect if their page is gone.
- **FR-015**: **Privilege policy** (owner decisions 1, 2 and 4). The Agent module MUST gain an integrator-configured privilege policy. It is a backend host option, never a session setting. It has three independent classes, and a host may enable the irrecoverable class alone.
  - **administrative:** `Admin` effects. An `Admin` effect that declares no other class belongs here by default.
  - **irrecoverable:** effects whose consequence cannot be undone. A module MUST declare this class for its own irrecoverable effects, whatever their risk label; leaves 1 and 2 do. A third-party irrecoverable effect that declares no class and is not `Admin` is not caught. This is a stated limitation.
  - **activation:** feature selection apply on the Server host.

  The policy MUST also follow these rules:
  - **Declaration:** the owning module declares the class at preparation, because core cannot infer an effect's consequence. The declared class is frozen into the approval digest.
  - **Defaults:** every class is off by default, so a host that leaves the policy unconfigured refuses every classed effect. The Workbench, as the reference integrator, enables all three classes in its base configuration, so in every environment.
  - **Enforcement:** the policy is read live at each step (FR-014).
  - **Display:** Studio shows the policy read-only, as part of its Host Policy, from the state the server reports at bootstrap.
- **FR-016**: **Outcomes.**
  - ***Performed*:** leaf 0 MUST add *performed* (owner decision 3). The approved action ran, and the owning module persisted no change. *Performed* is a new proposal status that reports the action as executed, with no persisted effect scope. Every server and Studio status mapping handles it.
  - **Owner codes:** each tool's descriptor declares a closed set of owner codes, and Agent core validates every code in receipts, refusals and preparation errors. A code outside the declared set is reported as a generic code, which makes the canary rule decidable.
    - An `Elsa.Agent.<Domain>` package maps its owning module's native codes into its set. This also resolves a native collision such as the two "not found" codes of the Secrets manager and its configuration store.
    - A receipt carries its code in a new receipt member, `OwnerCode`.
    - Codes mapped from an owning module are dotted ASCII identifiers outside the `agent.` namespace. The existing Agent-package codes keep their `agent.` prefix, and existing tools' descriptors declare their current codes.
  - **Existing outcomes:** every other outcome reuses the existing `agent.*` codes and outcomes. *Uncertain* keeps its existing shape and generic message, and the session owner resolves it (spec 1189). A new code is added only where none fits, such as the privilege-policy refusal. A status alone never stands as evidence of an effect.

#### Contracts, catalogs and records

- **FR-017**: Replacement contracts MUST detect a second registration (framework §2.6.2). The Agent-wide gap for existing contracts remains #3633. Visibility follows framework §2.23.3 and ADR 0080 D3.
- **FR-018**: **SemVer.** Every change MUST be classified under framework §4.2. Pre-GA Majors follow ADR 0080 D4 and Elsa constitution §E5, as spec 1193 FR-033 did.
  - **Major:**
    - *performed*, a new value appended to the proposal-status enum and its status string, which changes model semantics. The plan names every server and Studio mapping site.
    - the privilege policy, because with every class off by default it refuses `Admin` tools, third-party ones included, that ran before.
  - **Minor:**
    - declared owner-code sets and the `OwnerCode` receipt member;
    - new members on existing stores and contracts, added so that existing implementations keep compiling (the plan names the mechanism);
    - new contracts, events, model members, packages, Context Kinds, tools, renderers, codes and permissions.

  Each leaf spec classifies its own changes under these rules.
- **FR-019**: **Catalogs** MUST change in the same unit of work as the code (framework §2.22.1):
  - **Leaf 0:** `Extensions/Agent/src/Elsa.Agent/EXTENSION_POINTS.md` gains the privilege-policy option, the class declaration, owner-code sets, the principal seam and the *performed* status.
  - **Leaf 1:**
    - `Essentials/src/Secrets/Core/EXTENSION_POINTS.md` gains the `SecretConsumersCollecting` event, the `ISecretConsumerSource` contributor and the `CollectSecretConsumers` handler;
    - `Essentials/src/Secrets/Elsa.Secrets/EXTENSION_POINTS.md` is updated;
    - `Essentials/src/Secrets/Persistence/EntityFrameworkCore/EXTENSION_POINTS.md` is updated.
  - **Leaf 2:**
    - `Essentials/src/Connections/Core/EXTENSION_POINTS.md` is updated;
    - `Essentials/src/Connections/Elsa.Connections/EXTENSION_POINTS.md` gains the `ISecretConsumerSource` implementation, as a cross-domain Known implementation;
    - the Secrets Core catalog lists that implementation.
  - **Leaf 4:** `Studio/src/essentials/Elsa.Studio.Api/EXTENSION_POINTS.md` gains the SDK read seams.
  - **Every leaf:**
    - its contributors, entered in the Agent catalog as cross-domain Known implementations;
    - each new package's own catalog, where it exposes extension points;
    - the root index (framework §2.22.2);
    - each package README, listing its cross-domain contributions.
- **FR-020**: **Records.** Evidence goes on each PR, #3443 and #3428, which owns the matrix and evidence ([program goal](../../docs/program-goals/weaver-across-studio.md)). This spec's PR records the owner's boundary amendment in the program goal: activation is in scope only for hosts that enable the activation class.

  Leaf 0 MUST file the follow-ups below as `needs-triage` (AGENTS.md; [repository hygiene](../../docs/contributing/repository-hygiene.md#issues)). Those that are this Program's work are triaged under the 29 October delegation, and each is recorded on #3428.
  - the ExtensionBuilder exclusion design (X);
  - field-level sensitivity for connection settings values (X);
  - a server-verifiable path for browser-only writes (V);
  - a durable, cluster-wide Workflow Runtime Diagnostics Setting (D);
  - console record reads (X);
  - a log time range and retention view (S);
  - working-state adapters for ETL creation, task forms, BPMN processes and activity contract proposals (W);
  - irrecoverable-class coverage for third-party effects that declare no class (FR-015).

  Nothing that already exists is filed. Context-budget progress goes on #3710, and Full payloads are recorded as Studio ADR 0009's open decision. Leaf 0 also records on #3428:
  - every narrowing;
  - the deferrals to other programs: #3604, and #3519's authoring;
  - the decisions open to owner review.
- **FR-021**: **Glossary.** Leaf 0 MUST make these changes.
  - **In the [Elsa glossary](../../docs/glossary/elsa.md)**, beside its Weaver and agent terms, add three backend Agent concepts, each linked from Studio/CONTEXT.md:
    - *Privilege Class*: a class of effects that the integrator enables in the Agent module's configuration (FR-015);
    - *Performed*: placed next to a pointer to spec 1186's outcome definitions (FR-016);
    - *Owner code*: FR-016.
  - **In Studio/CONTEXT.md:**
    - **Replace** *Module Load Status*'s state list, "discovered, accepted, activated, disabled, or rejected", with the states the host reports: available, loaded, disabled, incompatible and failed, plus the registry's pending and unknown. Its _Avoid_ line, "Diagnostics signal, observability widget", stays.
    - **Widen** *Privileged Tool* to mean a Weaver Tool whose effect belongs to a privilege class.
    - **Add** *Built-in Studio Screen*: a screen the `Elsa.Studio.Hosting` client renders itself.
    - **Add** *Client-Asserted Kind*: a Context Kind whose content only the browser supplies. It is untrusted and confers no authority, and the server checks only its schema and bounds.

#### Evidence

- **FR-022**: Each story MUST have end-to-end evidence. Fixture cases run in the probe host, and the restart cases of stories 2 and 3 run in that leaf's durable host. Every contributed tool and effect MUST have a conformance-probe case on both provider adapters: Copilot's delegated loop and Anthropic's host-driven loop. Negative cases are tested at the cheapest level that proves them.
- **FR-023**: The canary search MUST cover HTTP bodies, SSE frames, adapter payloads, tool results, owner codes, review cards and audit records.
- **FR-024**: Evidence MUST keep three categories distinct:
  - deterministic component tests;
  - deterministic provider-protocol native evidence on the actual Copilot and Anthropic adapters, with packaged consumers;
  - opt-in live-provider evidence (none claimed).

  Probe evidence is frozen at the exact reviewed head of its PR. Failures, skips and quarantines are recorded as they occurred.

#### Later leaves

- **FR-025**: Each later leaf MUST get its own spec, numbered at the time with the next free number. The spec is written and reviewed when the previous leaf merges, because #3428 elaborates later leaves only when their slice is concrete. A leaf spec is bound by this spec's cross-cutting requirements, FR-002 to FR-024, and by its FR-001 rows. It specifies those rows' effects, owner prerequisites, risks, classes and scenarios.

  Leaf 0's PR assigns every row its refresh adds, either to an existing leaf or to a new one. #3443 cannot close while any row is unassigned.

  | Leaf | Rows | Owning modules | Scope |
  | --- | --- | --- | --- |
  | 3 | Structured logs, traces | StructuredLogs, OpenTelemetry | allowlisted log and trace reads; correlation |
  | 4 | Diagnostics overview, console, module load status | Studio Hosting; `Elsa.Studio.ConsoleStream` with console log streaming | screen reads; console lines the user attaches; creates `Elsa.Studio.Weaver.Hosting` and its SDK read seams |
  | 5 | Home | Studio Dashboard, Studio Preferences, Attention, Workflows.Dashboard | widget snapshots, personal layout, snooze |
  | 6 | Workflow Runtime Diagnostics Setting, activity availability | Workflows Runtime, Activities Design | host-scoped settings changes |
  | 7 | Features, appearance, theme builder | FeatureManagement, Modularity, Studio Hosting, Hosting.Themes | feature draft and Server-host apply; local appearance and builder edits; the *Theme Selection Preference* glossary widening |
  | 8 | Modules, feeds, backends, settings | Hosting.Nuplane, Studio Backends, Studio Hosting | administration reads |
  | 9 | ETL read | ETL | exact design and runtime reads |
  | 10 | ETL effects | ETL | draft update, run cancellation |
  | 11 | Activity definitions | Activities Design, Workflows.Publishing, Elsa3 | reads, drafts, upgrade plans |
  | 12 | User tasks, agents, BPMN | `Elsa.Studio.UserTasks` with Workflows Runtime, Agent Specialists, Activities BPMN | reads; task claim, release and reassign. Reassign is escalated in that spec if it stays `Admin` |
  | 13 | Expressions | Expression Modules | language-owned guidance and exact expression edits, coordinated with #3711 |

### Key Entities

- **Coverage row:** an inventory screen with its owning module, target, outcome, narrowings and evidence (FR-001).
- **Disclosure allowlist:** the fields whose values may be disclosed, authored by the owning module (FR-012).
- **Effect:** a reviewed operation with its risk, privilege class, stale check, receipt and common outcomes (FR-013, FR-014).
- **Narrowing:** a recorded gap between a target and the delivered outcome, with its grounds (FR-001).

## Success Criteria

### Measurable Outcomes

- **SC-001**: Every FR-001 row has linked evidence on #3428, from this spec's leaves or from its leaf spec. Every narrowing has its grounds, and no row is unassigned.
- **SC-002**: Stories 1–3 pass their acceptance scenarios in the hosts FR-022 names. A host that leaves the privilege classes unconfigured refuses every classed effect.
- **SC-003**: Seeded canaries appear in none of the channels FR-023 lists. Content the user explicitly attaches is excluded from the canary search (FR-009).
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
- **Delivery:** each leaf is its own independently reviewed PR. Leaf PRs say "Refs #3443", and the leaf that closes #3443 says "Fixes #3443". This follows spec 1193's Refs-then-Fixes precedent, an exception that [merging](../../docs/contributing/merging.md) allows.
  - **Dependencies:** leaves 1 and 2 depend on leaf 0, and leaf 2 builds on leaf 1.
  - **Status:** the PR that merges this spec sets *Approved*. Leaf 0's PR sets *In progress*. The closing leaf's PR sets *Implemented*, because it completes the last FR-001 row ([spec lifecycle](../../docs/reference/spec-lifecycle.md)).
  - **Bookkeeping:** every leaf comments on #3428 and #3443 at each state transition, and reconciles #3443 and its Project item (AGENTS.md program bookkeeping).
- **Program owner decisions, 2026-11-03** (Sipke Schoorstra; [receipt on #3428](https://github.com/elsa-workflows/elsa-foundation/issues/3428#issuecomment-6612627295)). These are owner decisions, not open to review here:
  1. **Irrecoverable deletes and revokes** of secrets and Service connections: "Let the integrator decide what privileges weaver has via configuration." They are delivered in the irrecoverable class (FR-015).
  2. **Feature selection apply (activation):** "Let the integrator decide via configuration." It is delivered on the Server host in the activation class. This widens #3443's activation boundary ("a manage claim alone cannot expand it", 31 October), but only for hosts that enable the class. Package promotion and package activation stay ground B, and the Studio host's apply stays ground V.
  3. **A generic outcome for an approved action that persists nothing:** "Add it in leaf 0." This is *performed* (FR-016).
  4. **The privilege default:** "Off; Workbench always on." Every class is off by default. The Workbench enables all three in its base configuration, so in Production too.
- **Decisions taken under Program authority, open to owner review** (as spec 1193 records its own):
  - **Packaging:** one Agent package per owning module (FR-002).
  - **Privilege classes:** membership is by consequence, declared by the owning module and frozen into the approval digest. The classes are independent. A rebind's class follows whether the connection owns the secret that the rebind leaves.
  - **Secret consumers:** reported through `ISecretConsumerSource`, as an advisory list. Weaver refuses to revoke or delete a connection-owned inline secret, more strictly than the native page.
  - **Owner codes:** declared closed sets, mapped by each Agent package.
  - **Expressions:** the Expressions leaf delivers the M0 target, coordinated with #3711.
  - **Post-M0 targets:** user tasks get read, claim, release and reassign, with reassign flagged for escalation in its leaf spec. Agents, BPMN and backends are read-only.
  - **Runtime capture semantics** stay untouched. Full payloads wait for Studio ADR 0009.
  - **Browser-only writes** are narrowings (V): the Theme Store, backend profiles and the Studio host's feature apply.
  - **Home layout:** personal layout changes are delivered. The default layout stays administration (T).
  - **Run controls:** ETL cancellation is reviewed, because its M0 target names explicit cancellation. BPMN run operations are outside BPMN's post-M0 target (T), as spec 1193 refuses workflow run controls.
- **Plan decisions:**
  - **P1:** each package's shell feature.
  - **P2:** the FR-006 contract types, the principal seam's shape, the revision mechanisms, and where native permission constants live.
  - **P3:** every non-atomic compare-then-write window, and how it is bounded.
- **Overlap**, reconciled without reparenting:
  - ETL: #2894, #2906 and the open ETL defects (#3709, #3719, #3721, #3724, #3562).
  - Home dashboards: #3604.
  - The parked Diagnostics Observability Readiness and Runtime Execution Evidence goals stay parked.
  - User Tasks: #3688, which is complete.
  - Studio Expression Developer Experience: #2310 (#3711, #2349). The Expressions leaf adds no children under #2310.
  - Elsa 3.8/3.9 Parity: #2771, which owns Elsa 3 import.
  - The [BPMN Engine program](../../docs/program-goals/bpmn-engine.md).
  - Agents: #3519. Runtime agents: #2892.
  - Weaver: #3710, #3720, and #3761, which tracks a status-mapping mismatch found while writing this spec.
  - Feed credentials: #3735.
- **Out of scope:**
  - releases, deployments and provider purchases;
  - live-provider credentials;
  - blanket administration and arbitrary workspace edits;
  - package promotion and package activation. Feature selection apply is in scope only through the activation class (owner decision 2).
  - Integrated qualification remains #3444.

## Issue Traceability

| #3433 criterion | Requirements and stories |
| --- | --- |
| Home/dashboard, ETL, Connections, Secrets metadata, Diagnostics, runtime evidence, activity availability and installed settings/administration surfaces have module-owned support | FR-001 to FR-004, FR-025; stories 2–3 |
| Each supported action has context, tool, renderer/effect and negative-case evidence appropriate to its domain; absent mutation support is explicit | FR-001, FR-004, FR-013, FR-014, FR-022 |
| Secret values and unrelated data are not exposed; runtime/model semantics and existing domain ownership remain intact | FR-006, FR-007, FR-009, FR-012; Assumptions (runtime capture semantics); FR-002 |
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
