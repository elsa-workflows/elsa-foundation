# Feature Specification: Elsa essentials images

**Feature Branch**: `agent/claude/3052-spec-essentials-images`

**Created**: 2026-11-24

**Status**: Draft

**Input**: Two published images, an Elsa 4 engine and a designer-only Studio, that carry exactly the features the
final JTest acceptance set needs. The composition is baked in. Users deliver one `appsettings.json`, their feature
packages and a folder of converted Elsa 4 workflows. One feature list feeds both the engine image and the reference
closure of the Elsa 3 migration CLI. The JTest acceptance set passes against the engine image.

## Problem

An Elsa 3 user who has finished a test migration holds three things: their Elsa 4 feature packages, a settings file and
a folder of converted Elsa 4 workflow definitions. To go live they need an Elsa 4 server to start with those three
things. Today the only published server image is `elsaworkflows/elsa-workbench`, which its own Dockerfile calls a
"development and demo host, not the shipped product". It compiles in about a hundred features, BPMN, Copilot, agents
and decisions among them, and its composition lives in a `shells.json` that users must know to configure it.

**Example.** To let Studio sign in against the Workbench, `docker/compose/docker-compose.images.yml` sets
`CShells__Shells__default__Features__FoundationIdentityAspNetCoreIdentity__AllowedReturnUrlOrigins__2`. The index `2`
is there only to keep the two origins the Workbench's `shells.json` already lists. A user cannot write that line
without reading the Workbench composition.

The final JTest acceptance (spec 1235) proves the conversion on the Workbench. Nothing proves it on the server a user
would actually run.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Start the engine with my own files (Priority: P1)

As an Elsa 3 user going live on Elsa 4, I start the essentials engine image with my `appsettings.json`, my feature
packages and my converted workflows mounted, and my workflows are published and run.

**Why this priority**: This is the last step of the migration journey; without it the converted workflows have
nowhere to run.

**Independent Test**: Start the engine with the documented example `appsettings.json`, one Elsa 4 workflow definition
with an HTTP trigger in the workflows folder and no packages; call the trigger and see the workflow complete.

**Acceptance Scenarios**:

1. **Given** the engine image and an `appsettings.json` with every required secret, **When** the container starts
   with a workflows folder mounted, **Then** each definition in the folder is imported and published, and a workflow
   with an HTTP trigger answers on its path.
2. **Given** an `appsettings.json` that adds a feature from a package in the packages folder, **When** the container
   starts, **Then** that feature is active next to the baked features and its activities are available to the
   imported workflows.
3. **Given** an `appsettings.json` that changes a setting of a baked feature, **When** the container starts, **Then**
   the changed value applies and every other baked setting keeps its default.
4. **Given** an `appsettings.json` that adds a feature whose package is not in the packages folder, **When** the
   container starts, **Then** it refuses to start and names the missing feature.
5. **Given** an `appsettings.json` without a required secret, **When** the container starts, **Then** it refuses to
   start and names the missing setting.
6. **Given** no workflows folder is mounted, **When** the container starts, **Then** it starts with no workflows and
   reports no error.

### User Story 2 - The JTest acceptance set passes against the engine image (Priority: P1)

As the coordinator, I run the S-15 JTest acceptance set against the essentials engine image and see the same result
the Workbench run gives on the same initiative head.

**Why this priority**: The JTest corpus is what keeps the migration correct. It must prove the server users run, not
only the Workbench.

**Independent Test**: Build the engine image at the head, convert the acceptance definitions with the migration CLI's
`inventory` command, mount the `converted/` folder, run `jtest run` with the S-15 Elsa 4 templates and an environment
that differs only in base URL, and compare the result per suite with the S-15 Workbench run on the same head.

**Acceptance Scenarios**:

1. **Given** the engine image and the `converted/` folder for the acceptance definitions, **When** the container
   starts, **Then** every converted definition is published.
2. **Given** a suite that passes on the Workbench on the same head, **When** it runs against the engine image,
   **Then** it passes, with the suite file and the Elsa 4 templates unchanged.
3. **Given** a suite that passes on the Workbench but fails on the engine image, **When** the cause is found,
   **Then** the missing feature or setting is added to the essentials composition with its reason, and the suite is
   run again.

### User Story 3 - Design workflows in the essentials Studio (Priority: P2)

As a workflow author, I start the essentials Studio image against the engine, sign in, and open, edit and publish a
workflow, without menus for modules the engine does not have.

**Why this priority**: Users need to look at and adjust their converted workflows, but the engine and its acceptance
do not depend on Studio.

**Independent Test**: Start both images with the essentials compose file, sign in to Studio, open a converted
definition in the designer, change it and publish it.

**Acceptance Scenarios**:

1. **Given** both images started with the essentials compose file, **When** the author signs in to Studio, **Then**
   the workflow definitions list shows the definitions the engine imported.
2. **Given** a definition open in the designer, **When** the author edits an expression and publishes, **Then** the
   engine runs the new version.
3. **Given** the essentials Studio, **When** the author looks at the navigation, **Then** it offers only workflow
   design and what the designer needs, and no entries for modules the engine does not compose.

**Studio UX acceptance record** ([Studio UX acceptance](../../docs/contributing/studio-ux-acceptance.md)):

- **User goal**: starting from the essentials compose file, an author signs in, finds a converted definition, changes
  an expression and publishes it, and the engine runs the new version.
- **Reference**: the existing Workflow Definitions list and designer in the full Studio; the essentials Studio reuses
  them unchanged.
- **Observable states**: sign-in (wrong password, success); definitions list empty and populated; designer open;
  expression edit with a validation error and its recovery; publish success; navigation without non-designer
  modules. Desktop (1440) and narrow (390) widths, light and dark.
- **Exclusions**: instance views, user management and settings screens are the workflows module's own and are not
  qualified again here; this record covers what the essentials composition changes (what is present and absent).

### User Story 4 - One feature list for the image and the migration CLI (Priority: P2)

As a maintainer, I change the essentials composition in one place, and both the engine image and the migration CLI's
reference closure follow it.

**Why this priority**: The migration CLI promises that a clean assessment means the workflows run on the image. That
holds only while both use the same composition.

**Independent Test**: Add a feature to the list and see the engine host compose it; remove a feature from the engine
host's references and see the guard test name it.

**Acceptance Scenarios**:

1. **Given** the feature list, **When** the engine host starts, **Then** it composes exactly the listed features with
   the listed settings, before the user's `appsettings.json` applies.
2. **Given** a feature in the list that the engine host cannot resolve, **When** the guard test runs, **Then** it fails
   and names the feature.
3. **Given** the list, **When** a separate tool reads it, **Then** it can do so without referencing the engine host.

### Edge Cases

- A user's `appsettings.json` lists a baked feature with different settings: the user's values win key by key; the
  feature stays composed once.
- A user's `appsettings.json` sets a baked feature to anything that would switch it off: this specification does not
  define, document or test that case (see Q5).
- A converted definition fails to import or publish at startup: the engine logs the definition and the reason, keeps
  running and serves the other definitions, as JSON workflow reconciliation does today.
- The workflows folder holds subfolders: only the top level is read, as JSON workflow reconciliation does today; the
  migration CLI writes a flat folder (FR-010).
- A user package needs a newer Elsa share than the image carries: Nuplane refuses it, as it does for the Workbench
  image.
- The data volume is not mounted: the engine runs, and its SQLite data is lost when the container is removed, as with
  the Workbench image.

## Requirements *(mandatory)*

### Functional Requirements

**Composition**

- **FR-001**: The engine composition MUST be one feature list, defined once in this repository. Each entry MUST
  carry its reason: a JTest suite or template of the S-15 acceptance set needs it, the essentials Studio designer needs
  it, it is JSON workflow reconciliation, or one of those features requires it to run (identity, persistence,
  locking, hosting).
- **FR-002**: No feature MUST be listed without such a reason. BPMN, Copilot and agents, decisions, connections,
  dashboards, the extension builder and the other Workbench extras MUST NOT be in the list.
- **FR-003**: The engine host MUST reference only the projects the listed features live in, plus what hosting and
  package loading need. It MUST carry no Elsa 3 assembly.
- **FR-004**: The composition MUST be baked into the image and not read from a file the user provides or sees. The
  image's documentation MUST NOT ask users to edit a shells file.
- **FR-005**: Users MUST configure the engine through one `appsettings.json` mounted at a fixed path. It is applied
  over the baked composition and the baked defaults, and environment variables are applied over it. It MAY change any
  setting of a baked feature and MAY add features. Switching a baked feature off is neither supported nor refused by
  this specification.
- **FR-006**: The order in which the baked composition, the baked defaults, the user's `appsettings.json` and the
  environment are merged MUST be implemented once, in a form the migration CLI (its own specification, defined in parallel) can call
  without the engine host.
- **FR-007**: A feature the user adds MUST come from a package in the mounted packages folder, loaded by Nuplane as the
  Workbench image loads packages today. When an added feature cannot be found, the engine MUST refuse to start and name
  it.
- **FR-008**: Secrets (token signing key, recovery and hierarchy cursor signing keys, secrets encryption key, the
  seeded administrator password, the module management key) MUST have no baked value. When one is missing, the engine
  MUST refuse to start and name the setting. A documented example `appsettings.json` MUST list every required setting.

**Workflows**

- **FR-009**: The engine MUST compose JSON workflow reconciliation, reading a fixed workflows folder that exists in
  the image and is empty by default, and MUST publish what it imports by default. A user MAY turn publishing off in
  `appsettings.json`.
- **FR-010**: The workflows folder MUST be read as a flat folder of files in the JSON workflow reconciliation format.
  This is the format and layout the migration CLI writes to `converted/`.
- **FR-011**: The engine MUST store its data in SQLite under a data folder meant for a volume by default. The other
  storage providers the persistence layer already supports MUST remain selectable through settings.

**Studio**

- **FR-012**: The essentials Studio MUST compose only sign-in, the backend connection, theming and the workflow
  designer with its expression editors (workflow definitions, designer, publishing and the workflow instance views
  that come with the workflows module). Dashboards, agents, decisions, secrets, connections, BPMN, Copilot, feature
  management and samples MUST NOT be composed.
- **FR-013**: The Studio composition MUST be baked the same way as the engine's (FR-004), and users MUST configure the
  Studio image through one `appsettings.json` and environment variables.
- **FR-014**: The essentials Studio MUST pass the [Studio UX acceptance](../../docs/contributing/studio-ux-acceptance.md)
  for its journey: sign in, open a definition from the list, edit, publish, at desktop and narrow widths in both
  themes.

**Images and delivery**

- **FR-015**: The images MUST be published as `elsaworkflows/elsa-server-essentials` and
  `elsaworkflows/elsa-studio-essentials` by the existing image workflows, with the same tags and version stamping the
  Workbench and Studio images get.
- **FR-016**: A compose file MUST start both images with mounts for `appsettings.json`, the packages folder, the
  workflows folder and the data volume, and MUST need no other configuration than the example `appsettings.json`.
- **FR-017**: The Workbench, its image and the S-15 acceptance on the Workbench MUST NOT change.

**One list**

- **FR-018**: The feature list MUST be machine-readable and readable by another tool without referencing the engine
  host. A guard test MUST fail, naming the feature, when the engine host cannot resolve a listed feature or composes
  one that is not listed.

**Acceptance**

- **FR-019**: The acceptance run MUST start a fresh engine container from an image built at the initiative head, with
  the workflows folder holding the `converted/` output of the migration CLI's `inventory` command for the S-15
  acceptance definitions (approved adjusted copies in place of their originals), and run `jtest run` with the S-15
  Elsa 4 templates and an environment that differs from the Workbench one only in base URL and credentials.
- **FR-020**: Every suite that passes on the S-15 Workbench run on the same head MUST pass on the engine image. A suite
  the S-15 rules exempt (JOIN, value capture, a 3.6.3 defect, an approved incompatibility) keeps its exemption and
  status here. A suite that passes on the Workbench and fails on the engine image is a defect of this specification.
- **FR-021**: Each acceptance run MUST be recorded with the commit, image digest, CLI version, `jtest` version and
  per-suite result next to the S-15 runs, without secret values. A failed run MUST stay recorded.
- **FR-022**: Until the migration CLI exists, the engine image MUST be checked with one hand-written Elsa 4 workflow
  definition in the workflows folder (User Story 1, scenario 1). This check does not replace FR-019.
- **FR-023**: The JTest run MUST NOT become part of a CI tier, as in spec 1235.

### Key Entities

- **Essentials feature list**: the features of the engine composition, each with its settings and its reason.
- **User settings file**: the one `appsettings.json` a user mounts; overrides and additions only.
- **Workflows folder**: the flat folder of Elsa 4 definitions the engine imports and publishes at startup.
- **Acceptance run record**: what ran, on which image and head, with which per-suite result, compared with the
  Workbench run.

## Spec decisions

| ID | Question | Decision |
|---|---|---|
| Q1 | New hosts or Workbench profiles? | New hosts (Joey, 2026-11-24). A Workbench profile would still ship every Workbench feature in the binary, and the CLI could not reuse it as its closure. |
| Q2 | How do the Elsa 3 definitions reach an engine that carries no Elsa 3 code? | Through the migration CLI's `converted/` folder, mounted as the workflows folder: the user's own go-live path. Joey gave no preference; this was the recommendation. The final acceptance run therefore waits for the CLI's `inventory` command (FR-019); FR-022 checks the image before that. |
| Q3 | Image names? | `elsaworkflows/elsa-server-essentials` and `elsaworkflows/elsa-studio-essentials`, chosen with Q1. |
| Q4 | Does the engine publish what it imports? | Yes by default: going live means the mounted workflows run. Users can turn it off. |
| Q5 | Can configuration switch a baked feature off? | Deliberately left alone, as agreed in the migration design. |
| Q6 | Which data store? | SQLite in a data folder by default, as the S-15 run uses; the providers the persistence layer already supports stay selectable through settings. No new provider work. |

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On one initiative head, every suite of the S-15 acceptance set that passes on the Workbench also passes on
  the essentials engine image; zero suites pass on one and fail on the other.
- **SC-002**: A user starts both images and runs a workflow from the workflows folder by writing only one
  `appsettings.json` from the documented example, with zero edits to a composition file.
- **SC-003**: Every feature in the essentials list has a recorded reason, and none of the Workbench extras FR-002 names
  is in it.
- **SC-004**: The engine image contains zero Elsa 3 assemblies.
- **SC-005**: Drift between the list and the engine host fails a test that names the feature.
- **SC-006**: The essentials Studio journey passes the Studio UX acceptance, and its navigation shows no module the
  engine does not compose.

## Dependencies and boundaries

- [Final JTest acceptance, spec 1235](../1235-jtest-final-acceptance/spec.md) supplies the acceptance set, its Elsa 4
  templates, its exemptions and the Workbench run this spec compares with. It stays on the Workbench, unchanged.
- The migration CLI (`dotnet elsa elsa3-migration workflows inventory|assess`, specified in parallel) consumes the
  feature list (FR-018) and the settings merge (FR-006), and writes the `converted/` folder FR-010 describes. The final
  acceptance run (FR-019) needs its `inventory` command.
- [Elsa constitution](../../.specify/memory/constitution.md) E2.7 keeps Elsa 3 behind the import boundary; the engine
  image carries no Elsa 3 code.
- JOIN behaviour (#4193) and value capture (#5647) keep the suites they block blocked, as in spec 1235.

## Assumptions

- Docker is available locally for the acceptance run, as for spec 1235.
- The JSON workflow reconciliation format is the format the migration CLI writes; if the CLI spec chooses another,
  that spec or this one changes before either is implemented.
- Sign-in for JTest uses the same identity features and template (`elsa-authenticate`) as the Workbench run.
