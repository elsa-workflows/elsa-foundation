---
name: elsa3-workflows-migration
description: Migrate Elsa 3 workflow definitions and custom Elsa 3 code (activities, JavaScript functions, variable types, storage drivers) to Elsa 4 with a local test migration. Use when someone moves from Elsa 3 to Elsa 4.
---

# Migrating Elsa 3 workflows to Elsa 4

This guide takes an Elsa 3 export, and the Elsa 3 code it depends on, through a test migration to Elsa 4. It ends with
a complete report of what to patch, what to reconsider, what to take into account and what goes smoothly. You can
follow it by hand, or give it to an AI assistant that reads skills. Every step names the command to run, the file it
produces and what to do with it.

This folder holds everything the steps need:

- `SKILL.md`: this guide;
- `migration-plan-template.md`: the plan that records where the migration stands;
- `porting-catalog.md`: how to port each kind of custom Elsa 3 code, with a tested Elsa 4 example per case.

Links to the Elsa 4 repository are for further reading only.

## What can reach the AI provider

When an AI assistant runs this guide, what it reads is sent to its provider. An assistant's first output in every
session is this statement, before it reads any file, the plan included:

- **Read without asking**: the migration report (`report.json`, which the tool always redacts: it holds no literal
  values, scripts or secrets; a report whose `redacted` marker is not `true` is not read further without asking), the migration plan, and the files in this folder. The plan holds only what you write
  into it: paths, names, decisions and notes.
- **Read only after you agree**:
  - each Elsa 3 source folder or repository: asked once per folder, before searching it (the search shows file names
    and matching lines only);
  - each Elsa 3 source file: asked before opening it, naming the file;
  - each raw workflow definition: asked each time, naming the file and the JSON path it wants to look at;
  - each other file: asked each time, naming the file.
- **Never read without asking, with a warning**: the migration host's local settings (`shells.Local.json`, user
  secrets, environment variables). They can hold connection strings and secrets.

Working by hand, the same list tells you what you would share if you later ask someone for help.

## What this migration does and does not do

It converts Elsa 3 workflow definitions to Elsa 4, ports the custom Elsa 3 code they use, and lets you try the result
on a local Elsa 4 host. Everything happens in your own workspace and your clone of the Elsa 4 repository. Nothing is
published or deployed.

It does not cover:

- live workflow instances (running Elsa 3 instances finish on Elsa 3 or are restarted);
- running Elsa 3 next to Elsa 4;
- persistence data migration;
- identity and authentication migration;
- Elsa 3 Studio extensions;
- C# and Python expressions.

## Start of every session: read the plan

1. Look for `elsa3-migration-plan.md` in your workspace. If it exists, read it first and continue from its `Stage` and
   the component statuses. Do not repeat finished work.
2. If it does not exist, copy `migration-plan-template.md` from this folder into the workspace as
   `elsa3-migration-plan.md`, set `Stage: inventory`, and fill in the export path.
3. The plan is the only record of the migration's state; the tool keeps none. Keeping the plan, the reports and your
   ported projects safe is your responsibility.
4. If the plan names no Elsa 4 clone, ask for the path of your clone of the Elsa 4 repository
   (`elsa-workflows/elsa-foundation`), read its commit with `git -C <clone> rev-parse HEAD`, and record both:
   `Elsa 4 clone: <path> at <commit>`. The tool and the migration host run from that clone.

## Using this guide as an AI skill (optional)

- **Agents that read `.agents/skills/`**: copy this whole folder into your workspace as
  `.agents/skills/elsa3-workflows-migration/`. Copy the folder, not only this file: the steps use the plan template
  and the porting catalog next to it.
- **Assistants that install plugins from a marketplace**: the Elsa 4 repository's README says how to install the
  `elsa3-migration` plugin, which holds this folder.

## Before the first run in a session

Check, and stop if a check fails, saying what is missing. Run nothing until it is fixed.

1. **The .NET SDK** the clone needs is installed: `dotnet --version` from inside the clone succeeds (the clone's
   `global.json` names the version).
2. **The clone** exists at the recorded path.
3. **The commit**: `git -C <clone> rev-parse HEAD` equals the commit in the plan. If it differs, ask whether the clone
   was moved on purpose. If yes, record the new commit and run the assessment again; findings from another commit no
   longer apply. If not, check out the recorded commit.

## Running the tool

The tool runs from the clone with `dotnet run`. Use absolute paths for the export and the output folder, and keep the
output folder in your workspace:

```text
dotnet run --project <clone>/extensions/Elsa3/src/Migration/Cli -- workflows inventory <export> --out <output>
dotnet run --project <clone>/extensions/Elsa3/src/Migration/Cli -- workflows assess <export> --out <output>
```

`<export>` is a folder of Elsa 3 definition JSON files (searched recursively) or a `.zip`. The tool writes
`report.json`, `report.html` and a `migrated/` folder into `<output>`.

After every run, record the command, its exit code and the date as `Last report` in the plan (and the report's
`tool` name and version once a report is written, as the template's `Last report` line shows), then read the result:

- **Exit 0 or 1, and the last line of standard output is the output path**: the run wrote a report. Exit 0 means no
  Must fix or Reconsider finding is left; exit 1 means there are some. Read `<output>/report.json`.
- **Exit 2**: the tool refused to run, for example because the export is missing or empty, or a listed feature cannot
  be composed. It wrote no report. Show the reason from the last line of standard error, record it in the plan, and stop
  until the input is fixed.
- **Anything else** is a build or tool failure, most often a half-ported feature project that does not compile. Show
  the errors, record them on the component being ported, and stop. Do not read the `report.json` in the output folder:
  it is from an earlier run.

### Reading the report

- **Schema version.** This guide reads `report.json` with `schemaVersion` 1. If the report has any other schema
  version, stop and tell the user to update this guide (pull the latest Elsa 4 repository, or reinstall the skill). Do
  not guess. Ignore properties this guide does not mention.
- **No built-in knowledge of findings.** Read each finding's `code`, `message`, `path` and `guidance` from the report,
  and act on its `category`, `remedy`, `componentKind` and `componentName`. Never rely on remembering what a code
  means; refer to a finding by its code as the report gives it (written `<code>` in this guide).
- **Redacted.** Confirm the report's `redacted` marker is `true` before reading further (see the privacy statement).
- **Progress.** Show the user the report's `summary` after every run: definitions, how many go smoothly, the counts per
  category and per bucket, and how many are blocked by a dependency.
- **Buckets** (per definition): `convert-as-is` (no Must fix and no Reconsider finding: nothing to do),
  `incompatible` (a finding with remedy `none`: no Elsa 4 change helps, so the user decides to redesign, drop or accept
  the refusal) and `convert-with-adjustments` (everything else: a component to build, a definition to patch, a finding
  to reconsider, or a dependency that blocks it). Settle the `incompatible` definitions with the user early, so no
  porting is spent on definitions that will be dropped; then work `convert-with-adjustments`.
- **Categories**: `mustFix` (the definition does not convert until fixed), `reconsider` (it converts, but behaves
  differently), `takeIntoAccount` (good to know).
- **Remedy** (Must fix findings only): `build-component` means a custom component is missing, so go to the plan's
  component row the finding names; `patch-definition` means change the Elsa 3 definition at the finding's path;
  `none` means no change in Elsa 4 helps, so the user decides (redesign, drop, or accept the refusal) and no code is
  written.
- **Routine findings** (`routine: true`) are added to every definition. A definition whose only findings are routine
  goes smoothly; do not ask the user to act on them.

## Stage 1: inventory

1. Run `workflows inventory` on the export.
2. Take the custom components from the report's `components` list only: activity types, JavaScript functions,
   variable types and storage drivers, with the number of definitions that use each (the length of its `definitions`
   list) and the number of references (`usages`). Add one row per component to the plan with status `todo`. A component that exists in the
   Elsa 3 source but that no definition uses is not migrated.
3. Show the user the components with their counts.
4. Mark the Elsa 3 built-ins: a storage driver or activity type whose namespace starts with `Elsa.` (Elsa 3's own), and a
   JavaScript function that case 14 of the porting catalog lists as one Elsa 3 defined itself. Record their source as
   `Elsa 3 built-in` and do not search for them.
5. Ask for the folders or repositories of the Elsa 3 source (asking once per folder before searching it) and record
   them under `Elsa 3 source`. For each other component, run a focused search:
   - **an activity type** `Ns.Type`: a class named `Type` in namespace `Ns`; `[Activity("Ns"` on a class named `Type`
     (the second string argument of the three-argument form is the category, not the type); `Type = "Type"` in an
     `[Activity]` attribute; or an `[Activity]` attribute whose third positional argument is an integer version;
   - **a JavaScript function**: `SetValue("<name>"`;
   - **a variable type**: its alias in `AddVariableTypeAndAlias<` or `AddTypeAlias<` (with no alias argument, the
     alias is the class name); when the report names an assembly-qualified type, the namespace and class before the
     comma;
   - **a storage driver**: the namespace and class before the comma in its assembly-qualified name.
6. Record each component's source location, or `not found`. For a component not found, ask for more source, or record
   the user's decision to redesign or drop the definitions that use it.
7. Set `Stage: prepare`.

## Stage 2: prepare

Work through the components one at a time, in the order the user prefers (most used first is a good default). For
each:

1. **Cases.** Read its Elsa 3 source (asking before opening each file, naming it) and find the porting catalog cases
   that apply. Record them in catalog order. A
   component often needs several (a trigger with bookmarks, services and outputs).
2. **Target.** Record the Elsa 4 target name: for an activity, the full class name (case 02); for a JavaScript
   function, `<prefix>.<name>` (case 14); for a variable type, its dotted alias (case 16).
3. **Feature and project.** Record the Elsa 4 feature and project the component goes into (case 18), and the Elsa 3
   tests to port (case 19).
4. **Prefixes.** Before porting the first JavaScript function, ask the user which prefix or prefixes to use and record
   which function takes which.
5. **Port.** Set the status to `porting` and follow each case's steps in catalog order, copying the shape of its "after"
   example. Port the tests. When the port builds and its tests pass, set the status to `ported`. If the session stops
   in the middle, the status stays `porting` and the next session continues that component.
6. **Redesign.** When a case's verdict for this component is *redesign*, write no workaround code. Record the
   alternatives from the case, let the user decide, and set the status to `redesign` (or `dropped`) with the decision
   in Notes.
7. **Elsa 3 built-in activities.** An activity type in an `Elsa.` namespace that the report still names is one of Elsa
   3's own activities that Elsa 4 does not map. There is no source to port and no catalog case: record the user's
   decision (rebuild the step with Elsa 4's own activities, or drop the definitions that use it) and set the status to
   `redesign` or `dropped`.
8. **Interview.** A storage driver is answered by the case 17 interview, once per driver type, covering every
   variable, input and output the report flags for it. Record the goal, the decision and the Elsa 4 feature chosen or
   the link to the user's own plan, and set the status to `interviewed`. Write no workaround code.

When every component is `ported`, `redesign`, `dropped` or `interviewed`, set `Stage: test-migration`.

## Stage 3: test migration

1. **Load the features.** For each feature project in the plan, the migration host in the clone must reference the
   project and list the feature. The user makes these two changes to the clone, or lets the assistant make them after
   asking:
   - reference: `dotnet add <clone>/apps/Elsa.Migration.Host/Elsa.Migration.Host.csproj reference <feature project>`;
   - list: in a local settings source of the host, such as `<clone>/apps/Elsa.Migration.Host/shells.Local.json`
     (git-ignored), under `CShells:Shells:default:Features`, add `"<FeatureName>": {}`.

   Ask the user to confirm both for every feature, and record it in the plan's features table. Read the host's local
   settings only with the user's consent, warning that they can hold secrets. Report any feature that is not referenced
   or not listed before running anything.
2. **Assess.** Run `workflows assess` on the export into the same output folder. Record the report path in the plan.
3. **Check the composed features.** Every feature in the plan must appear in the report's `run.features`. Report any
   missing one, and fix it before reading further.
4. **Work through the report**, in this order:
   1. findings that belong to no definition: `inputFindings` (for example an unreadable export file) and `runFindings`
      (a refusal of the shared import);
   2. per definition, Must fix, then Reconsider, then Take into account;
   3. root causes before the definitions they block: a definition with `blockedBy` set waits until its root is fixed;
      do not work on it directly.

   For each finding:
   - `build-component`: go to the component row named by `componentKind` and `componentName`, and return it to
     `porting`;
   - `patch-definition`: show the user the definition and the finding's path, with the finding's guidance. Ask before
     opening the raw definition, naming the file and the JSON path;
   - `none`: record the user's decision: redesign, drop, or accept the refusal. Only the user adds an accepted refusal
     to the plan, with the reason and the date;
   - a Reconsider or Take into account finding (no remedy): go to the component row its kind and name point to when it
     has one, otherwise to the definition and path, and record what the user decides;
   - a storage-driver finding repeats on every run; it does not reopen an `interviewed` row.
5. Tell the user about `missingTargets`: dispatched workflows the export does not contain.
6. **Rerun** `workflows assess` after each round of changes. A component row becomes `verified` when no finding of any
   category in the last report names it (a storage-driver row stays `interviewed`).
7. The test migration is done when no input or run finding is left unaccepted, and every remaining definition converts
   or carries a refusal the user accepted in the plan. Then set `Stage: handoff`.

The converted workflows are under `<output>/migrated/`: each definition's `exportedTo` (and a reusable workflow's
`wrapperExportedTo`) is a path relative to the output folder.

## Stage 4: handoff

The migration ends here. Outside your workspace, this guide changed only the migration host in your clone: its
project references to your feature projects (stage 3) and its local settings. Nothing is published or deployed. Tell
the user:

- Keep the Elsa 4 feature projects in any repository, and store the migrated workflows anywhere.
- **Trying the result, for now**: run the migration host from the same clone and commit
  (`dotnet run --project <clone>/apps/Elsa.Migration.Host`), with the feature projects referenced and listed as in
  stage 3. Point its two settings, which default to the host's own sample folder, at the output folder of the plan's
  last report, with absolute paths, in its local settings or environment variables:

  ```text
  CShells__Shells__default__Features__JsonWorkflowReconciliation__Options__FolderPath=<output>/migrated/workflows
  CShells__Shells__default__Features__JsonActivityReconciliation__Options__FilePath=<output>/migrated/activities/activities.json
  ```
- This local setup is a temporary development setup, not the production go-live. The go-live comes with an Elsa 3
  migration app profile once Elsa 4's distribution channels are decided, and this guide will be updated then.
- Moving the clone to another commit means running the assessment again before trying the result.

## Rules for the cases this guide does not spell out above

- **A component used in several catalog cases**: record every case; port them in catalog order.
- **A redesign case applies**: write no workaround code; record the redesign and let the user decide.
- **An interview case applies** (storage drivers): ask the objective once per driver type, record the answer and the
  chosen Elsa 4 feature or the link to the user's own plan; write no workaround code.
- **A JavaScript global used other than as a direct call** (assigned to a variable, passed as a callback): once a host
  function provides it, the assessment reports a Must fix finding that patches the definition. Send the user to case
  14: make it a direct call in the Elsa 3 definition. Neither the tool nor this guide scans scripts.
- **A finding has no Elsa 4 remedy**: record the user's decision (redesign, drop or accept the refusal); write no code.
- **A definition refused only because a dependency was refused**: fix the root; leave the blocked definition alone.
- **The migration host does not reference or list a planned feature**: report it before running the assessment.
- **The user stops in the middle of a port**: the component stays `porting`; the next session continues it.
- **A report with an unknown schema version**: stop and ask the user to update this guide.
- **The tool refuses to run, or the build fails**: show the reason or the errors, record them, and stop. Never read an
  older report instead.
