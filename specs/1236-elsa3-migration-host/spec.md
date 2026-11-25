# Feature Specification: Elsa 3 migration host

**Feature Branch**: `agent/claude/3052-spec-essentials-images`

**Created**: 2026-11-24 (rescoped 2026-11-25)

**Status**: Draft

**Input**: A migration-specific ASP.NET Core host in this repository. It composes, by project references, exactly the
Elsa 4 features that the JTest acceptance set's converted Elsa 3 workflows need, and loads the migration CLI's
`migrated/` folder at startup. Engineers who migrate clone the repository and run it. It is not a published image and
not the GA product host. The JTest acceptance set passes against it.

## Problem

An engineer migrating from Elsa 3 converts their workflows with the migration CLI (spec 1237) and gets a `migrated/`
folder: Elsa 4 workflow definitions under `workflows/` and converted reusables in `activities/activities.json`. They
then need two things: an Elsa 4 server that loads that folder and runs it, and a list of the Elsa 4 features those
workflows need, so they can build their own host.

Today the only server that can do this is the Workbench, a development and demo host that composes about a hundred
features.

**Example.** The S-15 JTest acceptance (spec 1235) proves the conversion on the Workbench. A converted workflow that
passes there runs next to BPMN, Copilot, agents and decisions. So the pass doesn't tell the engineer which features
their own host must compose, and a suite could pass only because some Workbench extra is present.

**Audience.** Engineers with good Elsa knowledge who clone this repository and work with AI on their migration plan
(the migration skill, spec 1238). They build and run hosts from source and don't need a published image.

**Out of scope.** The GA composition story: a thin base, essentials profiles, opt-in triggers and images (#5800). This
host must not become that product.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Run my migrated workflows (Priority: P1)

As an engineer migrating from Elsa 3, I point the migration host at my `migrated/` folder and start it from my clone.
My converted workflows are published and run.

**Why this priority**: Without it, the converted workflows have nowhere to run and the migration can't be checked.

**Independent Test**: Start the host from the repository with its default settings. The project's sample `migrated/`
folder holds one Elsa 4 workflow with an HTTP trigger. Call the trigger and see the workflow complete.

**Acceptance Scenarios**:

1. **Given** the host's settings point at a `migrated/` folder, **When** the host starts, **Then** each definition in
   its workflows folder is imported and published, and a workflow with an HTTP trigger answers on its path.
2. **Given** a `migrated/` folder whose activities file holds a reusable composite and whose workflows folder holds a
   workflow that uses it, **When** the host starts, **Then** both load, the workflow is published, and running it runs
   the composite.
3. **Given** a feature of the engineer's own (custom activities), provided as a project reference in their clone and
   listed in the host's settings, **When** the host starts, **Then** that
   feature is active and its activities are available to the imported workflows.
4. **Given** a settings change in a local, uncommitted settings source (user secrets, environment variables or a local
   settings file), **When** the host starts, **Then** the changed value applies and every other setting keeps its
   default.
5. **Given** the settings name a feature that no project reference provides, **When** the host starts,
   **Then** it refuses to start (see *Refusing to start*) and the log names the missing feature.
6. **Given** the settings point at a workflows folder that doesn't exist, **When** the host starts, **Then** it refuses
   to start and the log names the folder.

### User Story 2 - The JTest acceptance set passes against the migration host (Priority: P1)

As the coordinator, I run the S-15 JTest acceptance set against the migration host and see the same result as the
Workbench run on the same initiative head.

**Why this priority**: The JTest corpus keeps the migration correct. It must prove the conversion on the smallest
composition the converted workflows need, not only on the Workbench.

**Independent Test**: Start the host from the initiative head with the CLI's `inventory` output as its `migrated/`
folder. Run `jtest run` with the S-15 Elsa 4 templates and an environment that differs only in base URL and
credentials. Compare the result per suite with the S-15 Workbench run on the same head.

**Acceptance Scenarios**:

1. **Given** the host and the `migrated/` folder for the acceptance definitions, **When** the host starts, **Then**
   every converted definition is published.
2. **Given** a suite that passes on the Workbench on the same head, **When** it runs against the migration host,
   **Then** it passes, with the suite file and the Elsa 4 templates unchanged.
3. **Given** a suite that passes on the Workbench but fails on the migration host, **When** the cause is found,
   **Then** the missing feature or setting is added to the host's composition with its reason, and the suite runs
   again.

### User Story 3 - See which features my workflows need (Priority: P2)

As an engineer building my own Elsa 4 host, I read the migration host's composition and see each feature with the
reason it is there. That tells me what my host must compose.

**Why this priority**: The engineer goes live on their own host, not on this one. The composition and its reasons are
what they carry over.

**Independent Test**: Open the host's composition and its reasons file (the README shows it). Every feature has a reason: a JTest suite or
template, the designer, the `migrated/` folder, or another listed feature that requires it.

**Acceptance Scenarios**:

1. **Given** the host's composition, **When** an engineer reads it, **Then** each feature names its reason, and none of
   the Workbench extras FR-002 names is in it.
2. **Given** a feature listed in the composition that the host can't resolve, **When** the host's tests run, **Then** a
   test fails and names the feature.

### User Story 4 - Look at converted workflows in Studio (Priority: P3)

As an engineer, I start a Studio host from this repository against the migration host, sign in, and open, edit and
publish a converted workflow.

**Why this priority**: Engineers want to look at and adjust converted workflows, but neither the migration nor its
acceptance depends on Studio.

**Independent Test**: Start the migration host and the repository's Studio host with the documented settings. Sign
in, open a converted definition in the designer, change it and publish it.

**Acceptance Scenarios**:

1. **Given** both hosts started as documented, **When** the engineer signs in to Studio, **Then** the workflow
   definitions list shows the definitions the host imported.
2. **Given** a definition open in the designer, **When** the engineer edits an expression and publishes, **Then** the
   migration host runs the new version.

### Edge Cases

- **A converted definition fails to publish at startup.** The host logs the definition and the reason, keeps running
  and serves the other definitions, as JSON workflow reconciliation does today.
- **A workflows file can't be imported, or the activities file is missing.** For example malformed JSON or a store
  error. The reconcile startup task fails and the host refuses to start, naming the file, as the reconciliation startup
  tasks do today. The CLI writes only definitions that converted and always writes the activities file, so this means a
  hand-edited or foreign file. This specification builds no per-file isolation.
- **A converted workflow uses a reusable with activity dependencies.** The activities file holds it as a composite. It
  loads through the engine fix for source-owned composites with dependencies (FR-010).
- **The workflows folder holds subfolders.** Only the top level is read, as JSON workflow reconciliation does today.
  The CLI writes a flat folder.

## Requirements *(mandatory)*

### Functional Requirements

**Refusing to start** means, everywhere in this specification: shell activation fails, `GET /health/ready` never
returns 200, and the log names the setting, feature or file and the reason. The process doesn't have to exit.

**The host**

- **FR-001**: The migration host MUST be one ASP.NET Core project in this repository, built and run from source. It
  MUST NOT be published as an image or a package, and MUST NOT be presented as a product or deployment host.
- **FR-002**: The host MUST compose only:
  - the features the JTest acceptance set needs;
  - the features the repository's Studio designer needs against it (US4);
  - JSON workflow and activity reconciliation;
  - the features those require to run (identity, persistence, locking, hosting).

  BPMN, Copilot and agents, decisions, connections, dashboards, the extension builder and the other Workbench extras
  MUST NOT be composed.
- **FR-003**: The host MUST reference, by project reference, only the projects the composed features live in, plus
  what hosting needs. It MUST carry no Elsa 3 assembly, so a pass proves the converted workflows run
  on Elsa 4 alone.
- **FR-004**: The composition MUST be one file in the host project. It MUST be readable by path, without referencing
  the host, so other tools in this repository (the migration CLI, the skill) can read it. Each composed feature's
  reason MUST be recorded in a machine-readable reasons file next to it, which the README shows, and a test MUST fail, naming the feature, when a composed feature has
  no reason or a reason names a feature that isn't composed.
- **FR-005**: An engineer MUST be able to change any setting and add features through the standard ASP.NET Core
  configuration sources (a local settings file, user secrets, environment variables, the command line), without
  editing committed files. A feature the engineer adds MUST come from a project reference in their clone; the host loads no
  packages at runtime, because its audience builds from source. When such a feature can't be found, the host MUST
  refuse to start and name it.
- **FR-006**: The host MUST run locally with the identity features in development mode and seeded development
  credentials, as the Workbench does in its committed settings. It MUST hold no production secret. Its README MUST say
  it is a local migration tool and must not be deployed.
- **FR-007**: By default the host MUST store its data in SQLite in a local, git-ignored data folder and migrate its
  schema at startup. This specification does not change which storage providers the persistence layer supports.

**The migrated folder**

The host reads the layout the migration CLI writes: Elsa 4 workflow definitions under `workflows/` (the workflows
folder) and converted reusables in `activities/activities.json` (the activities file).

- **FR-008**: The host MUST compose JSON workflow reconciliation, reading the workflows folder as a flat folder of
  files in that feature's format. It MUST publish what it imports by default; an engineer MAY turn publishing off.
- **FR-009**: The host MUST compose JSON activity reconciliation, reading the activities file, so the reusables the CLI
  converts to composite activities load at startup.
- **FR-010**: Loading a source-owned composite that depends on other activities relies on the engine fix for
  source-owned composites with dependencies (PR #5685, merged into the initiative branch on 2026-11-25). This
  specification does not change it.
- **FR-011**: The host project MUST ship a sample `migrated/` folder with one hand-written Elsa 4 workflow (an HTTP
  trigger that writes a response) and an empty activities file. The default settings MUST point at it, so the host
  starts and US1's independent test runs without the CLI. The two settings that name the workflows folder and the
  activities file MUST be documented.

**Studio**

- **FR-012**: A Studio host from this repository MUST be able to sign in and design against the migration host. That
  takes CORS and the identity return URLs for the Studio host's local address. This specification adds no Studio app
  and changes no Studio code.

**Acceptance**

- **FR-013**: The acceptance run MUST:
  - start the host from the initiative head with a fresh data folder;
  - use the `migrated/` output of the CLI's `inventory` command for the S-15 acceptance definitions, with approved
    adjusted copies in place of their originals;
  - run `jtest run` with the S-15 Elsa 4 templates and an environment that differs from the Workbench one only in base
    URL and credentials.
- **FR-014**: Every suite that passes on the S-15 Workbench run on the same head MUST pass on the migration host.
  - A suite the S-15 rules exempt (JOIN, value capture, a 3.6.3 defect, an approved incompatibility) keeps its
    exemption and status here.
  - When a suite passes on the Workbench and fails on the migration host, the cause decides whose defect it is. A
    composition, setting or host cause is a defect of this specification. A cause in how the CLI exports or writes
    `migrated/` is a defect of the CLI's specification. A converter cause follows the S-15 failure rule.
- **FR-015**: Each acceptance run MUST be recorded next to the S-15 runs with the commit, CLI version, `jtest` version
  and per-suite result, without secret values. A failed run MUST stay recorded.
- **FR-016**: The JTest run MUST NOT become part of a CI tier, as in spec 1235.

**Boundaries**

- **FR-017**: The Workbench (`apps/Elsa.Workbench` on the initiative branch, `apps/Elsa.Workbench.Host` after the
  main sync), its image, its settings and the S-15 acceptance on the Workbench MUST NOT change.

### Key Entities

- **Host composition**: the features the migration host composes, each with its settings and its reason.
- **Migrated folder**: the workflows folder and the activities file the CLI writes and the host loads.
- **Acceptance run record**: what ran, on which head, with which per-suite result, compared with the Workbench run.

## Spec decisions

| ID | Question | Decision |
|---|---|---|
| Q1 | New host or Workbench profile? | A new host (Joey, 2026-11-24). A profile would still carry every Workbench feature in the binary. |
| Q11 | Published images, a base image and a designer-only Studio, or a host built from source? | A migration-specific ASP.NET Core project built from source with project references, with no images and no new Studio app (Joey, 2026-11-25). The audience clones the repository. The GA composition story (base, essentials profiles, images) moved to #5800. This replaces the earlier image decisions: the image names (old Q3, whose number now holds the host's location), Q5 and Q7. |
| Q2 | How do the Elsa 3 definitions reach a host that carries no Elsa 3 code? | Through the migration CLI's `migrated/` folder. Joey gave no preference; this was the recommendation. The final acceptance run therefore waits for the CLI's `inventory` command (FR-013). FR-011's sample checks the host before then. |
| Q3 | Where does the host live, and what is it called? | `Elsa3.Migration.Host` in `extensions/Elsa3/src/Migration/Host/`, next to the rest of the Elsa 3 migration. The name says what it is for. It also keeps the host out of `apps/`, where the product and demo hosts live (`Elsa.Essentials.Host`, the Workbench). It references no Elsa 3 project (FR-003). |
| Q4 | Does the host publish what it imports? | Yes by default, because the migrated workflows must run. Engineers can turn it off. |
| Q6 | Which data store? | SQLite in a local data folder, as the S-15 run uses. No provider work. |
| Q8 | May the host compose what the Studio designer needs, beyond the JTest features? | Yes (US4): the designer's backend (activity catalog, expression and design APIs), each with the reason "Studio designer". |
| Q9 | How do converted reusables reach the host? | As composite activities in the activities file, loaded by JSON activity reconciliation (Joey on PR #5678, spec 1237). |
| Q10 | On the initiative branch, the OpenIddict token store is Workbench host code; the migration host needs one. Where does it come from? | Main has already moved it into `Elsa.Foundation.Identity.OpenIddict.EntityFrameworkCore` (`AddOpenIddictEntityFrameworkCoreStore`, 2026-11-24), in line with Frans's reading of ADR 0073 ("kept on the host side"). The migration host calls that library, so this specification moves nothing. Implementation starts after a main sync brings the library to the initiative branch. |

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On one initiative head, every suite of the S-15 acceptance set that passes on the Workbench also passes on
  the migration host: zero suites pass on the Workbench and fail on the migration host.
- **SC-002**: An engineer starts the host from a fresh clone and runs a workflow from their `migrated/` folder by
  changing only the two folder settings, with zero edits to committed files.
- **SC-003**: Every feature in the host's composition has a recorded reason, and none of the Workbench extras FR-002
  names is in it.
- **SC-004**: The host's dependency closure contains zero Elsa 3 assemblies.
- **SC-005**: A listed feature the host can't resolve fails a test that names it.

## Dependencies and boundaries

- **[Final JTest acceptance, spec 1235](../1235-jtest-final-acceptance/spec.md)** supplies the acceptance set, its Elsa
  4 templates, its exemptions and the Workbench run this specification compares with. It stays on the Workbench.
- **The migration CLI (spec 1237, PR #5678)** writes the `migrated/` folder that FR-008 and FR-009 read, always
  including the activities file. The final acceptance run (FR-013) needs its `inventory` command. Spec 1237 was designed
  around an image built `FROM` the essentials engine image. That image is no longer planned, so how the CLI composes its
  assessment is now spec 1237's decision. FR-004 lets it read this host's composition by path.
- **The engine fix for source-owned composites with activity dependencies** (PR #5685, spec 1239) is merged into the
  initiative branch. US1 scenario 2 and every acceptance suite whose definitions use a reusable workflow rely on it.
- **Main's OpenIddict store library** (Q10) must reach the initiative branch through a main sync before implementation.
- **[Elsa constitution](../../.specify/memory/constitution.md) §E2.7** keeps Elsa 3 behind the import boundary. The
  host references no Elsa 3 project.
- **JOIN behaviour (#4193) and value capture (#5647)** keep the suites they block blocked, as in spec 1235.

## Assumptions

- The engineer has the .NET SDK the repository pins. Running the host needs no Docker.
- The JSON workflow reconciliation format is the format the CLI writes. If the CLI spec chooses another, that spec or
  this one changes before either is implemented.
- Sign-in for JTest uses the same identity features and template (`elsa-authenticate`) as the Workbench run.
