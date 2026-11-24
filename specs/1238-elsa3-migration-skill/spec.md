# Feature Specification: Elsa 3 to Elsa 4 migration skill and documentation

**Feature Branch**: `agent/claude/3052-spec-migration-skill`

**Created**: 2026-11-24

**Status**: Draft — provisional: it reads the migration report that spec 1237 defines, and that schema is not agreed yet.

**Input**: A guide that takes an Elsa 3 user through a test migration of their workflows to Elsa 4, ending in a
complete report of what to patch, what to reconsider, what to take into account and what goes smoothly. The guide is a
provider-neutral skill that any AI assistant can follow and any human can read as documentation. It comes with a
migration plan template, a porting catalog of 19 cases for custom Elsa 3 code, a short section in the main README, and
contributor rules that keep it current.

## Problem

An Elsa 3 user has an export of their workflow definitions and an Elsa 3 codebase with custom activities, JavaScript
functions, variable types and storage drivers. The migration command-line tool (spec 1237) converts the export and
reports every finding. But a report alone does not tell the user what to do with it:

- A refusal such as "unmapped activity type `Acme.SendInvoice`" says that a component is missing. It does not say how
  to rewrite the Elsa 3 `SendInvoice : CodeActivity` with `Input<T>` properties and `context.GetRequiredService<T>()`
  as an Elsa 4 activity, which takes plain input properties, constructor injection and returns a transition.
- The same custom component is often used by many definitions. Porting it once in the right shape fixes all of them.
  Porting it twice, by two people or two AI sessions, in two different shapes, creates drift.
- Some Elsa 3 constructs have no Elsa 4 counterpart: a custom composite that schedules children, a custom Liquid
  filter. A user needs to learn this before porting, not after.
- A migration takes days. Without a written state, every new session starts over.

**Example.** An export of 40 definitions uses one custom activity, `Acme.SendInvoice`, in 12 definitions, a
JavaScript global `lookupCustomer()` in 5, and a variable type `Invoice` in 3. The user asks an AI assistant to help.
The assistant reads the plan (none yet), runs the inventory, finds the three components in the report, asks for the
Elsa 3 source folder, and finds `SendInvoice.cs`, the `ConfigureEngine` call that defines `lookupCustomer`, and the
`AddVariableTypeAndAlias<Invoice>` registration. It fills in the plan: one Elsa 4 feature `Acme.Elsa4` with the three
components, the prefix `acme` for the JavaScript function, and the porting cases that apply to each. It ports each
component by following the catalog case's example, ports the tests, builds the package, runs the assessment with the
package and the user's settings, and repeats until every definition converts or carries a refusal the user accepted.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A guided test migration with an AI assistant (Priority: P1)

As an Elsa 3 user, I can ask any AI assistant to migrate my workflows, and it takes me through inventory, preparation
and test migration, writing down where we are so that a later session resumes there.

**Why this priority**: This is the journey the skill exists for.

**Independent Test**: Give an AI assistant the skill folder, a synthetic Elsa 3 export and its Elsa 3 source. Run the
journey to the end, start a new session halfway, and confirm it resumes from the plan.

**Acceptance Scenarios**:

1. **Given** no migration plan in the workspace, **When** the user starts the skill, **Then** it first states what can
   reach the AI provider (FR-005), then creates the plan from the template, asks for the tag of the essentials engine
   image the user will go live on and records it (FR-004), and runs the inventory command.
2. **Given** an inventory report, **When** the skill reads it, **Then** it lists the custom components the report
   names, with their usage counts, asks for the Elsa 3 source folders or repositories, and records each component's
   source location in the plan, or records it as not found.
3. **Given** a component in the plan, **When** the skill prepares it, **Then** it records the Elsa 4 target name, the
   porting cases that apply and the tests to port, and ports the component by following each case's example.
4. **Given** prepared feature packages and the user's settings file, **When** the skill runs the assessment, **Then**
   it records the report path in the plan and works through the report by category: Must fix first, then Reconsider,
   then Take into account.
5. **Given** a plan with stage "test migration", **When** a new session starts the skill, **Then** it reads the plan
   first and continues from the recorded stage and component statuses without rerunning finished work.
6. **Given** every remaining definition converts or carries a refusal the user accepted in the plan, **When** the
   skill finishes, **Then** it describes the handoff (FR-020) and changes nothing outside the workspace.

### User Story 2 - The same journey without an AI (Priority: P1)

As an Elsa 3 user who does not use an AI assistant, I can read the skill as documentation and do the same migration
by hand.

**Why this priority**: The skill must not lock the migration to AI tooling, and the README promises a document.

**Independent Test**: A reviewer who has not seen the skill follows the README section to the skill file and completes
the inventory and one component port on the synthetic sample with no AI.

**Acceptance Scenarios**:

1. **Given** the main README, **When** a user looks for Elsa 3 migration, **Then** a short "Migrating from Elsa 3"
   section names the file to read and says that installing it as an AI skill is optional.
2. **Given** the skill file, **When** a human reads it, **Then** every step names the command to run, the file it
   produces and what to do with it, in plain prose.

### User Story 3 - Port a custom component the same way every time (Priority: P1)

As a user porting a custom Elsa 3 component, I follow a fixed catalog case with a before and after example, so the
result does not depend on which AI or which person did the work.

**Why this priority**: Consistent ports are the reason for a catalog, and wrong ports multiply across definitions.

**Independent Test**: For each catalog case marked *port*, the "after" example is an excerpt of the compiled sample
(FR-014) and its tests pass. For each case marked *redesign*, the catalog names the Elsa 4 alternative.

**Acceptance Scenarios**:

1. **Given** an Elsa 3 activity that uses `context.GetRequiredService<T>()`, **When** the user follows case 10,
   **Then** the Elsa 4 activity takes the service as a constructor parameter.
2. **Given** an Elsa 3 composite that schedules child activities, **When** the user looks up case 7, **Then** the
   catalog says it cannot be ported as an activity and names the alternatives before any code is written.
3. **Given** an Elsa 3 activity whose JSON type name differs from its namespace and class name, **When** the user
   follows case 2, **Then** the Elsa 4 activity carries the replaced-type marker naming the Elsa 3 type.

### User Story 4 - Know what reaches the AI provider (Priority: P2)

As a user with confidential workflows and code, I know before the skill reads anything what it will send to the AI
provider, and it asks before reading anything beyond the redacted report.

**Why this priority**: Users will not run the skill on business workflows without this.

**Independent Test**: Start the skill and check that the privacy statement comes before the first file read, and that
it asks before opening a raw definition or a source file.

**Acceptance Scenarios**:

1. **Given** a new session, **When** the skill starts, **Then** its first output is the privacy statement.
2. **Given** a finding whose guidance is not enough to act on, **When** the skill wants to read the raw definition,
   **Then** it asks first and names the file and the JSON path.

### User Story 5 - Contributors keep the skill current (Priority: P2)

As a contributor changing the Elsa 3 converter or an activity authoring API, I know that the change also needs JTest
suites and a skill update, and an AI in my session reminds me if I forget.

**Why this priority**: The skill is only as good as its match with the converter and the Elsa 4 API.

**Independent Test**: Read the Elsa 3 mapping README and `AGENTS.md`; both state the trigger. Change an Elsa 4 API a
port case uses and confirm the sample's build breaks.

**Acceptance Scenarios**:

1. **Given** a converter change, **When** a contributor or an AI reads the contributor documentation, **Then** it
   names the JTest suites and the skill files (catalog, plan template) the change must update.
2. **Given** a renamed Elsa 4 activity API that a port case uses, **When** CI builds, **Then** the compiled sample
   fails, or the catalog check fails because the excerpt no longer matches.

### User Story 6 - Install the skill (Priority: P3)

As a user of Claude Code or of another agent that reads `.agents/skills/`, I can install the skill from this
repository.

**Why this priority**: Convenient, but reading the file already works.

**Independent Test**: Add this repository as a Claude Code plugin marketplace, install the plugin and see the skill;
copy the skill folder into a workspace's `.agents/skills/` and see the agent find it.

**Acceptance Scenarios**:

1. **Given** the repository's marketplace file, **When** a Claude Code user installs the plugin, **Then** the skill
   folder is the plugin's only skill and nothing outside the plugin folder is needed.
2. **Given** a copy of the skill folder in a workspace's `.agents/skills/`, **When** an agent lists skills, **Then**
   it finds the skill by its name and description.

### Edge Cases

- The report's schema version is one the skill does not read: the skill stops and tells the user to update the
  skill, instead of guessing.
- The tool refuses to run (bad input, a package or setting it cannot load) and writes no report: the skill shows the
  tool's reason, records it in the plan and stops until the user fixes the input.
- A component the report names is not found in the source the user gave: the plan records it as not found, and the
  skill asks for more source or records the user's decision to redesign or drop the definitions that use it.
- A component exists in the source but no definition uses it: it is not migrated (inventory comes only from the
  report).
- Docker is not available, or the migration image tagged with the plan's essentials image tag cannot be pulled: the skill stops
  before running anything and says what to install or which version to use.
- A JavaScript global is used other than as a direct call (assigned to a variable, passed as a callback): the
  converter does not rewrite it, so it would fail at run time. The converter is to report such a reference as an
  ordinary finding (see Assumptions); neither the tool nor the skill scans scripts. The skill points the user to
  case 14.
- A definition's only findings are routine ones the report marks as added to every definition: it goes smoothly,
  and the skill does not ask the user to act on them.
- A finding has no Elsa 4 remedy (the report says neither patching the definition nor building a component helps):
  the skill records the user's decision in the plan (redesign, drop, or an accepted refusal) and writes no code.
- The user's Elsa 3 activity uses several catalog cases at once (a trigger with bookmarks, services and outputs): the
  plan lists every case, and the port follows them in catalog order.
- A definition is refused only because a dependency was refused: the skill fixes the root first and does not work on
  the blocked definition.
- The settings file does not enable a custom feature the plan lists: the skill reports it before running the
  assessment.
- The user stops in the middle of a port: the component's status in the plan says so, and the next session continues
  that component.
- A case marked *redesign* applies: the skill does not write workaround code; it records the redesign in the plan and
  the user decides.

## Requirements *(mandatory)*

### Functional Requirements

**The skill folder**

- **FR-001**: The canonical skill MUST be one self-contained folder,
  `plugins/elsa3-migration/skills/elsa3-workflows-migration/`, holding `SKILL.md`, `migration-plan-template.md` and
  `porting-catalog.md`. No step the user must take may depend on a repository file outside the folder; links to
  the repository are for further reading only.
- **FR-002**: `SKILL.md` MUST use the open agent-skill format (a `name` and `description` front matter, then plain
  markdown) and MUST read as ordinary documentation: no wording addressed to one AI model or vendor, and every step
  doable by a human.
- **FR-003**: The adapters MUST hold metadata only: a repository-root `.claude-plugin/marketplace.json` that lists
  one plugin whose source is `plugins/elsa3-migration/`, and that plugin's `.claude-plugin/plugin.json`. Users of
  agents that read `.agents/skills/` copy the skill folder. No adapter repeats the skill's content.
- **FR-004**: The skill MUST state the report schema versions it reads and stop on any other. It is not tied to a tool
  release: the plan records the tag of the essentials engine image the user will go live on (a build tag, not a
  package version), and the skill runs the migration image with that same tag (FR-022).

**Privacy**

- **FR-005**: Before reading any file, the skill MUST tell the user what can reach the AI provider: the redacted
  report, the migration plan and the skill's own files without asking (the plan holds only what the user wrote into
  it: paths, names, decisions and notes); everything else only after the user agrees. It asks once per Elsa 3 source folder before searching it
  (FR-009), and before each raw definition and each other file it opens, naming the file. It never reads the user's
  `appsettings.json` without asking, and warns that the file can hold connection strings and secrets.
- **FR-006**: The skill MUST rely on the report being complete without an AI and always redacted (spec 1237).

**The journey**

- **FR-007**: Each session MUST read the migration plan first. With no plan, the skill creates one from the template
  in the user's AI workspace. The plan is the only state; the command-line tool stays stateless; keeping the files is
  the user's responsibility, and the skill says so.
- **FR-008**: **Inventory.** The skill MUST run `workflows inventory` in the Elsa 3 migration image
  (FR-022) on the export and take the custom components only from the report's inventory: activity types, JavaScript functions, variable types
  and storage drivers, with usage counts. A component no definition uses is not migrated.
- **FR-009**: The skill MUST ask for the folders or repositories of the user's Elsa 3 source and match each
  component to its implementation by a focused search (research R-6): an activity by its Elsa 3 type name (namespace
  and class, or what its activity attribute sets), a JavaScript function by its global name, a variable type by its
  alias, or by the type name before the comma when the export holds an assembly-qualified name, a storage driver by
  its type name. It records the source location, or "not found", in the plan.
- **FR-010**: **Prepare.** For each component the skill MUST record in the plan its Elsa 4 target name, the Elsa 4
  feature and package it belongs to, the porting cases that apply, the tests to port, and its status; then port it
  by following the applicable catalog cases. The JavaScript prefix choice is a recorded step: the user picks one or
  more prefixes and the plan records which function takes which.
- **FR-011**: Before running the assessment, the skill MUST have the user confirm that every custom feature in the
  plan is enabled in their `appsettings.json` (reading the file only with consent, FR-005); after it, it MUST check
  that each appears among the features the report says the run loaded. It reports any missing one.
- **FR-012**: **Test migration.** The skill MUST run `workflows assess` in the Elsa 3 migration image
  (FR-022) with the user's packages folder and settings file, record the report path in the plan, and work through the report: Must fix, then Reconsider,
  then Take into account; root causes before definitions blocked by a dependency; findings that belong to no definition (an unreadable
  export file, a refusal of the whole import) before the rest. It reruns until no such finding is left unaccepted and every remaining
  definition converts or carries a refusal the user accepted, recorded in the plan with the reason.
- **FR-013**: The skill MUST NOT hard-code diagnostic knowledge. It reads each finding's code, message, JSON path and
  guidance from the report, and it reads the categories and buckets the report defines (spec 1237). The skill folder
  contains no diagnostic code literal; examples use a placeholder such as `<code>`.

- **FR-022**: **Running the tool.** The command-line tool runs inside the Elsa 3 migration image that spec 1237 names
  (`elsaworkflows/elsa-elsa3-migration`, tagged like the essentials engine image it is built from), whose entry point is the tool's migration command group. The skill starts it with `docker run` as spec 1237's
  command-line contract shows, without network access: the export and the output folder for every run, plus the
  packages folder and the `appsettings.json` for `assess`. It records the command in the plan. Before the first run of a session, the skill MUST check that Docker is
  available and that the migration image tagged with the plan's essentials image tag (FR-004) can be
  pulled. When either fails, it stops, says what is missing, and runs nothing. When the tool refuses to run, the skill
  shows the tool's reason and stops.

**The porting catalog**

- **FR-014**: The porting catalog MUST hold the 19 cases below, each with: when it applies, the Elsa 3 "before"
  example, the Elsa 4 counterpart and "after" example, the focused search that finds it in Elsa 3 source, and its
  verdict, *port* or *redesign*. A *port* case's "after" example MUST be an excerpt of the compiled sample (FR-016).
  A *redesign* case MUST name the Elsa 4 alternatives and MUST NOT offer workaround code.

  | # | Case | Elsa 4 counterpart | Verdict |
  |---|------|--------------------|---------|
  | 1 | Base class | `Activity` / `Activity<TResult>`; the result is one typed record | port |
  | 2 | Type name and the exact-name rule | The catalog key is the CLR full name, so namespace and class match the Elsa 3 type name; the replaced-activity-type marker only where they cannot match | port |
  | 3 | Inputs (defaults, UI hints, required) | Plain properties with the activity input attribute; required marker; default value, UI hint, options | port |
  | 4 | Outputs | Output attribute on the result record's properties | port |
  | 5 | Completion | Return a completion transition instead of calling complete | port |
  | 6 | Outcomes | Outcome attributes on the class; select by name in the completion | port |
  | 7 | Scheduling child activities | None for activity authors: scheduling children is engine-only | redesign: a reusable Activity Graph definition, or the built-in composites |
  | 8 | Signals and bookmarks | Bookmarks: a stateful activity that suspends and resumes on a resume target. Signals: none | port (bookmarks); redesign (signals) |
  | 9 | Triggers | A trigger activity plus a trigger stimulus provider registered in the feature | port |
  | 10 | Services | Constructor injection; the execution context offers no service lookup | port |
  | 11 | Variables and workflow state | Variable-reference inputs read and written through the step's variables; undeclared names and workflow state need their access markers; a free-form properties bag has no counterpart | port; redesign for the properties bag |
  | 12 | Background and task activities | The background-execution-safe marker; the workflow author chooses background placement (spec 1230) | port when the activity meets the eligibility criteria |
  | 13 | Cancellation and faults | The cancellation token; fault and cancel transitions; no per-activity cancel callback | port |
  | 14 | JavaScript functions | A JavaScript host function declaration with a `prefix.name`, effect-free, JSON in and out, that names the Elsa 3 global it replaces; the converter rewrites direct calls only, so other references to the global are turned into direct calls in the Elsa 3 definition first | port; redesign as an activity when the function has effects |
  | 15 | Liquid filters and other expression additions | Liquid filters: none, filter registration is closed to modules. A new expression syntax: the public expression descriptor and portable expression handler extension points | redesign (Liquid filters: a JavaScript host function or an activity); a new syntax is named with its extension points but carries no example |
  | 16 | Variable type registration | A variable type descriptor provider registering the type under a dotted alias | port |
  | 17 | Storage drivers | Follows spec 1240: a durable-value storage driver registered in the feature, under the driver key the converter keeps for the Elsa 3 driver. Until spec 1240 is implemented, publication refuses non-default variable storage | port once spec 1240 is implemented; redesign (default durable storage) before that |
  | 18 | Feature registration | An Elsa 4 shell feature in a Nuplane package, enabled in `appsettings.json`; activities are discovered from the feature's assembly | port |
  | 19 | Tests | xunit tests that build an activity execution context and call the activity directly | port |

- **FR-015**: Catalog cases MUST be ordered as above and applied in that order when several apply to one component.

**Keeping it correct**

- **FR-016**: A compiled sample MUST exist in the Elsa 3 converter test area: one synthetic `Acme` Elsa 4 feature
  project that implements every *port* case, and an xunit test project for it. Each *port* case's "after" example is
  a marked region of the sample. The sample is synthetic and carries no customer code or data.
- **FR-017**: A check MUST fail when a *port* case's "after" example differs from its marked region in the sample,
  when a marked region has no catalog case, or when the skill folder contains a diagnostic code literal. It runs in
  CI next to the catalog link check, and changes to the skill folder's markdown count as documentation-only, so a
  catalog edit runs the check without the full build.
- **FR-018**: The Elsa 3 mapping README MUST state that a converter change also needs JTest suites for it and an
  update to the migration skill (porting catalog, plan template), and that a change to an Elsa 4 activity authoring
  API used by a *port* case needs the sample and catalog updated. `AGENTS.md` MUST carry a one-line pointer to that
  rule, so an AI in a contributor session raises it. No other enforcement.

**Documentation**

- **FR-019**: The main README MUST gain a short "Migrating from Elsa 3" section that names `SKILL.md` as the file to
  read, says that installing it as an AI skill is optional, and lists what the migration does not cover (FR-021).
- **FR-020**: The skill MUST end with the handoff, as text only: the user keeps their feature projects in any
  repository, publishes their packages through any pipeline and stores the migrated workflows anywhere; going live
  means starting the essentials engine image with their `appsettings.json`, their packages and the `migrated/` folder
  mounted. The skill performs no publishing or deployment step.
- **FR-021**: The skill and the README section MUST state what is out of scope: live workflow instances, running
  Elsa 3 next to Elsa 4, persistence data migration, identity and authentication migration, Elsa 3 Studio
  extensions, and C# and Python expressions.

### Key Entities

- **Skill folder**: the canonical, self-contained skill: `SKILL.md`, the plan template, the porting catalog.
- **Migration plan**: the user's filled-in copy of the template, in their workspace: stage; prefix choices; the
  Elsa 4 features and packages and the components grouped into them; per component its Elsa 3 source location,
  Elsa 4 target name, applicable cases, tests to port and status; accepted refusals with reasons; the path of the
  last report.
- **Porting case**: one of 19 entries: applies-when, before, after, search, verdict.
- **Migration report**: `report.json` written by the command-line tool; its schema belongs to spec 1237.
- **Compiled sample**: the synthetic Elsa 4 feature and tests whose marked regions are the catalog's "after" examples.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A reviewer with no AI follows the README section and the skill file and completes the inventory and the
  port of one custom activity on a synthetic export in one sitting, without asking for help.
- **SC-002**: Two sessions of two different AI assistants, given the same synthetic export and source, produce plans
  with the same components, target names and applicable cases.
- **SC-003**: All 19 catalog cases carry a verdict and before and after examples; every *port* example matches its
  sample region and the sample's tests pass.
- **SC-004**: The skill folder contains zero diagnostic code literals and zero references to one AI vendor; the
  check enforces the Elsa 3 conversion and value-flow code prefixes, and review covers the rest.
- **SC-005**: A session started halfway resumes from the plan without repeating a finished step.
- **SC-006**: Renaming an Elsa 4 API a *port* case uses makes CI fail.

## Assumptions

- Spec 1237 delivers `inventory` and `assess`, the redacted `report.json` with the content listed in the plan's
  report contract, and the categories and buckets. This spec only references that schema.
- Spec 1236 delivers the essentials engine image the handoff names; spec 1237 delivers the Elsa 3 migration image,
  built from it, that runs the tool.
- The converter, on the initiative branch, diagnoses unregistered variable types and unmapped storage drivers and maps declared JavaScript
  functions to prefixed host functions; the skill relies on the guidance those diagnostics carry. A converter
  change that spec 1237 also depends on adds a finding for a declared JavaScript global used other than as a direct
  call.
- The user has Docker, and the .NET SDK to build their feature packages. The only network calls are NuGet restore
  and image pulls.
- Spec 1240 decides whether workflow variables, inputs and outputs can use a registered storage driver and how
  the converter carries Elsa 3 drivers over. Case 17 follows it: if spec 1240 is not approved, case 17 stays
  *redesign* and its sample task is dropped.
- The sample is never packed or published.
