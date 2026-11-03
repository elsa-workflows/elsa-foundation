# Feature Specification: Module-owned Weaver support for the current Studio inventory

**Feature Branch**: `agent/claude/3443-weaver-module-support`

**Created**: 2026-11-03

**Status**: Draft

**Input**: [Feature #3443](https://github.com/elsa-workflows/elsa-foundation/issues/3443) is M4 ([#3433](https://github.com/elsa-workflows/elsa-foundation/issues/3433)) of [Program #3428](https://github.com/elsa-workflows/elsa-foundation/issues/3428), and it is [in progress](https://github.com/elsa-workflows/elsa-foundation/issues/3443#issuecomment-6612633571).

The Program owner's standing authorization covers autonomous delivery of #3443's six acceptance criteria, within the issue's boundaries, as the owner decisions under Assumptions amend them. The current inventory is the finite matrix. There is no blanket administration permission, no arbitrary workspace editing, and no package promotion or activation. Consequential decisions outside that scope go to the Program owner (#3428, 29 October continuation).

The scope is:
- the M0 inventory and its targets in [docs/reports/weaver-baseline.md](../../docs/reports/weaver-baseline.md), which include Activity definitions;
- the issue's 30 and 31 October planning comments;
- the rows the [claim](https://github.com/elsa-workflows/elsa-foundation/issues/3443#issuecomment-6605270638) adds: `/user-tasks` and `/agents`;
- the screens merged since: `/settings/backends` (PR #3600) and the BPMN pages (PR #3603).

Prerequisite #3440 is Done. All leaf code, including leaf 0's, waits for #3442's last slice ([spec 1193](../1193-weaver-workflow-journeys/spec.md)), because the Program runs one implementation leaf at a time.

**What this spec covers**:
1. **The M4 delivery matrix (FR-001):** every row, with its target, outcome and grounds.
2. **The cross-cutting requirements** that bind every leaf.
3. **Leaves 0–2, fully specified:** leaf 0 is story 1 (the generic changes), leaf 1 is story 2 (Secrets), and leaf 2 is story 3 (Service connections).

Each later leaf gets its own spec (FR-025). This spec's contract decisions live in `contracts/privilege-policy.md` and `contracts/outcomes.md`, which the plan writes.

**Review**: Eleven review rounds on 2026-11-03, each with two independent lenses (correctness and standards). Every finding was verified against `origin/main`. The Program owner's decisions of the same day are recorded under Assumptions.

**Terminology**:
- **Studio terms.** These keep their meanings in [Studio/CONTEXT.md](../../Studio/CONTEXT.md): *Weaver*, *Weaver Tool*, *Context Kind*, *Operation Batch*, *Result Renderer*, *Host Policy*, *Module Load Status*, *Privileged Tool* and *Workflow Runtime Diagnostics Setting*.
- **Service connection** keeps its meaning in the [Elsa glossary](../../docs/glossary/elsa.md).
- **Outcome words.** *Proposed*, *approved*, *persisted*, *refused* and *uncertain* are defined by [spec 1186](../1186-weaver-effects/spec.md). *Applied locally* is defined by #3428 and the [program goal](../../docs/program-goals/weaver-across-studio.md). Leaf 0 adds *performed*: the approved action ran, and the owning module persisted no change to its resource. A conflict is *refused* when nothing changed, and *uncertain* when something may have.
- **New names.** Domain nouns keep their current names, such as Attention, until [ADR 0080 D5](../../docs/adr/0080-elsa-4-simplification-decisions.md#d5--plain-language-renamed-now) renames them in that domain's batch. New contributor-style interfaces use the `I<X>Extension` suffix now (ADR 0080 D5, framework §2.6.1, Elsa §E6 R4).
- **"Owner"** is always qualified:
  - the *owning module* owns a row's resource;
  - the *Program owner* is Sipke Schoorstra;
  - the *session owner* resolves an uncertain effect ([spec 1189](../1189-weaver-recovery/spec.md));
  - a Service connection's *Owner field* is part of its metadata.
- **Owner-unavailable:** the outcome when a tool's or context's owning module cannot decide. It carries the owning module's code and no cause text.
- **Host attachment budget:** the per-collection admission limit that Weaver's context admission enforces ([#3710](https://github.com/elsa-workflows/elsa-foundation/issues/3710)). Overflow denies the whole collection.
- **Normal host:** the Workbench backend (`Elsa.Workbench`) with Studio (`Elsa.Studio.Web`), Design and Runtime combined, in its Development configuration.
- **Durable host:** the Workbench's own durable configuration, with the Agent EF store (`FoundationAgentEntityFrameworkCore`), the Connections EF store (`ConnectionsEntityFrameworkCore`) and the Secrets EF store it already composes. The normal host keeps Weaver sessions and Connections in memory.
- **Probe host:** the conformance probe's packaged consumer, which composes the Agent EF store. It is used for cases that need the probe's effect-bearing fixture module.

FR-021 makes three changes to Studio/CONTEXT.md:
- **Replaced:** *Module Load Status* now lists the states the host reports: available, loaded, disabled, incompatible and failed, plus the registry's pending and unknown. Its _Avoid_ lines are reconciled.
- **Widened:** *Privileged Tool* is a Weaver Tool whose effect belongs to a privilege class.
- **Added:**
  - *Privilege Class*, beside *Privileged Tool* and *Host Policy*: a class of effects that the integrator enables in the Agent module's configuration (FR-015).
  - *Performed* (FR-016).
  - *Built-in Studio Screen*: a screen that the `Elsa.Studio.Hosting` client renders itself, such as module load status, appearance or the settings section resolver.
  - *Client-Asserted Kind*: a Context Kind whose content only the browser supplies. It is untrusted, confers no authority, and the server checks only its schema and bounds.

## User Scenarios & Testing

Each story's Independent Test also covers:
- the **common controls** (FR-009);
- for each of its effects, the **common effect outcomes** (FR-014);
- the **canary search** (FR-023).

Every contributed tool and effect has a conformance-probe case on both provider adapters (FR-022). Each step runs in a single host; hosts are never mixed within a step.

### User Story 1 - Explicit answers, the privilege policy and the *performed* outcome (Priority: P1)

A user asks about a module that is not installed or is disabled, or about a page this feature does not support. Weaver answers from two live sources: the Studio module registry, which module load status shows, and the Weaver contributions each host registers. It never infers support from a route or from M0's historical module list.

The user can also see which privilege classes the integrator has enabled. Each review shows the effect's class. An approved effect that persists nothing shows as *performed*.

**Why this priority**: Without this story, every narrowing would be silent, and no later leaf could use the privilege policy or *performed*.

**Independent Test**:
1. In the normal host, run leaf 0's inventory refresh. Then set up a known state:
   - Deselect the Secrets features on the backend (Secrets, the Secrets API, the Secrets workflows integration and the Secrets EF store) and the Secrets Studio module.
   - Deselect the Weather sample.
   - Disable ExtensionBuilder.
2. Ask Weaver about weather forecasts, the extension builder and secrets.
3. Open `/weather` and `/security/secrets`, and ask again.
4. Enable ExtensionBuilder, open it, and ask again.
5. In the probe host, use the effect-bearing fixture module to propose an administrative effect, an irrecoverable effect, and an effect whose owning module persists nothing. Do this three ways:
   - with the privilege classes unconfigured;
   - with all three classes enabled, as the Workbench configures them;
   - with a class switched off at each step: between review and approval, between approval and execution, and before recovery.
6. In the probe host, leave an effect pending, restart, remove the fixture module, and approve.

**Acceptance Scenarios**:

1. **Given** a module that the registry does not list, or lists as deselected, **When** the user asks about it, **Then** Weaver answers from the registry that it is not installed. On the module's route, it discloses only the route.
2. **Given** a disabled module, or an enabled module without Weaver contributions, **When** the user asks for help on its page, **Then** Weaver says so and offers no tool.
3. **Given** a deselected module, **When** a page that would nest its picker or widget is open, **Then** nothing of that module is offered, and other modules' support is unchanged.
4. **Given** unconfigured privilege classes, **When** any classed effect is proposed, **Then** it is refused before review.
5. **Given** all three classes enabled, **When** the same effects are proposed, **Then** each is reviewed, and its review shows its class.
6. **Given** a session, **When** it bootstraps, **Then** the server reports every class's state, and Studio shows the states read-only.
7. **Given** an effect whose class is off at the step being taken, **When** that step is approval, execution or recovery, **Then** the effect is refused at that step. At execution, nothing is dispatched.
8. **Given** an approved effect whose owning module reports that it persisted nothing, **When** the outcome is recorded, **Then** it is *performed*, and the review shell shows it.
9. **Given** a pending effect whose module is removed, before or after a restart, **When** it is approved or recovered, **Then** it resolves as unavailable with zero effect ([spec 1189](../1189-weaver-recovery/spec.md)).

---

### User Story 2 - Inspect, describe, test, revoke and delete Secrets without exposing a value (Priority: P1)

A user on the Secrets page (`/security/secrets`, or its alias `/secrets`) asks which secrets exist, why one is expired, or whether one is usable. The user may also ask Weaver to improve a description, or to revoke or delete a secret.

Weaver reads each secret's metadata:
- name, display name and description;
- type, store and scope;
- tags and status;
- the effective condition, such as expired, and the latest version's expiry, both computed by the owning module (FR-006);
- current version and revision;
- created and updated times.

It never discloses a value. It can propose these effects:
- a metadata update;
- the owning module's value test;
- a revoke or delete, where the integrator enables the irrecoverable class.

**Why this priority**: This is the smallest row with the highest exclusion risk, and it sets the pattern for the others.

**Independent Test**: In the normal host, with a test-only secret store, seed:
- an active secret, one whose latest version has expired, and a revoked secret, each holding a canary value;
- a secret whose test message echoes its value;
- a secret whose delete removes the value but then fails to save the metadata;
- a Service connection that references one of the secrets.

Ask about the list on both routes and about each secret. Then improve one description, test one secret, revoke one and delete one. Approve each, and compare each receipt with the owning module's readback.

Negative cases:
- an actor without `secrets:read`, for reads;
- an actor without `secrets:write`, one without `secrets:test`, and one without `secrets:delete`;
- an edit or a rotation in another tab, between review and approval;
- a test of a secret that does not exist or is inactive;
- a repository that cannot save at a revision;
- the administrative or irrecoverable class off at each step;
- the partial delete;
- in the durable host, a restart with an approved effect pending;
- requests to create, rename, reveal, rotate, import or export a secret.

**Acceptance Scenarios**:

1. **Given** the list with a filter and a selection, **When** Weaver reads context, **Then** it receives the filter, the selected identities and their metadata, bounded by `secrets:read`. An expired secret shows as expired.
2. **Given** an approved display-name or description update, **When** it is applied, **Then** the owning module saves it at the reviewed revision, and the receipt carries the readback. A repository that cannot save at a revision makes the update, the revoke and the delete unavailable.
3. **Given** an approved test, **When** it runs at the reviewed version, **Then** it is *performed*, and the receipt carries only success or failure and the owning module's code. The value and the message are redacted. A test that could not run, because the secret does not exist or is inactive, is refused with the owning module's code.
4. **Given** a revoke, **When** it is reviewed, **Then** the review states that the revoked version's value cannot be restored, that rotation reactivates the secret with a new value, and which Service connections reference the secret.
5. **Given** a delete, **When** it is reviewed, **Then** the review states that the delete cannot be undone, and which Service connections reference the secret.
6. **Given** a revoke or delete, **When** it is prepared, **Then** it needs `secrets:delete`, and it is refused unless the irrecoverable class is enabled. The reviewed revision is checked before any side effect. The receipt for a delete is an absence readback: the secret is hidden.
7. **Given** a partial delete, **When** the outcome is recorded, **Then** it is *uncertain*, with the owning module's code, and it is never replayed.
8. **Given** a request to create, rename, reveal, rotate, import or export, **When** Weaver receives it, **Then** it refuses on the grounds in FR-001.

---

### User Story 3 - Understand, prepare, update, test, revoke and delete a Service connection (Priority: P1)

A user on the Connections page, in its creation form, or at a connection picker in a workflow asks what a connection is or why it fails. The user may also ask Weaver to prepare, update, rebind, test, revoke or delete one.

Through a new owning-module read (FR-006), Weaver reads the connection's:
- name, display name, type, authentication scheme and status;
- settings field names and secret-bound field names;
- managing party and Owner field;
- tags, last test time and version.

Bound secret names are disclosed only under `secrets:read`; otherwise only field names are. Settings values are never disclosed.

Weaver can propose:
- a prefill of the visible creation form;
- an update of the display name and tags;
- a rebind of a secret field;
- the owning module's test, which may contact an external service depending on the type's provider;
- a revoke or delete, where the integrator enables the irrecoverable class.

**Why this priority**: "Why does my integration fail" is the commonest Connections question.

**Independent Test**: In the normal host, use a test-only connection type. Its test records each invocation, it has canary token fields, and its test message echoes a resolved value. Seed:
- a connection with a canary settings value, a canary secret value and canary token values;
- a connection whose field uses an inline secret that the connection owns;
- an active text secret in the tenant, and an inactive one.

Explain each connection. Then prefill the form, update a connection, rebind a field, test, revoke and delete. Approve each, and compare the receipts with the recorded invocation and the connection's readback.

Negative cases:
- an edit or a user-run test between proposal and approval, either of which advances the version;
- an actor with neither `workflows/design:read` nor `connections:manage`, for reads;
- an actor without `secrets:read`, for bound names and for a rebind; the rebind is refused before review exactly as for an unknown secret;
- an actor without `connections:manage`, for updates, rebinds, revokes and deletes;
- an actor without `connections:bind-secret`, and one without `connections:test`;
- a rebind to an inactive or non-text secret;
- a rebind away from the inline secret, whose cleanup then fails after commit;
- a test that cannot run;
- a test that runs and fails;
- a test whose write fails after the external call;
- a test message that echoes a resolved value;
- a dropped test response;
- a delete that removes owned secrets but then fails to remove the connection;
- the creation form closed before the prefill completes;
- the administrative or irrecoverable class off at each step;
- in the durable host, a restart with an approved effect pending;
- requests to create a connection, authorize OAuth, change the redirect URI, or set a settings value.

**Acceptance Scenarios**:

1. **Given** a connection, **When** Weaver reads it, **Then** it receives the metadata and version, and bound secret names only under `secrets:read`.
2. **Given** the creation form, **When** Weaver reads it, **Then** it receives the type, scheme, field names and validation errors, and no entered settings or secret value. The form's secret picker belongs to the Connections context.
3. **Given** an approved prefill, **When** it is applied, **Then** only the form's type, name and display name change, applied locally. Creating the connection stays the user's action.
4. **Given** an approved update or rebind, **When** it is applied, **Then** it applies only at the reviewed version, under `connections:manage`. A rebind also needs `connections:bind-secret`, `secrets:read` and an active text secret in the tenant. Candidates shown to the client grant nothing.
5. **Given** a rebind away from an inline secret that the connection owns, **When** it is proposed, **Then** it belongs to the irrecoverable class by that ownership, whatever cleanup later decides. The review states that the secret is deleted after commit if nothing else references it, and that it cannot be restored. If cleanup fails after commit, the effect is persisted with a warning.
6. **Given** an approved test, **When** it runs, **Then** it runs only at the reviewed version, checked before the external call. When the type's test runs, the owning module records the test time and advances the version. The receipt is persisted with whether the test ran, whether it succeeded, and the owning module's code. The message is redacted. Other pending proposals for that connection become stale.
7. **Given** a test that cannot run, **When** the outcome is recorded, **Then** it is refused with the owning module's code. **Given** a test whose write fails after the external call, **Then** it is *uncertain*, with the owning module's code.
8. **Given** a revoke or delete, **When** it is reviewed, **Then** the review states that the action cannot be undone and that workflows using the connection will fail. For a delete, the review also lists the owned secrets that will be deleted.
9. **Given** a revoke or delete, **When** it is prepared and run, **Then** it is refused before review unless the irrecoverable class is enabled. It applies only at the reviewed version, which is checked before any side effect. A delete's receipt is an absence readback: the row is gone.
10. **Given** a delete that removes owned secrets but then fails to remove the connection, **When** the outcome is recorded, **Then** it is *uncertain*, with the owning module's code.

### Edge Cases

- **A module unloads during a turn:** its context is re-admitted without it.
- **The Studio host and the backend differ:** each context names the host it describes.
- **A nested picker is open while an Operation Batch is pending:** the picker's own context never changes the batch's target.

## Requirements

### Functional Requirements

#### Delivery matrix

- **FR-001**: Leaf 0 MUST refresh the normal host's live installed and composed module inventory, because M0's loaded-module list is historical (#3443, 30 October). It MUST record the result on #3443.
  - **M0 rows:** each one records M0's inventory, which the refresh confirms. If the refresh finds a row absent or changed, leaf 0's PR updates the row with ground A, or with its new outcome.
  - **New screens:** if the refresh finds a screen the matrix lacks, leaf 0's PR adds it as a new row. The row names its owning module, takes ground T, and is recorded on #3428 until a leaf spec takes it.
  - **Weather:** if the refresh finds the Weather sample installed, leaf 0 qualifies it.
  - **Outcomes and grounds:** every row MUST end at its outcome and evidence. A narrowing of a row's target, and every existing owner operation that Weaver does not offer, MUST state its grounds.

  Outcome words:
  - **Read:** read and explain.
  - **Reviewed:** a reviewed effect.
  - **Local:** a reviewed edit of visible, unsaved state.
  - **None:** no support in M4.

  Grounds:
  - **A:** absent from, or disabled in, the refreshed inventory.
  - **B:** the Program boundary: secret values, package promotion and package activation, and module and feed administration. Feature selection apply is outside B where the integrator enables the activation class (owner decision 2).
  - **C:** a credential, file or value would pass through Weaver.
  - **D:** the owning module does not hold the setting durably or cluster-wide.
  - **H:** a human's decision.
  - **N:** no owner operation exists.
  - **O:** another issue or open decision owns it.
  - **S:** the screen does not show it.
  - **T:** outside the row's target. For an M0 row this is the M0 target; for a post-M0 row it is the target stated here.
  - **V:** the owning module's write is reachable only from the browser, so the server cannot verify its result.
  - **W:** needs a module-owned working-state adapter.
  - **X:** needs its own exclusion design before any disclosure.

  This spec specifies the Weather, ExtensionBuilder, Secrets and Service connections rows. Each other row states its outcome here, and its leaf spec states the effects, contracts and scenarios.

  | Row | Owning module | Target | Outcome; narrowings (grounds) |
  | --- | --- | --- | --- |
  | Weather | Samples | Qualify when installed (M0) | None (A), per M0. Leaf 0 qualifies it if the refresh finds it installed |
  | ExtensionBuilder | ExtensionBuilder | Optional inspection and guidance (M0) | None (A while disabled; X once enabled, because its state includes workspace source) |
  | Secrets | Secrets | Reference, metadata and availability inspection; domain effects (M0) | Read; Reviewed metadata update, value test, revoke and delete (story 2). Value-bearing create and rotate (B); configuration-key create and rotate (T); rename, import and export (N); the workflow property's Secrets editor (O: #3711) |
  | Service connections | Connections | Metadata, bindings and diagnostics; changes and tests (M0) | Read; Local prefill; Reviewed update, rebind, test, revoke and delete (story 3). Settings values (X: no field declares its sensitivity, so values cannot be disclosed safely); create and OAuth authorize (C); redirect URI (N: a read-only public URL) |
  | Structured logs, traces | StructuredLogs, OpenTelemetry | Filters, records, correlation; trace, span and query (M0) | Read, specified in its leaf spec. Time range and retention, metrics and OpenTelemetry logs (S) |
  | Diagnostics overview, console, module load status | Studio Hosting; `Elsa.Studio.ConsoleStream` (client) with console log streaming (server) | Visible results; console source and records; module state (M0) | Read, specified in its leaf spec. Server reads of console records (X); lines the user attaches are the user's own client-asserted disclosure (FR-009); module administration (B) |
  | Home | Studio Dashboard, Studio Preferences, Attention, Workflows.Dashboard | Snapshots, filters and freshness; supported preferences (M0) | Read; Reviewed personal layout change and Attention Item snooze, specified in its leaf spec. Default layout (T); new widgets and saved queries (O: #3604) |
  | Workflow Runtime Diagnostics Setting, activity availability | Workflows Runtime, Activities Design | Capture policy; effective availability; supported changes (M0) | Read; Reviewed change within today's capture rules, specified in its leaf spec. Full payloads (O: Studio ADR 0009's open decision); a durable, cluster-wide diagnostics setting (D) |
  | Features | FeatureManagement (client), Modularity (server) | Host, revision, filters and draft; reviewed differences (M0) | Read; Local draft; Reviewed Server-host apply in the activation class, specified in its leaf spec. Studio-host apply (V); configuration values and edits (C) |
  | Appearance, theme builder | Studio Hosting, Hosting.Themes | Preference and theme edits (M0) | Read; Local appearance and builder edits, specified in its leaf spec. Theme Store writes (V) |
  | Modules, feeds | Hosting.Nuplane (client: Studio Hosting) | Host, package, feed and reconcile state (M0) | Read, specified in its leaf spec. Package and feed operations (B); feed credentials are never disclosed (#3735) |
  | Backends | Studio Backends | Read the profiles (post-M0) | Read, specified in its leaf spec. Profile edits (V) |
  | Settings sections | Studio Hosting | The resolved section and host (#3443, 31 October refinement, item 4) | Read, specified in its leaf spec |
  | ETL definitions, connectors, runs | ETL | Drafts, filters, validation and authoring; runs and partitions; explicit cancellation (M0) | Read; Reviewed draft update and run cancellation, specified in its leaf spec. Create (W); delete and start (N); publish (H); complete (T) |
  | Activity definitions | Activities Design; Workflows.Publishing (test runs, publish); Elsa3 (import) | Inspection; draft, import and upgrade proposals (M0) | Read; Reviewed draft add, update and discard, and upgrade plans, specified in its leaf spec. Import (C); publish (H); other definition and version operations, and test runs (T); contract proposals (W) |
  | User tasks, forms | Workflows Runtime | Read; claim, release and reassign (post-M0) | Read; Reviewed claim, release and reassign, specified in its leaf spec. Decisions (H); prefill values and decision responses (C); form authoring (W) |
  | Agents, reviews | Agent Specialists | Read (post-M0) | Read, specified in its leaf spec. Review decisions (H); the decider (C); authoring (O: #3519) |
  | BPMN | Activities BPMN | Read (post-M0) | Read, specified in its leaf spec. Run and work operations, artifact registration, interchange management (T); direct publish (H); process authoring (W) |
  | Expressions | Expression Modules | Language-owned guidance and exact expression edits (M0) | Read and edits, specified in its leaf spec, coordinated with #3711 |
  | New workflow | Workflows, Weaver.Workflows | Caller attachment; typed suggestions or reviewed creation (M0) | Delivered by #3442 (O) |
  | Workflow rows: definitions, draft, templates, library and tags, versions, Executables, Runs | Workflows | M3 rows | None in M4 (O: #3442) |

#### Packaging and ownership

- **FR-002**: **Server packages.** This spec decides, under Program authority, that there is one Agent package per owning module. #3428 allows optional integrations to be "separately packaged with their owning module". The guards below rule out placing them in Essentials owners.
  - **Name and path:** each package is named `Elsa.Agent.<Domain>`, at the path the architecture guard derives.
  - **Why an Agent package:** the ExtensionBoundary guard forbids every core project from referencing an extension. Framework §2.2's secondary-domain sub-rule covers features with no models of their own, and these packages define their own Context Kind, tool and result models.
  - **References:** a package references its owning module's `.Core` where one exists, and the Agent packages in its own bucket. It registers the server-side descriptor of every client tool its module offers.
  - **Owning modules without a `.Core`:** a package for such a module admits only client-asserted kinds. It exists only where SC-004's per-package deselection needs it.
  - **Cross-bucket edges:** where the owning module sits in another extension bucket, such as ETL or ExtensionBuilder, the reference is declared in the ExtensionBoundary guard's `DeclaredExtensionEdges`, with a justification comment.

  Leaves 1 and 2 add `Elsa.Agent.Secrets` and `Elsa.Agent.Connections`. `Elsa.Agent.Connections` contributes Weaver support for the Connections module. It is distinct from the existing `Elsa.Agent.Specialists.Connections`, which serves specialist model connections. #3442 slice F (#3732, merged) added `Elsa.Agent.Workflows.Runtime`, which later leaves extend.
- **FR-003**: **Studio packages.** A Studio contribution MUST live in its owning Studio module, through the public SDK. A satellite in the shipped form `Elsa.Studio.Weaver.<Domain>` is added only where a host must be able to keep the module but drop its Weaver support. The Built-in Studio Screens are such a case, because every host keeps the shell. They are owned by `Elsa.Studio.Hosting`, and their contributions live in `Elsa.Studio.Weaver.Hosting`.
- **FR-004**: **Result Renderers and module guidance.** Each module MUST ship, in its own packages, the Result Renderers for its tool result kinds and its own bounded, versioned module guidance. Module guidance is trusted installed-module text, kept distinct from resource content. Shared Weaver code holds neither.

#### Generic changes (leaf 0)

- **FR-005**: Weaver core, the shell's collector and review shell, and the provider adapters MUST gain no row's domain knowledge. Leaf 0 delivers every generic change:
  - the privilege policy (FR-015), including the server's report of every class's state at bootstrap;
  - the review shell showing each effect's class and the *performed* outcome;
  - *performed* and the owner code on *uncertain* (FR-016);
  - the registry-based answer of story 1;
  - SDK read seams for `Elsa.Studio.Weaver.Hosting`;
  - new outcome codes, only where no existing code fits.

  This spec's `contracts/privilege-policy.md` and `contracts/outcomes.md` record these decisions. Leaf 0 adds a dated one-line pointer, "Amended by spec 1203 (contracts/…)", to spec 1186's session-policy contract and to spec 1189's recovery contract.

#### Owner prerequisites

- **FR-006**: Where a row needs a read, an authorization or an operation that today exists only behind the owning module's HTTP endpoints, or outside its `.Core`, the owning module MUST supply a `.Core` contract, implemented by its feature package, that Weaver can call under the same principal.
  - **Principal:** the contract takes the caller's authenticated principal. The Agent host supplies it from the authenticated request at preparation, approval and execution, and at the explicit authenticated execute that runs a recovered approval. The contract checks that the actor matches the principal, and decides permissions through `IPermissionAuthorizationService`.
  - **Exceptions:** infrastructure exceptions are wrapped (framework §2.23.5).
  - **Writes to an existing resource:** these take an expected revision and check it before any side effect. A composition that cannot honor the revision reports the write as unsupported, so the write never runs unconditionally.
  - **Creates:** where no resource exists yet, the contract uses the owning module's create-if-absent.
  - **Native pages:** the shipped native pages keep working, each with a regression test.

  Plan decision P2 owns the type shape. Leaves 1 and 2 need the following; each later leaf spec lists its own prerequisites under this rule.

  | Owning module | Prerequisite |
  | --- | --- |
  | Secrets | A metadata read carrying the revision, the effective condition and the latest version's expiry. A revisioned metadata update. A value test at an expected version that reports whether it ran. Revoke and delete at the revision. Each write is unavailable without a revision-aware repository. A delete reports a value removed without a metadata save as a partial outcome. The Service connections that reference a secret, for review. |
  | Connections | A read that adds the version and the bindings, omits settings values, and shows bound names only under `secrets:read`. An update (display name and tags) and a rebind, each at an expected version; the rebind checks `secrets:read` and refuses as for an unknown secret. A test at an expected version, checked before the external call, that reports whether the type's test ran separately from its result. Revoke at an expected version. A delete that checks the version before deleting any owned secret, then removes the row with the repository's version check, and reports a partial outcome. |

- **FR-007**: **Read authority.** A rule that is exactly one permission MAY be checked through the tool's declared permission, which `IPermissionAuthorizationService` decides for the caller's principal. Any other rule MUST be decided by the owning module's contract (FR-006), never reimplemented in a Weaver package.
- **FR-008**: Deployment shapes MUST keep working (constitution §E2.2.3). Design-side contributions register only where Design is composed, and Runtime-side contributions only where Runtime is. An absent side is explicitly unavailable.

#### Context and disclosure

- **FR-009**: **Common controls.** Every Context Kind and tool MUST have a negative case for each control below, tested at the cheapest level that proves it (ADR 0080 D1, framework §2.23):
  - **Module removed:** nothing from the module is offered.
  - **Denied:** nothing is disclosed, not even the resource's existence, and a denied effect is refused before review.
  - **Owner-unavailable:** nothing from that owning module is disclosed.
  - **Stale target:** zero effect, and navigation never retargets an effect.
  - **Excluded data:** seeded canaries appear nowhere.
  - **Untrusted content:** resource content and every client-asserted kind are marked untrusted, distinct from module guidance. Resource content includes log and console text, attributes, titles, review content and agent instructions.

  Content the user explicitly attaches, such as selected console lines, is the user's own disclosure. It is client-asserted and untrusted, and the excluded-data control does not apply to it.
- **FR-010**: Each Context Kind MUST be module-owned, versioned, bounded and authorized, and MUST be attributed to an exact identity:
  - the owning module's revision, where one exists;
  - for a list, the visible query and selection;
  - for unsaved state, the client binding and the base revision.

  Server context providers admit only their own kinds, and produce their permission and catalog kinds from their own reads. A tool never takes data from another package's kind. A nested picker contributes its own kind, beside the form's. A picker embedded in a form belongs to the form's module.
- **FR-011**: Each Context Kind MUST declare its bound. The budget applies per collection, so each leaf adds a page-sum test proving that its densest pages fit at the largest set each one shows by default.
- **FR-012**: Each disclosure allowlist MUST be authored by the module that defines the data's meaning, and ship in that module's Agent package. Unlisted fields are excluded by default. The allowlist is proven with a canary in an unlisted field that the owning module's own redaction does not already handle.

#### Effects

- **FR-013**: Every effect MUST declare its risk, privilege class, stale check and receipt. Each leaf spec states them explicitly. For leaves 1 and 2:

  | Effect | Risk | Privilege class | Stale check | Receipt |
  | --- | --- | --- | --- | --- |
  | Secret metadata update | `Admin` | administrative | revision | readback; persisted |
  | Secret value test | `Admin` | administrative | version | *performed* with success or failure and the owning module's code; refused when it could not run |
  | Secret revoke | `Admin` | irrecoverable | revision | readback; persisted |
  | Secret delete | `Admin` | irrecoverable | revision | absence readback; persisted; *uncertain* when partial |
  | Connection prefill | `ReviewRequired` | none | client binding | applied locally |
  | Connection update, or a rebind that leaves no owned secret behind | `Admin` | administrative | version | readback; persisted |
  | Connection rebind away from an owned inline secret | `Admin` | irrecoverable | version | readback; persisted, with a warning if cleanup failed |
  | Connection test | `Admin` | administrative | version, checked before the external call | persisted with whether the test ran, its result and the owning module's code; refused when it could not run; *uncertain* when the write failed after the external call |
  | Connection revoke | `Admin` | irrecoverable | version | readback; persisted |
  | Connection delete | `Admin` | irrecoverable | version | absence readback; persisted; *uncertain* when partial |

- **FR-014**: **Common effect outcomes.** Every effect MUST be tested for each of these outcomes, at the cheapest level that proves it:
  - **Review:** it is reviewed in every autonomy mode, including `full-auto`.
  - **Refused at a step:** it is refused at preparation, approval, execution or recovery whenever its privilege class is off at that step. The policy is read live, so a refusal is not sticky. At execution, nothing is dispatched. It is also refused before review without the owning module's permission.
  - **Owner unavailable at approval:** the outcome is owner-unavailable, with zero effect.
  - **Module removed at approval:** the outcome is unavailable, with zero effect.
  - **Restart:** in the durable host, a pending approval survives a restart. A recovered approval runs only on an explicit, authenticated execute, which rechecks the revision, the permission and the class.
  - **Lost response:** an effect whose response is lost after dispatch, or which may have run before a restart, is *uncertain*. It is never replayed, and the session owner resolves it as spec 1189's recovery contract defines.
  - **Local edits:** they carry correlated acknowledgements, and are unavailable with zero effect if their page is gone.
- **FR-015**: **Privilege policy** (owner decisions 1, 2 and 4). The Agent module MUST gain an integrator-configured privilege policy. It is a host option, part of Studio's Host Policy, and never a session setting. It has three classes, and each is enabled independently. A host may enable the irrecoverable class without the administrative one, and this is intended.
  - **administrative:** an `Admin` effect that declares no other class belongs here by default.
  - **irrecoverable:** an effect whose consequence cannot be undone MUST declare this class, whatever its risk label. A server-persisted `Destructive` effect that declares no class also belongs here by default. Client working-state effects, such as spec 1193's undoable batch removals, are unaffected.
  - **activation:** feature selection apply on the Server host.

  The policy MUST also satisfy these rules:
  - **Declaration:** the owning module declares the class at preparation, because core cannot infer consequence (FR-005). The declared class is frozen into the approval digest.
  - **Defaults:** every class is off by default, so a host that leaves the policy unconfigured refuses every classed effect. The Workbench, as the reference integrator, enables all three in its base configuration, so in every environment.
  - **Enforcement:** the policy is read live at each step (FR-014).
  - **Display:** Studio shows the policy read-only, from the state the server reports at bootstrap.
- **FR-016**: **Outcomes.**
  - **Performed:** leaf 0 MUST add the *performed* outcome (owner decision 3). The approved action ran, and the owning module persisted no change. It is a new proposal status, and its receipt carries only what the owning module reports.
  - **Owner code on uncertain:** leaf 0 also adds, under Program authority, an owning module's code on the existing *uncertain* outcome, so that a module can report an ambiguous answer, such as a partial delete or a write that failed after an external call, with its own code. Today a module cannot return an owner-coded uncertain. An uncertain effect is still never replayed, and the session owner still resolves it (spec 1189).
  - **Existing outcomes:** all other outcomes reuse the existing ones: denied, owner-unavailable, capability-unavailable, executor-unavailable, tool-not-found, context-changed (at admission), refused (at apply time), client-unavailable and budget-exceeded. New codes are added only where none fits, such as the privilege-policy refusal.
  - **Evidence:** a status alone never stands as evidence of an effect.

#### Contracts, catalogs and records

- **FR-017**: Replacement contracts MUST detect a second registration (framework §2.6.2). The Agent-wide gap for existing contracts remains #3633. Visibility follows framework §2.23.3 and ADR 0080 D3.
- **FR-018**: **SemVer.** Every change MUST be classified under framework §4.2. Pre-GA Majors follow ADR 0080 D4 and Elsa constitution §E5, as spec 1193 FR-033 did.
  - **Major:** these change model semantics:
    - *performed* as a new proposal status, appended to the proposal-status enum and to its status string. Studio maps the enum by position, so its mapping is updated in the same change.
    - the *uncertain* lifecycle envelope gaining an owner code, under a new envelope schema version;
    - the privilege policy. With every class off by default, it refuses `Admin` tools, including third-party ones, that ran before, and server-persisted `Destructive` effects that declare no class now default to the irrecoverable class.
  - **Minor:**
    - new members on existing stores and contracts, added so that existing implementations keep compiling (the plan names the mechanism);
    - new contracts, model members, packages, Context Kinds, tools, renderers, codes and permissions.

  Each leaf spec classifies its own changes by these rules.
- **FR-019**: Catalogs MUST change in the same unit of work as the code (framework §2.22.1). For leaves 1 and 2, these are:
  - `Essentials/src/Secrets/Core/EXTENSION_POINTS.md`;
  - `Essentials/src/Connections/Core/EXTENSION_POINTS.md`;
  - `Extensions/Agent/src/Elsa.Agent/EXTENSION_POINTS.md`, where each new contributor is a cross-domain Known implementation;
  - each new package's own catalog, where the package exposes extension points;
  - the root index (framework §2.22.2);
  - each package README, listing its cross-domain contributions.
- **FR-020**: **Records.** Evidence goes on each PR, on #3443 and on #3428, which owns the matrix and evidence ([program goal](../../docs/program-goals/weaver-across-studio.md)). This spec's PR records the owner's boundary amendment in the program goal: activation is in scope only for hosts that enable the activation class.

  Leaf 0 MUST file follow-ups as `needs-triage` (AGENTS.md; [repository hygiene](../../docs/contributing/repository-hygiene.md#issues)). Those that are this Program's work are then triaged under the 29 October delegation, and each is recorded on #3428. The follow-ups are:
  - the ExtensionBuilder exclusion design (X);
  - a server-verifiable path for browser-only writes (V);
  - a durable, cluster-wide Workflow Runtime Diagnostics Setting (D);
  - console record reads (X);
  - a log time range and retention view (S);
  - working-state adapters for ETL creation, task forms, BPMN processes and activity contract proposals (W).

  Nothing that already exists is filed. Progress on the context budget is recorded on #3710. Full payloads are recorded as owned by Studio ADR 0009's open decision. Leaf 0 also records on #3428 every narrowing, the deferrals to other programs (#3604, and #3519's authoring), and the decisions open to owner review.
- **FR-021**: Leaf 0 MUST make the Studio/CONTEXT.md changes listed under Terminology.

#### Evidence

- **FR-022**: Each story MUST have end-to-end evidence. Fixture cases run in the probe host, and restart cases of stories 2 and 3 run in the durable host. Every contributed tool and effect MUST have a conformance-probe case on both provider adapters: Copilot's delegated loop and Anthropic's host-driven loop. Negative cases are tested at the cheapest level that proves them.
- **FR-023**: The canary search MUST cover HTTP bodies, SSE frames, adapter payloads, tool results, review cards and audit records.
- **FR-024**: Evidence MUST keep three categories distinct:
  - deterministic component tests;
  - deterministic provider-protocol native evidence on the actual Copilot and Anthropic adapters, with packaged consumers;
  - opt-in live-provider evidence (none claimed).

  Probe evidence is frozen at the exact reviewed head of its PR. Failures, skips and quarantines are recorded as they occurred.

#### Later leaves

- **FR-025**: Each later leaf MUST get its own spec, numbered at the time with the next free number. It is written and reviewed when the previous leaf merges, because #3428 elaborates later leaves only when their slice is concrete. A leaf spec is bound by this spec's cross-cutting requirements (FR-002 to FR-024) and by its FR-001 rows. It specifies those rows' effects, owner prerequisites, risks, classes and scenarios.

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
  | 12 | User tasks, agents, BPMN | Workflows Runtime, Agent Specialists, Activities BPMN | reads; task claim, release and reassign |
  | 13 | Expressions | Expression Modules | language-owned guidance and exact expression edits, coordinated with #3711 |

### Key Entities

- **Coverage row:** an inventory screen with its owning module, target, outcome, narrowings and evidence (FR-001).
- **Disclosure allowlist:** the fields whose values may be disclosed, authored by the owning module (FR-012).
- **Effect:** a reviewed operation with its risk, privilege class, stale check, receipt and common outcomes (FR-013, FR-014).
- **Narrowing:** a recorded gap between a target and the delivered outcome, with its grounds (FR-001).

## Success Criteria

### Measurable Outcomes

- **SC-001**: Every FR-001 row has linked evidence on #3428, from this spec's leaves or from its leaf spec. Every narrowing has its grounds.
- **SC-002**: Stories 1–3 pass their acceptance scenarios in the hosts FR-022 names. A host that leaves the privilege classes unconfigured refuses every classed effect.
- **SC-003**: Seeded canaries appear in none of the channels FR-023 lists. Content the user explicitly attaches is excluded (FR-009).
- **SC-004**: For each contributing package, a composition test that deselects it removes exactly its contributions, and leaves every other row's unchanged.
- **SC-005**: An architecture or diff check over each leaf's PR finds that Weaver core, the shell's collector and review shell, the provider adapters and other modules changed only through FR-005's generic changes.
- **SC-006**: After leaf 0's generic changes, the independent package witness is re-run and passes. The [program goal](../../docs/program-goals/weaver-across-studio.md) requires later lifecycle changes to have their own qualification.
- **SC-007**: The native-page regression tests of FR-006 and the page-sum tests of FR-011 pass.

## Assumptions and Boundaries

- #3440 and #3441 are Done. #3442's shared seams are reused, except where FR-005 changes them.
- **Delivery.** Each leaf is its own independently reviewed PR that says "Refs #3443", and the last one closes #3443.
  - **Leaves 0–2:** leaf 0 delivers the inventory refresh and the Weather qualification (FR-001), the FR-005 generic changes, the pointers in specs 1186 and 1189, the FR-020 follow-ups and #3428 record, and the FR-021 glossary changes. Leaf 1 delivers Secrets, and leaf 2 Service connections.
  - **Later leaves:** FR-025.
  - **Status:** the PR that merges this spec sets *Approved*. Leaf 0's PR sets *In progress*. The PR of the leaf that closes #3443 sets *Implemented*, because it completes the last FR-001 row ([spec lifecycle](../../docs/reference/spec-lifecycle.md)).
  - **Bookkeeping:** every leaf comments on #3428 and #3443 at each state transition, and reconciles #3443 and its Project item (AGENTS.md program bookkeeping).
- **Program owner decisions, 2026-11-03** (Sipke Schoorstra; [receipt on #3428](https://github.com/elsa-workflows/elsa-foundation/issues/3428#issuecomment-6612627295)). These are owner decisions, not open to review here:
  1. **Irrecoverable deletes and revokes of secrets and Service connections:** "Let the integrator decide what privileges weaver has via configuration." They are delivered in the irrecoverable class (FR-015).
  2. **Feature selection apply (activation):** "Let the integrator decide via configuration." It is delivered on the Server host in the activation class. This widens #3443's activation boundary ("a manage claim alone cannot expand it", 31 October) only for hosts that enable the class. Package promotion and package activation stay ground B, and the Studio host's apply stays ground V.
  3. **A generic outcome for an approved action that persists nothing:** "Add it in leaf 0." This is *performed* (FR-016).
  4. **The privilege default:** "Off; Workbench always on." Every class is off by default. The Workbench enables all three classes in its base configuration, so in Production too.
- **Decisions taken under Program authority, open to owner review**, as spec 1193 records its own:
  - **Packaging:** one Agent package per owning module (FR-002).
  - **Owner code on uncertain:** an owning module's code on the existing *uncertain* outcome (FR-016).
  - **Class membership:** decided by consequence, declared by the owning module, and frozen into the approval digest. An irrecoverable effect cannot opt out. A server-persisted `Destructive` effect defaults to the irrecoverable class. A rebind's class is decided by the ownership of the secret it leaves. Classes are independent.
  - **Naming:** domain nouns keep their current names. New contributor-style interfaces use `I<X>Extension`.
  - **Expressions:** the Expressions leaf delivers the M0 target, coordinated with #3711.
  - **Post-M0 targets:** user tasks get read, claim, release and reassign. Agents, BPMN and backends are read-only.
  - **Capture semantics:** runtime capture semantics stay untouched, and Full payloads wait for Studio ADR 0009.
  - **Browser-only writes:** the Theme Store, backend profiles and the Studio host's feature apply are narrowings (V).
  - **Home layout:** personal layout changes are delivered, and the default layout stays administration (T).
  - **Run controls:** ETL cancellation is reviewed, because its M0 target names explicit cancellation. BPMN run operations are outside BPMN's post-M0 target (T), as spec 1193 refuses workflow run controls.
- **Plan decisions:**
  - **P1:** each package's shell feature.
  - **P2:** the FR-006 contract types, the principal's shape, the revision mechanisms, and where native permission constants live.
  - **P3:** every non-atomic compare-then-write window, and how it is bounded.
- **Overlap**, reconciled without reparenting:
  - ETL: #2894, #2906 and the open ETL defects #3709, #3719, #3721, #3724 and #3562.
  - Home dashboards: #3604.
  - The parked Diagnostics Observability Readiness and Runtime Execution Evidence goals stay parked.
  - User Tasks: #3688, which is complete.
  - Studio Expression Developer Experience: #2310 (#3711, #2349). The Expressions leaf adds no new children under #2310.
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
| Task: review and CI gates; reconcile the issue and Project | The [merging](../../docs/contributing/merging.md) PR evidence; the Elsa constitution §E7 test-tier manifest for each new test project; locked-mode restore; the solution filters (as spec 1193 SC-010); Delivery; FR-020 |
