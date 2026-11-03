# Feature Specification: Module-owned Weaver support for the current Studio inventory

**Feature Branch**: `agent/claude/3443-weaver-module-support`

**Created**: 2026-11-03

**Status**: Draft

**Input**: [Feature #3443](https://github.com/elsa-workflows/elsa-foundation/issues/3443) is M4 ([#3433](https://github.com/elsa-workflows/elsa-foundation/issues/3433)) of [Program #3428](https://github.com/elsa-workflows/elsa-foundation/issues/3428). It is [in progress](https://github.com/elsa-workflows/elsa-foundation/issues/3443#issuecomment-6612633571).

The Program owner's standing authorization covers autonomous delivery of #3443's six acceptance criteria, within the issue's boundaries. The owner decisions under Assumptions amend those boundaries. The current inventory is the finite matrix. The authorization grants no blanket administration permission, arbitrary workspace editing, package promotion or activation. Consequential decisions outside that scope go to the Program owner (#3428, 29 October continuation).

The scope covers:
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

Leaves 1 and 2 depend on leaf 0, and leaf 2 builds on leaf 1. Each later leaf gets its own spec (FR-025). The plan PR adds `contracts/privilege-policy.md` and `contracts/outcomes.md`.

**Terminology**:
- **Glossary terms:** Studio terms keep their meanings in [Studio/CONTEXT.md](../../Studio/CONTEXT.md). *Service connection* keeps its meaning in the [Elsa glossary](../../docs/glossary/elsa.md). FR-021 lists the glossary changes.
- **Outcome words:**
  - *Proposed*, *approved*, *persisted*, *refused* and *uncertain* are defined by [spec 1186](../1186-weaver-effects/spec.md).
  - *Applied locally* is defined by spec 1186 FR-003 and by #3428.
  - Leaf 0 adds *performed*: the approved action ran, and the owning module persisted no change to its resource.
- **"Owner"** always carries a qualifier:
  - the *owning module* owns a row's resource;
  - the *Program owner* is Sipke Schoorstra;
  - the *session owner* resolves an uncertain effect ([spec 1189](../1189-weaver-recovery/spec.md));
  - a Service connection's *Owner field* is part of its metadata.
- **Owner code:** a code a tool reports from its declared set (FR-016).
- **Secret consumer:** a resource that uses a secret, such as a Service connection that binds or owns it. The term is kept distinct from Studio's *Secret Reference*.
- **Host attachment budget:** the per-collection attachment count and per-attachment bounds of Weaver's context admission, plus the session disclosure budget ([#3710](https://github.com/elsa-workflows/elsa-foundation/issues/3710)).

**Hosts**:
- **Normal host:** the Workbench backend (`Elsa.Workbench`) and Studio (`Elsa.Studio.Web`), with Design and Runtime combined, in the Development configuration.
- **Durable host:** the normal host with the overlay that the Workbench's durable-store test applies, which adds the Agent EF store. The Secrets EF store is already composed. For leaf 2, the durable host also adds the Connections EF store; leaf 2 adds the Workbench's project reference and catalog entry for it.
- **Test overlay:** the overlay a leaf applies to the normal and durable hosts, adding that leaf's test-only fixtures (FR-002).
- **Probe host:** the conformance probe's packaged consumer. Its baseline does not compose the Agent EF store. Its restart runs enable the Agent EF store together with Secrets through runtime configuration, and its restart helper refuses a baseline that already selects Secrets. Leaves 1 and 2 change that baseline (FR-002).

## User Scenarios & Testing

Each story's Independent Test also covers:
- the **common controls** (FR-009);
- the **common effect outcomes**, for each of its effects (FR-014);
- the **canary search** (FR-023).

Every contributed tool and effect has a conformance-probe case on both provider adapters (FR-022). Each step runs in one host.

### User Story 1 - Explicit answers, the privilege policy and *performed* (leaf 0) (Priority: P1)

A user asks about a module that is not installed or is disabled, or about a page this feature does not support. Weaver answers from two live sources: the Studio module registry, which module load status shows, and the Weaver contributions each host registers. It never infers support from a route, or from M0's historical module list.

The user can also see which privilege classes the integrator has enabled, and each effect's classes in its review. An approved effect that persists nothing shows as *performed*.

**Why this priority**: Without it, every narrowing would be silent, and no later leaf could use the privilege policy, *performed* or owner codes.

**Independent Test**:
1. In the normal host, run leaf 0's inventory refresh. ExtensionBuilder is disabled by default.
2. Deselect the structured logs features: the backend feature, its EF store and its Studio module. The EF store is its only dependant.
3. Ask Weaver about structured logs and the extension builder. Open `/diagnostics/structured-logs` and ask again.
4. Open the Weather sample and ask about the forecast.
5. Enable ExtensionBuilder in the backend configuration, add its Studio module to Studio's shells, open it, and ask again.
6. In the probe host, use the fixture module. Leaf 0 extends it with four effects:
   - an administrative effect;
   - an irrecoverable effect;
   - an effect that needs both classes;
   - an effect that persists nothing.

   Leaf 0 also extends it with a tool that declares an owner-code set, and that emits a code outside the set or a code that echoes a canary.
7. Propose each effect:
   - with the privilege classes unconfigured;
   - with all three classes enabled, as the Workbench configures them;
   - with a class switched off at each step: between review and approval, between approval and execution, and before recovery.
8. In the probe host, deselect the fixture module while a page nests its widget.
9. In the probe host's restart configuration, leave an effect pending, restart, remove the fixture module, and approve.

**Acceptance Scenarios**:

1. **Given** a module that the registry does not list, **When** the user asks about it or opens its route, **Then** Weaver answers from the registry that it is not installed, and the route is the only context it discloses.
2. **Given** a disabled module, or an enabled module without Weaver contributions, **When** the user asks for help on its page, **Then** Weaver says so and offers no tool. The Weather sample is such a module.
3. **Given** a deselected module, **When** a page that would nest its widget is open, **Then** nothing of that module is offered, and other modules' support is unchanged.
4. **Given** unconfigured privilege classes, **When** any classed effect is proposed, **Then** it is refused before review.
5. **Given** all three classes enabled, **When** the same effects are proposed, **Then** each is reviewed, and its review shows its classes.
6. **Given** an effect that needs two classes, **When** only one of them is enabled, **Then** it is refused.
7. **Given** the host's bootstrap request, which carries no session, **When** it is answered, **Then** it reports the state of every class, and Studio shows those states read-only.
8. **Given** an effect with a class that is off at the step being taken, **When** that step is approval, execution or recovery, **Then** the effect is refused at that step. At execution, nothing is dispatched.
9. **Given** an approved effect whose owning module reports that it persisted nothing, **When** the outcome is recorded, **Then** it is *performed*, with its no-persisted-effect scope, and the review shell shows it.
10. **Given** a tool with a declared owner-code set, **When** it emits a code outside that set, **Then** the outcome is reported with a generic code, and a canary in the code appears nowhere.
11. **Given** a pending effect whose module is removed, before or after a restart, **When** it is approved or recovered, **Then** it resolves as unavailable with zero effect ([spec 1189](../1189-weaver-recovery/spec.md)).
12. **Given** an approval pending in a durable session from before leaf 0's upgrade, **When** it is approved after the upgrade, **Then** it is stale and refused, and is never executed (FR-018).

---

### User Story 2 - Inspect, describe and test Secrets without exposing a value (leaf 1) (Priority: P1)

A user on the Secrets page (`/security/secrets`, or its alias `/secrets`) asks which secrets exist, why one is expired, or whether one is usable. The user may also ask Weaver to improve a description.

Weaver reads each secret's metadata:
- name, display name and description;
- type, store, scope, tags and status;
- the effective condition, such as expired, and the latest version's expiry, both computed by the owning module (FR-006);
- current version and revision;
- created and updated times.

It never discloses a value. Weaver can propose a metadata update, or the owning module's value test.

Revoke and delete arrive in leaf 2, together with the secret consumer source they require (story 3).

**Why this priority**: This is the smallest row with the highest exclusion risk, and it sets the pattern for the rest.

**Independent Test**: In the normal host with the test overlay, seed:
- an active secret, one whose latest version has expired, and a revoked secret, each holding a canary value;
- a secret whose test message echoes its value;
- a secret in another tenant.

Ask about the list on both routes and about each secret. Improve one description, and test one secret. Approve each, and compare each receipt with the owning module's readback.

Negative cases:
- an actor without `secrets:read`, for reads;
- an actor without `secrets:write`, and one without `secrets:test`;
- a target in another tenant;
- an edit or a rotation in another tab between review and approval;
- a test of a secret that does not exist, or is inactive;
- a repository that cannot save at a revision;
- the Secrets API feature not composed;
- the administrative class off at each step;
- in the durable host, a restart with an approved effect pending;
- requests to create, rename, reveal, rotate, import, export, revoke or delete.

**Acceptance Scenarios**:

1. **Given** the list with a filter and a selection, **When** Weaver reads context, **Then** it receives the filter, the selected identities and their metadata, bounded by `secrets:read` and the tenant. An expired secret shows as expired.
2. **Given** an approved display-name or description update, **When** it is applied, **Then** the owning module saves it at the reviewed revision, and the receipt carries the readback. A repository that cannot save at a revision makes the update unavailable.
3. **Given** an approved test, **When** it runs at the reviewed version, **Then** it is *performed*. The receipt carries only success or failure and the owner code, and the value and the message are redacted.
4. **Given** a test that could not run, because the secret does not exist or is inactive, **When** the outcome is recorded, **Then** it is refused with the owner code.
5. **Given** the Secrets API feature not composed, **When** a tool runs, **Then** the outcome is owner-unavailable (FR-006).
6. **Given** a request to create, rename, reveal, rotate, import or export, **When** Weaver receives it, **Then** it refuses on the grounds in FR-001. **Given** a request to revoke or delete, **Then** Weaver says that leaf 2 delivers it, and offers nothing in leaf 1.

---

### User Story 3 - Service connections, and secret revoke and delete (leaf 2) (Priority: P1)

A user on the Connections page, in its creation form, or at a connection picker in a workflow asks what a connection is or why it fails. The user may also ask Weaver to prepare, update, rebind, test, revoke or delete a connection. On the Secrets page, the user may ask Weaver to revoke or delete a secret.

Through a new owning-module read (FR-006), Weaver reads each connection's:
- name, display name, type, authentication scheme and status;
- settings field names and secret-bound field names;
- managing party and Owner field;
- tags, last test time and version.

Bound secret names and named-credential names are disclosed only under `secrets:read`. Otherwise only the field names are disclosed. Settings values are never disclosed.

Weaver can propose:
- a prefill of the visible creation form;
- an update of the display name and tags;
- a rebind of a secret field;
- the owning module's test, which may contact an external service, depending on the type's provider;
- a connection revoke or delete;
- a secret revoke or delete.

Revoke and delete require the irrecoverable class to be enabled. A secret revoke or delete also requires a complete consumer answer, which comes from the Connections consumer source added in this leaf.

**Why this priority**: "Why does my integration fail" is the most common Connections question. Irrecoverable secret effects are safe only once every consumer is known.

**Independent Test**: Use the normal host with the test overlay. The overlay provides:
- a test connection type that records each test invocation, carries canary token fields, and returns a test message echoing a resolved value;
- a secret repository whose save can be made to fail.

Seed:
- a connection with a canary settings value, a canary secret value and canary token values;
- a connection whose field uses an inline secret the connection owns;
- an active, an expired and an inactive text secret in the tenant;
- another connection's owned secret;
- a connection in another tenant.

Explain each connection. Then prefill the form, update a connection, rebind a field, test, revoke and delete. From the Secrets page, revoke and delete secrets that connections use. Approve each effect, and compare the receipts with the recorded invocation and the owning modules' readback.

Negative cases:
- an edit or a user-run test between proposal and approval (either one advances the version);
- for reads, an actor with neither `workflows/design:read` nor `connections:manage`;
- an actor without `secrets:read`:
  - for bound names, and for owned-secret names in a delete review;
  - for a rebind, which is refused before review, exactly as for an unknown secret;
- an actor without `connections:manage` (for updates, rebinds, revokes and deletes), one without `connections:bind-secret`, one without `connections:test`, and one without `secrets:delete`;
- the Connections API feature not composed, which leaves the consumer answer incomplete;
- a target in another tenant;
- a rebind to an expired, inactive or non-text secret, or to another connection's owned secret;
- a rebind away from the inline secret whose cleanup then fails after commit;
- tests:
  - a test that cannot run;
  - a test that runs and fails;
  - a test whose write fails after the external call;
  - a test whose message or owner code echoes a resolved value;
  - a dropped test response;
- a connection delete that removes owned secrets and then fails to remove the connection;
- a secret delete whose value is removed but whose metadata save fails;
- a consumer query that is truncated;
- a secret's revoke or delete review, for an actor who cannot read connections;
- a revoke or delete of a connection-owned inline secret from the Secrets page;
- the creation form closed before the prefill completes;
- the administrative or the irrecoverable class off at each step;
- in the durable host, a restart with an approved effect pending;
- requests to create a connection, authorize OAuth, change the redirect URI, or set a settings value.

**Acceptance Scenarios**:

1. **Given** a connection, **When** Weaver reads it, **Then** it receives the metadata and version. It receives bound and named-credential names only under `secrets:read`.
2. **Given** the creation form, **When** Weaver reads it, **Then** it receives the type, scheme, field names and validation errors, and no entered settings or secret value.
3. **Given** an approved prefill, **When** it is applied, **Then** only the form's name, type and scheme change, applied locally. Creating the connection stays the user's action.
4. **Given** an approved update or rebind, **When** it is applied, **Then** it applies only at the reviewed version, under `connections:manage`. A rebind also needs `connections:bind-secret`, `secrets:read`, and an active text secret in the tenant that no other connection owns. Candidates shown to the client grant nothing.
5. **Given** a rebind away from an inline secret the connection owns, **When** it is reviewed, **Then** it needs both the administrative and the irrecoverable class. The review states that the secret is deleted after commit unless another connection still uses it, and that the deletion cannot be undone. If cleanup fails after commit, the effect is persisted with a warning.
6. **Given** an approved test, **When** it runs, **Then** the reviewed version is checked before the external call. When the type's test runs, the owning module records the test time and advances the version, and other pending proposals for that connection become stale. The receipt is persisted with whether the test ran, whether it succeeded, and the owner code. The message is redacted.
7. **Given** a test that cannot run, **When** the outcome is recorded, **Then** it is refused with the owner code. **Given** a test whose write fails after the external call, **Then** it is *uncertain*.
8. **Given** a connection revoke or delete, **When** it is reviewed, **Then** the review states that it cannot be undone, and that workflows using the connection will fail. For a delete, it also lists the owned secrets that will be deleted: by name under `secrets:read`, otherwise as a count. A secret that another connection still uses is kept and is not listed. Only other connections are checked.
9. **Given** a connection revoke or delete, **When** it is prepared and run, **Then** it is refused before review unless the irrecoverable class is enabled. It applies only at the reviewed version, which is checked before any side effect. A delete's receipt is an absence readback: the row is gone. A delete that removes owned secrets and then fails to remove the connection is *uncertain*.
10. **Given** a secret revoke, **When** it is reviewed, **Then** the review states that the revoked version's value cannot be restored, and that rotating reactivates the secret with a new value. **Given** a secret delete, **Then** the review states that it cannot be undone.
11. **Given** a secret revoke or delete, **When** it is prepared, **Then** it needs `secrets:delete` and the irrecoverable class. The review lists the secret's consuming connections as reported by the Connections consumer source: by name when the actor may read connections, otherwise as a count.
12. **Given** a secret revoke or delete whose consumer answer is incomplete, **When** it is prepared, **Then** it is refused as owner-unavailable. The answer is incomplete when Connections is composed without its API feature, or when the consumer query is truncated. Weaver never reports "no consumers known" in that case.
13. **Given** a secret revoke or delete with a complete answer, **When** it runs, **Then** the reviewed revision is checked before any side effect. A delete's receipt is an absence readback: the secret is hidden. A delete whose value is removed but whose metadata save fails is *uncertain*, and is never replayed.
14. **Given** an inline secret a connection owns, **When** a revoke or delete is proposed from the Secrets page, **Then** Weaver refuses it. This is stricter than the native page, because the Secrets module has no notion of connection ownership. The refusal relies on the owning connection that the consumer source reports, since the operation would break that connection irrecoverably. The user can still act on the native page, or remove the binding from the connection first.
15. **Given** the Connections API feature not composed, **When** a Connections tool runs, **Then** the outcome is owner-unavailable (FR-006).

### Edge Cases

- **A module unloads during a turn.** Its context is re-admitted without it (FR-010 covers host attribution and nested pickers).

## Requirements

### Functional Requirements

#### Delivery matrix

- **FR-001**: Leaf 0 MUST refresh the normal host's live installed and composed module inventory, and record the result on #3443. M0's loaded-module list is historical (#3443, 30 October).
  - **M0 rows:** each one records M0's inventory, which the refresh confirms. A row that the refresh finds absent or changed is updated by leaf 0's PR, with ground A or with its new outcome.
  - **New screens:** a screen the refresh finds that the matrix lacks is added as a row by leaf 0's PR, and assigned under FR-025.
  - **Outcomes and grounds:** every row MUST end at its outcome and evidence. A narrowing of its target, and every existing owner operation that Weaver does not offer, MUST state its grounds.

  Outcome words:
  - **Read:** read and explain.
  - **Reviewed:** a reviewed effect.
  - **Local:** a reviewed edit of visible, unsaved state.
  - **Delivered by #3442:** M3 delivered it.
  - **None in M4:** not supported in M4.

  Grounds:
  - **A:** absent from, or disabled in, the refreshed inventory.
  - **B:** the Program boundary: secret values, package promotion and package activation, and module and feed administration. Feature selection apply falls outside B where the integrator enables the activation class (owner decision 2).
  - **C:** a credential, file or value would pass through Weaver.
  - **D:** the owning module does not hold the setting durably or cluster-wide.
  - **H:** a human's decision.
  - **N:** no owner operation, or no server owner, exists.
  - **O:** another issue, spec or open decision owns it.
  - **S:** the screen does not show it.
  - **T:** outside the row's target. For an M0 row, that is the M0 target. For a post-M0 row, it is the target stated here, which is a Program-authority choice open to owner review.
  - **V:** the owning module's write is reachable only from the browser, so the server cannot verify its result.
  - **W:** needs a module-owned working-state adapter.
  - **X:** needs its own exclusion design before any disclosure.

  This spec specifies the Weather, ExtensionBuilder, Secrets and Service connections rows. Every other row states its outcome here, and its leaf spec states its effects, contracts and scenarios.

  | Row | Owning module | Target | Outcome; narrowings (grounds) |
  | --- | --- | --- | --- |
  | Weather | Samples (Studio-only) | Qualify when installed (M0) | None in M4: A when absent; N when installed, because the Studio-only sample has no server owner to admit its context |
  | ExtensionBuilder | ExtensionBuilder | Optional inspection and guidance (M0) | None in M4: A while disabled; X once enabled, because its state includes workspace source |
  | Secrets | Secrets | Reference, metadata and availability inspection; domain effects (M0) | Read; Reviewed metadata update and value test (story 2); Reviewed revoke and delete (story 3). Value-bearing create and rotate (B); configuration-key create and rotate (T); rename, import and export (N); the workflow property's Secrets editor (O: #3711) |
  | Service connections | Connections | Metadata, bindings and diagnostics; changes and tests (M0) | Read; Local prefill; Reviewed update, rebind, test, revoke and delete (story 3). Settings values (X: no field declares its sensitivity); create and OAuth authorize (C); redirect URI (N: a read-only public URL) |
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
  | ETL definitions, connectors, runs | ETL | Drafts, filters, validation and authoring; run and partition; explicit cancellation (M0) | Read; Reviewed draft update and run cancellation (its leaf spec). Create (W); delete and start (N); publish (H); complete (T) |
  | Activity definitions | Activities Design; Workflows.Publishing (test runs, publish); Elsa3 (import) | Inspection; draft, import and upgrade proposals (M0) | Read; Reviewed draft add, update and discard, and upgrade plans (its leaf spec). Import (C); publish (H); other definition and version operations, and test runs (T); contract proposals (W) |
  | User tasks, forms | `Elsa.Studio.UserTasks` (client), Workflows Runtime (server) | Read; claim, release and reassign (post-M0) | Read; Reviewed claim, release and reassign (its leaf spec). Decisions (H); prefill values and decision responses (C); form authoring (W) |
  | Agents, reviews | Agent Specialists | Read (post-M0) | Read (its leaf spec). Review decisions (H); the decider (C); authoring (O: #3519) |
  | BPMN | Activities BPMN | Read (post-M0) | Read (its leaf spec). Run and work operations, artifact registration, interchange management (T); direct publish (H); process authoring (W) |
  | Expressions | Expression Modules | Language-owned guidance and exact expression edits (M0) | Read; Reviewed expression edits (its leaf spec, coordinated with #3711) |
  | New workflow | Workflows, Weaver.Workflows | Caller attachment; typed suggestions or reviewed creation (M0) | Delivered by #3442 |
  | Workflow rows: definitions, draft, templates, library and tags, versions, Executables, Runs | Workflows | M3 rows | Delivered by #3442. Spec 1193 refuses run controls (retry, cancel, delete, re-dispatch), library deletes, and edits to published or historical versions and Executables (O: spec 1193) |

#### Packaging and ownership

- **FR-002**: **Server packages.** This spec decides, under Program authority, that there is one Agent package per owning module. #3428 allows optional integrations to be "separately packaged with their owning module"; the guards below rule out placing them inside Essentials owners.
  - **Naming:** each package is named `Elsa.Agent.<Domain>`, at the path the architecture guard derives.
  - **Why the Agent bucket:** the ExtensionBoundary guard forbids every core project from referencing an extension. Framework §2.2's secondary-domain sub-rule covers only features with no models of their own, and these packages define their own Context Kind, tool and result models.
  - **References:** a package references its owning module's `.Core` where one exists, and Agent packages in its own bucket. It registers the server-side descriptor of every client tool its module offers.
  - **Modules without a `.Core`:** the package admits only client-asserted kinds. It exists only where SC-004's per-package deselection needs it.
  - **Cross-bucket edges:** where the owning module sits in another extension bucket, such as ETL or ExtensionBuilder, the reference is declared in the ExtensionBoundary guard's `DeclaredExtensionEdges`, with a justification comment.
  - **Leaf 1:** adds `Elsa.Agent.Secrets`, which reads only Secrets `.Core`. It also changes the probe host:
    - it packages the Secrets API;
    - it composes Secrets, the Secrets API and `Elsa.Agent.Secrets` in the baseline;
    - it adjusts the restart helper so that it no longer refuses a baseline that already selects Secrets;
    - its test overlay provides the test secret store.
  - **Leaf 2:** adds `Elsa.Agent.Connections`, which serves the Connections module. It is distinct from the existing `Elsa.Agent.Specialists.Connections`. Leaf 2 also adds Connections, the Connections API and `Elsa.Agent.Connections` to the probe host. Its test overlay provides the test connection type and the secret-repository save fault.

  `Elsa.Agent.Workflows.Runtime` exists, from #3442 slice F (#3732), and later leaves extend it.
- **FR-003**: **Studio packages.** A Studio contribution MUST live in its owning Studio module, through the public SDK.
  - **Satellites:** a satellite package, in the shipped form `Elsa.Studio.Weaver.<Domain>` under `Extensions/Weaver/studio/`, is added only where a host must keep the module but drop its Weaver support. As in FR-002, framework §2.2's sub-rule does not apply, because a satellite defines its own renderers and Weaver-facing models.
  - **Built-in Studio Screens** are such a case, because every host keeps the shell. They are owned by `Elsa.Studio.Hosting`, and their contributions go in `Elsa.Studio.Weaver.Hosting`. Leaf 4, the first leaf that needs that package, creates it together with its SDK read seams.
- **FR-004**: **Result Renderers and module guidance.** Each module MUST ship, in its own packages, the Result Renderers for its tool result kinds and its own bounded, versioned module guidance. Module guidance is trusted installed-module text, kept distinct from resource content. Shared Weaver code holds neither.

#### Generic changes

- **FR-005**: Weaver core, the shell's collector and review shell, and the provider adapters MUST gain no row's domain knowledge. These are the generic changes:
  - the privilege policy (FR-015), including the server reporting each class's state on the host's bootstrap request;
  - the review shell showing an effect's classes and the *performed* outcome;
  - *performed*, its no-persisted-effect receipt scope, and the matching executor and integrity validation (FR-016);
  - declared owner-code sets, and their validation (FR-016);
  - a principal seam that hands a tool the caller's authenticated principal at preparation, approval, execution, and the explicit authenticated execute that runs a recovered approval. Today, a tool's authorization receives only the actor and the tenant;
  - the registry-based answer of story 1;
  - the probe fixture's classed, *performed* and owner-code effects;
  - doc comments on the risk enum, matching FR-013;
  - SDK read seams for `Elsa.Studio.Weaver.Hosting`, delivered by leaf 4 (FR-003);
  - new outcome codes, only where no existing code fits.

  Leaf 0 delivers all of these except the SDK read seams. Leaf 0 also adds a dated one-line pointer, "Amended by spec 1203", to spec 1186's session-policy contract and to spec 1189's recovery contract.

#### Owner prerequisites

- **FR-006**: Where a row needs a read, an authorization or an operation that today exists only behind the owning module's HTTP endpoints, or outside its `.Core`, the owning module MUST supply it in a form Weaver can call under the same principal:
  - a `.Core` contract, implemented in the owning module's `.Api` package, which already references the identity contracts;
  - taking the caller's authenticated principal from the principal seam (FR-005);
  - checking that the actor matches that principal, and deciding permissions through `IPermissionAuthorizationService`.

  The contract MUST also meet these rules:
  - **Absent dependencies:** if the `.Api` feature or the authorization service is not composed, the contract is absent and the outcome is owner-unavailable. A permission gate that defaults to allowing everything is never relied on.
  - **Exceptions:** infrastructure exceptions are wrapped (framework §2.23.5).
  - **Writes:** a write to an existing resource takes an expected revision and checks it before any side effect. A composition that cannot honor the revision reports the write as unsupported, so it never runs unconditionally. A create, where no resource exists, uses the owning module's create-if-absent.
  - **Native pages:** the shipped native pages keep working, with a regression test.

  The type names below are contract decisions; the plan owns their signatures.

  | Owning module | Prerequisite |
  | --- | --- |
  | Secrets (leaf 1) | A metadata read with the revision, the effective condition and the latest version's expiry. A revisioned metadata update. A value test at an expected version that reports whether it ran. |
  | Secrets (leaf 2) | Revoke and delete at the revision, each unavailable without a revision-aware repository. A fan-in in Secrets `.Core` (framework §2.6.1): the return-style contributor `ISecretConsumerSource`, the named Sequential event `SecretConsumersCollecting` carrying the caller's principal, and its single aggregating handler, `CollectSecretConsumers`. Secrets `.Core` gains a reference to `Elsa.Events.Core` for the event. A module whose core feature declares that it consumes secrets, but whose consumer source is not composed, makes the answer *incomplete*. |
  | Connections (leaf 2) | A read that adds the version and the bindings, omits settings values, and shows bound and named-credential names only under `secrets:read`. An update (display name and tags) and a rebind, each at an expected version. The rebind checks `secrets:read`, refuses as for an unknown secret, and refuses another connection's owned secret. A test at an expected version, checked before the external call, whose result adds a "ran" flag and an owner code. Revoke at an expected version. A delete that checks the version before deleting any owned secret, then removes the row with the repository's version check. A bounded repository query that lists the connections that bind or own a given secret, with an explicit truncation flag; truncation makes the consumer answer *incomplete*. The Connections core feature declares that it consumes secrets. An `ISecretConsumerSource` implementation in `Elsa.Connections.Api`, which decides names or count under the Connections read rule (`workflows/design:read` or `connections:manage`). |

- **FR-007**: **Read authority.** A rule that is exactly one permission MAY be checked through the tool's declared permission, decided by `IPermissionAuthorizationService` for the caller's principal. Any other rule MUST be decided by the owning module's contract, never reimplemented in a Weaver package. Names from another module in a review follow that module's read rule, and appear as a count otherwise (stories 3.8 and 3.11).
- **FR-008**: Deployment shapes MUST keep working (constitution §E2.2.3). Design-side contributions register only where Design is composed, and Runtime-side ones only where Runtime is. An absent side is explicitly unavailable.

#### Context and disclosure

- **FR-009**: **Common controls.** Every Context Kind and tool MUST have a negative case for each control, tested at the cheapest level that proves it (ADR 0080 D1, framework §2.23):
  - **Module removed:** nothing of the module is offered.
  - **Denied, or a cross-tenant target:** nothing is disclosed, not even existence, and an effect is refused before review.
  - **Owner-unavailable:** nothing from that owning module is disclosed.
  - **Stale target:** zero effect, and navigation never retargets.
  - **Excluded data:** seeded canaries appear nowhere.
  - **Untrusted content:** resource content and every client-asserted kind are marked untrusted, distinct from module guidance.

  Content the user explicitly attaches, such as selected console lines, is the user's own disclosure. It is client-asserted and untrusted, and it is excluded from the canary search.
- **FR-010**: Each Context Kind MUST be module-owned, versioned, bounded and authorized. It names the host it describes. It is attributed to an exact identity:
  - the owning module's revision, where one exists;
  - for a list, the visible query and selection;
  - for unsaved state, the client binding and the base revision.

  Server context providers admit only their own kinds, and produce their permission and catalog kinds from their own reads. A tool never takes data from another package's kind. A nested picker contributes its own kind from its own module; for example, the Connections picker in a workflow property form is a Connections contribution. A form's own fields belong to the form's module. A nested picker's context never changes the target of a pending Operation Batch.
- **FR-011**: Each Context Kind MUST declare its bounds. Each leaf adds tests proving that:
  - its densest pages, nested pickers included, stay within the attachment count at the largest set each shows by default;
  - each kind stays within the per-attachment bounds.

  A session that reaches its disclosure budget is refused, as that budget defines.
- **FR-012**: Each disclosure allowlist MUST be authored by the module that defines the data's meaning, and ship in that module's Agent package. It excludes unlisted fields by default. It is proven by a canary in an unlisted field that the owning module's own redaction does not already handle.

#### Effects

- **FR-013**: Every effect MUST declare its risk, its set of privilege classes, its stale check and its receipt. Each leaf spec states these explicitly.
  - **Risks follow the risk enum's order:** ReadOnly < ReviewRequired < Destructive < Admin.
    - *Destructive*: the effect removes or replaces existing state, as an Operation Batch removal does.
    - *Admin*: the effect touches sensitive or administrative state.
  - **Risk is not the gate:** [ADR 0003](../../docs/adr/0003-weaver-direct-apply-risk-classification-is-elsa-owned.md) keeps risk classification Elsa-owned. Gating comes from the classes, not the label.

  For leaves 1 and 2:

  | Effect | Leaf | Risk | Privilege classes | Stale check | Receipt |
  | --- | --- | --- | --- | --- | --- |
  | Secret metadata update | 1 | `Admin` | administrative | revision | readback; persisted |
  | Secret value test | 1 | `Admin` | administrative | version | *performed*, with success or failure and the owner code; refused when it could not run |
  | Secret revoke | 2 | `Destructive` | irrecoverable | revision; complete consumer answer | readback; persisted |
  | Secret delete | 2 | `Destructive` | irrecoverable | revision; complete consumer answer | absence readback; persisted; *uncertain* when partial |
  | Connection prefill | 2 | `ReviewRequired` | none | client binding | applied locally |
  | Connection update, or rebind of a field whose secret the connection does not own | 2 | `Admin` | administrative | version | readback; persisted |
  | Rebind of a field whose inline secret the connection owns | 2 | `Admin` | administrative and irrecoverable | version | readback; persisted, with a warning if cleanup failed |
  | Connection test | 2 | `Admin` | administrative | version, checked before the external call | persisted, with whether the test ran, its result and the owner code; refused when it could not run; *uncertain* when the write failed after the external call |
  | Connection revoke | 2 | `Destructive` | irrecoverable | version | readback; persisted |
  | Connection delete | 2 | `Destructive` | irrecoverable | version | absence readback; persisted; *uncertain* when partial |

- **FR-014**: **Common effect outcomes.** Every effect MUST be tested for each of these, at the cheapest level that proves it:
  - **Review:** the effect is reviewed in every autonomy mode, including `full-auto`.
  - **Refusal:** the policy is read live at each step. An effect is refused at preparation, approval, execution or recovery whenever one of its classes is off at that step. A refusal is not sticky, and at execution nothing is dispatched. An effect without the owning module's permission is refused before review.
  - **Owner unavailable at approval:** owner-unavailable, with zero effect.
  - **Module removed at approval:** unavailable, with zero effect.
  - **Restart:** in the durable host, a pending approval survives a restart. A recovered approval runs only on an explicit authenticated execute, which rechecks the revision, the permission and the classes.
  - **Lost or uncertain response:** an effect whose response is lost after dispatch, or which may have run before a restart, is *uncertain*. It is never replayed, and the session owner resolves it, as spec 1189's recovery contract defines.
  - **Local edits:** these carry correlated acknowledgements, and are unavailable with zero effect if their page is gone.
- **FR-015**: **Privilege policy** (owner decisions 1, 2 and 4). The Agent module MUST gain an integrator-configured privilege policy. It is a backend host option, never a session setting. Its three classes are enabled independently:
  - **administrative:** `Admin` effects. An `Admin` effect belongs to this class unless it declares otherwise.
  - **irrecoverable:** effects whose consequence cannot be undone. A module MUST declare this class for its own irrecoverable effects, whatever their risk label; leaves 1 and 2 do. A third-party irrecoverable effect that declares no class and is not `Admin` is not caught. This limitation is recorded.
  - **activation:** feature selection apply on the Server host.

  The policy MUST also behave as follows:
  - **Class sets:** each effect declares a set of classes, and needs every class in it. For example, the owned-inline rebind needs administrative and irrecoverable.
  - **Declaration:** the owning module declares the set at preparation, because core cannot infer an effect's consequence. The set is frozen into the approval digest.
  - **Defaults:** every class is off by default, so a host that leaves the policy unconfigured refuses every classed effect. The Workbench, as the reference integrator, enables all three classes in its base configuration, so in every environment.
  - **Enforcement:** the policy is read live at each step (FR-014).
  - **Display:** Studio shows the policy read-only, as part of its Host Policy, from the host's bootstrap response.
- **FR-016**: **Outcomes.**
  - ***Performed*:** leaf 0 MUST add *performed* (owner decision 3): the approved action ran, and the owning module persisted no change. It is a new proposal status, and it reports the action as executed.
    - Its receipt uses a new effect scope, `NoPersistedEffect`, and carries only what the owning module reports.
    - The executor's and the invocation integrity's validation accept that scope for *performed*, and only for *performed*.
    - Every server and Studio status mapping handles *performed*; the plan names the sites.
  - **Owner codes:** a tool's descriptor MAY declare a closed set of owner codes. Every M4 tool declares one.
    - For a tool that declares a set, Agent core validates each code in receipts, refusals and preparation errors. A code outside the set is reported as a generic code, which makes the canary rule decidable.
    - An `Elsa.Agent.<Domain>` package maps its owning module's native codes into its set. This also resolves native collisions, such as the two "not found" codes of the Secrets manager and its configuration store.
    - Codes mapped from an owning module are dotted ASCII identifiers outside `agent.`. A receipt carries its code in a new member, `OwnerCode`.
    - Tools that declare no set pass their codes through unchanged, and existing tools' codes are unchanged.
  - **Existing outcomes:** every other outcome reuses the existing outcomes and codes. *Uncertain* keeps its existing shape and generic message, and the session owner resolves it (spec 1189). A new code is added only where none fits, such as the privilege-policy refusal. A status alone never stands as evidence of an effect.

#### Contracts, catalogs and records

- **FR-017**: Replacement contracts MUST detect a second registration (framework §2.6.2). The Agent-wide gap for existing contracts remains #3633. Visibility follows framework §2.23.3 and ADR 0080 D3.
- **FR-018**: **SemVer.** Every change MUST be classified under framework §4.2. Pre-GA Majors follow ADR 0080 D4 and Elsa constitution §E5, as spec 1193 FR-033 did.

  Major:
  - *performed*, because it changes model semantics through:
    - the new proposal-status value and its status string;
    - the new `NoPersistedEffect` member of the effect-scope enum;
    - the executor and integrity validation changes.
  - the privilege policy, for two reasons:
    - with every class off by default, it refuses `Admin` tools that ran before, including third-party ones;
    - freezing the class set into the approval digest makes approvals pending in durable sessions across the upgrade stale. They are refused, and never executed (story 1.12).

  Minor:
  - declared owner-code sets, which are opt-in, so tools without a set are unaffected;
  - the `OwnerCode` receipt member;
  - new members on existing stores and contracts, added so that existing implementations keep compiling (the plan names the mechanism);
  - Secrets `.Core`'s new reference to `Elsa.Events.Core`;
  - new contracts, events, model members, packages, Context Kinds, tools, renderers, codes and permissions.

  Each leaf spec classifies its own changes under these rules.
- **FR-019**: **Catalogs** MUST change in the same unit of work as the code (framework §2.22.1):
  - **Leaf 0:** `Extensions/Agent/src/Elsa.Agent/EXTENSION_POINTS.md` gains the privilege policy, class sets, owner-code sets, the principal seam, *performed* and its effect scope.
  - **Leaf 1:** `Essentials/src/Secrets/Core/EXTENSION_POINTS.md`, `Essentials/src/Secrets/Elsa.Secrets/EXTENSION_POINTS.md` and `Essentials/src/Secrets/Persistence/EntityFrameworkCore/EXTENSION_POINTS.md`.
  - **Leaf 2:**
    - the Secrets Core catalog gains the `SecretConsumersCollecting` event, the `ISecretConsumerSource` contributor and the `CollectSecretConsumers` handler;
    - `Essentials/src/Connections/Api/EXTENSION_POINTS.md` gains the `ISecretConsumerSource` implementation, as a cross-domain Known implementation;
    - `Essentials/src/Connections/Core/EXTENSION_POINTS.md` and `Essentials/src/Connections/Elsa.Connections/EXTENSION_POINTS.md`.
  - **Leaf 4:** `Studio/src/essentials/Elsa.Studio.Api/EXTENSION_POINTS.md` gains the SDK read seams.
  - **Every leaf:**
    - its contributors go in the Agent catalog, as cross-domain Known implementations;
    - each new package gets its own catalog where it exposes extension points;
    - the root index is updated (framework §2.22.2);
    - each package's README lists its cross-domain contributions.
- **FR-020**: **Records.** Evidence goes on each PR, on #3443 and on #3428, which owns the matrix and the evidence ([program goal](../../docs/program-goals/weaver-across-studio.md)).

  Leaf 0 MUST file the following follow-ups as `needs-triage` (AGENTS.md; [repository hygiene](../../docs/contributing/repository-hygiene.md#issues)):
  - the ExtensionBuilder exclusion design (X);
  - field-level sensitivity for connection settings values (X);
  - a server-verifiable path for browser-only writes (V);
  - a durable, cluster-wide Workflow Runtime Diagnostics Setting (D);
  - console record reads (X);
  - a log time range and retention view (S);
  - working-state adapters for ETL creation, task forms, BPMN processes and activity contract proposals (W);
  - coverage in the irrecoverable class for third-party effects that declare no class (FR-015).

  How they are handled:
  - Follow-ups that are this Program's work are triaged under the 29 October delegation.
  - Every follow-up is recorded on #3428.
  - A follow-up in a parked program's area is filed without a parent.
  - Nothing that already exists is filed. Context-budget progress goes on #3710, and Full payloads are recorded as Studio ADR 0009's open decision.

  Leaf 0 also records on #3428:
  - every narrowing;
  - the deferrals to other programs: #3604, and #3519's authoring;
  - the decisions open to owner review.
- **FR-021**: **Glossary.** Leaf 0 MUST make these glossary changes.
  - **In the [Elsa glossary](../../docs/glossary/elsa.md)**, beside its Weaver and agent terms, each linked from Studio/CONTEXT.md:
    - *Privilege Class* (FR-015);
    - *Performed*, next to a pointer to spec 1186's outcome definitions (FR-016);
    - *Owner code* (FR-016);
    - *Secret consumer*.
  - **In Studio/CONTEXT.md:**
    - *Module Load Status*: replace its state list, "discovered, accepted, activated, disabled, or rejected", with the states the host reports. Those are available, loaded, disabled, incompatible and failed, plus the registry's pending and unknown. Its _Avoid_ line, "Diagnostics signal, observability widget", stays.
    - *Privileged Tool*: widen it to a Weaver Tool whose effect needs a privilege class.
    - *Built-in Studio Screen*: add it, as a screen that the `Elsa.Studio.Hosting` client renders itself.
    - *Client-Asserted Kind*: add it, as a Context Kind whose content only the browser supplies. It is untrusted, it confers no authority, and the server checks only its schema and bounds.

#### Evidence

- **FR-022**: Each story MUST have end-to-end evidence. Fixture cases run in the probe host, and the restart cases of stories 2 and 3 run in that leaf's durable host. Every contributed tool and effect MUST have a conformance-probe case on both provider adapters: Copilot's delegated loop and Anthropic's host-driven loop. Negative cases are tested at the cheapest level that proves them.
- **FR-023**: The canary search MUST cover HTTP bodies, SSE frames, adapter payloads, tool results, owner codes, review cards and audit records.
- **FR-024**: Evidence MUST keep three categories distinct:
  - deterministic component tests;
  - deterministic provider-protocol native evidence on the actual Copilot and Anthropic adapters, with packaged consumers;
  - opt-in live-provider evidence (none claimed).

  Probe evidence is frozen at the exact reviewed head of its PR. Failures, skips and quarantines are recorded as they occurred.

#### Later leaves

- **FR-025**: Each later leaf MUST get its own spec, numbered at the time with the next free number. It is written and reviewed when the previous leaf merges, as #3428 elaborates later leaves only once their slice is concrete. A leaf spec is bound by FR-002 to FR-024 and by its FR-001 rows, and specifies those rows' effects, owner prerequisites, risks, classes and scenarios.

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
  | 10 | ETL | draft update, run cancellation |
  | 11 | Activity definitions | reads, drafts, upgrade plans |
  | 12 | User tasks, agents, BPMN | reads; task claim, release and reassign. That leaf's spec escalates reassign if it stays `Admin` |
  | 13 | Expressions | language-owned guidance and exact expression edits, coordinated with #3711 |

### Key Entities

- **Coverage row:** an inventory screen with its owning module, target, outcome, narrowings and evidence (FR-001).
- **Disclosure allowlist:** the fields whose values may be disclosed, authored by the owning module (FR-012).
- **Effect:** a reviewed operation with its risk, privilege classes, stale check, receipt and common outcomes (FR-013, FR-014).
- **Narrowing:** a recorded gap between a target and the delivered outcome, with its grounds (FR-001).

## Success Criteria

### Measurable Outcomes

- **SC-001**: Every FR-001 row has linked evidence on #3428, either from this spec's leaves or from its leaf spec. Every narrowing has its grounds, and no row is unassigned.
- **SC-002**: Stories 1–3 pass their acceptance scenarios in the hosts FR-022 names. A host that leaves the privilege classes unconfigured refuses every classed effect.
- **SC-003**: Seeded canaries appear in none of the channels FR-023 lists. Content the user explicitly attaches is excluded from the canary search (FR-009).
- **SC-004**: For each contributing package, a composition test that deselects it removes exactly that package's contributions, and leaves every other row's contributions unchanged.
- **SC-005**: Each leaf PR's review evidence includes a diff check that lists the changed projects against FR-005's list. It shows that Weaver core, the shell's collector and review shell, the provider adapters and other modules changed only through those generic changes.
- **SC-006**: After leaf 0's generic changes, the independent package witness is re-run and passes. The [program goal](../../docs/program-goals/weaver-across-studio.md) requires later lifecycle changes to have their own qualification.
- **SC-007**: The native-page regression tests of FR-006, and the attachment tests of FR-011, pass.
- **SC-008**: Each leaf PR passes the CI and review gates, as spec 1193 SC-010 did:
  - the [merging](../../docs/contributing/merging.md) PR evidence;
  - the Elsa constitution §E7 test-tier manifest for each new test project;
  - locked-mode restore;
  - the solution filters.

## Assumptions and Boundaries

- **Delivery:** each leaf is its own independently reviewed PR. Leaf PRs say "Refs #3443", and the closing leaf says "Fixes #3443", following spec 1193's Refs-then-Fixes precedent.
  - **Order:** leaves 1 and 2 depend on leaf 0, and leaf 2 builds on leaf 1.
  - **Status:** the PR that merges this spec sets *Approved*. Leaf 0's PR sets *In progress*. The closing leaf's PR sets *Implemented* ([spec lifecycle](../../docs/reference/spec-lifecycle.md)).
  - **Bookkeeping:** every leaf comments on #3428 and #3443 at each state transition, and reconciles #3443 and its Project item (AGENTS.md program bookkeeping).
- **Program owner decisions, 2026-11-03** (Sipke Schoorstra; [receipt on #3428](https://github.com/elsa-workflows/elsa-foundation/issues/3428#issuecomment-6612627295)):
  1. **Irrecoverable deletes and revokes** of secrets and Service connections: "Let the integrator decide what privileges weaver has via configuration." These use the irrecoverable class (FR-015).
  2. **Feature selection apply (activation):** "Let the integrator decide via configuration." It uses the activation class, on the Server host. This widens #3443's activation boundary ("a manage claim alone cannot expand it", 31 October), but only for hosts that enable the class.
  3. **A generic outcome for an approved action that persists nothing:** "Add it in leaf 0." This is *performed* (FR-016).
  4. **The privilege default:** "Off; Workbench always on." Every class is off by default, and the Workbench enables all three in its base configuration.
- **Decisions taken under Program authority, open to owner review** (as spec 1193 records its own):
  - **Packaging:** one Agent package per owning module (FR-002).
  - **Class sets:** an effect's privilege classes are declared by consequence, as a set that must be satisfied in full, and frozen into the approval digest.
  - **Secret consumers:** reported through `ISecretConsumerSource`. An incomplete answer refuses an irrecoverable secret effect. Weaver refuses to revoke or delete a connection-owned inline secret, which is stricter than the native page.
  - **Owner codes:** declared closed sets, mapped by each Agent package.
  - **ExtensionBuilder:** narrowed from M0's "optional inspection and guidance" to None in M4, with ground X once enabled.
  - **Console:** server reads of console records are narrowed (X). Only lines the user attaches are disclosed.
  - **Expressions:** the Expressions leaf delivers the M0 target, coordinated with #3711.
  - **Post-M0 targets:** user tasks get read, claim, release and reassign, with reassign flagged for escalation. Agents, BPMN and backends are read-only.
  - **Runtime capture:** its semantics stay untouched, and Full payloads wait for Studio ADR 0009.
  - **Browser-only writes:** these are narrowings (V). They cover the Theme Store, backend profiles, and the Studio host's feature apply.
  - **Personal layout:** changes are delivered. The default layout stays administration (T).
  - **Run cancellation:** ETL cancellation is reviewed, because its M0 target names explicit cancellation. BPMN run operations are outside BPMN's post-M0 target (T), consistent with spec 1193's refusal of workflow run controls.
- **Plan decisions:**
  - **P1:** each package's shell feature.
  - **P2:** the signatures of the FR-006 contracts and the principal seam, the revision mechanisms, and where native permission constants live.
  - **P3:** every non-atomic compare-then-write window, and how it is bounded.
- **Overlap**, reconciled without reparenting:
  - ETL: #2894 and #2906, with the open ETL defects #3709, #3719, #3721, #3724 and #3562.
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

  Integrated qualification remains #3444.

## Issue Traceability

| #3433 criterion | Requirements and stories |
| --- | --- |
| Home/dashboard, ETL, Connections, Secrets metadata, Diagnostics, runtime evidence, activity availability and installed settings/administration surfaces have module-owned support | FR-001 to FR-004, FR-025; stories 2–3 |
| Each supported action has context, tool, renderer/effect and negative-case evidence appropriate to its domain; absent mutation support is explicit | FR-001, FR-004, FR-013, FR-014, FR-022 |
| Secret values and unrelated data are not exposed; runtime/model semantics and existing domain ownership remain intact | FR-006, FR-007, FR-009, FR-012; Assumptions (runtime capture); FR-002 |
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
