# Data model: Elsa 3 workflows migration CLI

The report is the data model. Its normative form is [contracts/report.schema.json](contracts/report.schema.json).
This page explains the entities and their rules.

## Report (`report.json`)

| Field | Meaning |
|---|---|
| `schemaVersion` | Integer, `1`. FR-020 says when it changes. |
| `tool` | `{ name, version }`: `elsa3-migration-cli` and the CLI's own informational version. |
| `command` | `inventory` or `assess`. |
| `exitCode` | `0` or `1`, the exit code the run returns (FR-004). A refused run writes no report. |
| `redacted` | Always `true` (FR-019). It lets a consumer check the privacy promise without trusting the version alone. |
| `options` | `{ allVersions }`. |
| `summary` | The counts below. |
| `components` | Custom-component inventory. |
| `definitions` | One entry per converted or refused definition version, ordered by definition id, then version. |
| `notConverted` | Versions skipped by selection (FR-012), ordered by definition id, then version. |
| `missingTargets` | Dispatched or executed workflow targets absent from the export. |
| `runFindings` | Refusals of the shared import that name no single definition, such as a plan error with no item or a refusal of the whole selection at apply. Each is a `mustFix` finding at stage `import`; its text is the code table's fixed text or, for a converter-owned code, its redacted message. Each affected definition also carries `ELS3-MIGRATION-RUN-REFUSED`. |
| `inputFindings` | Findings with no definition identity: a file that is not valid JSON, or valid JSON with no Elsa 3 definition. A duplicate input is a definition result, with an `error` finding and its own bucket. |
| `run` | The settings and composition section (FR-018 item 4). |

No field holds a timestamp, an absolute path, a machine name or a run id (FR-022). All paths in the report are relative: `exportedTo` to `--out`, and input file paths to the
export root, use `/` separators, and are ordinal-sorted. A zip entry path is used as it is.

## Summary

`definitions`, `goesSmoothly`, `published`, `exported`, `blockedByDependency`, `notConverted`, plus
`byCategory { mustFix, reconsider, takeIntoAccount }` (finding counts) and
`byBucket { convertAsIs, convertWithAdjustments, incompatible }` (definition counts).

## Finding

| Field | Rule |
|---|---|
| `code` | The diagnostic code as emitted. |
| `severity` | `error`, `warning` or `info`. |
| `category` | `mustFix`, `reconsider` or `takeIntoAccount` (FR-015). |
| `remedy` | For `mustFix` only: `patch-definition`, `build-component` or `none` (FR-016). |
| `componentKind` | For component codes only: `activity`, `javascript-function`, `variable-type` or `storage-driver`. |
| `componentName` | The Elsa 3 name, for component codes only, read from the diagnostic's `ComponentName` metadata (FR-016a). |
| `stage` | `input`, `conversion`, `import` or `publish`. |
| `path` | JSON path into the Elsa 3 definition, or `null` for publish findings that have none. |
| `nodeId` | Activity node id, when known. |
| `message` | Redacted message text (FR-019). For a code the converter does not own, the code table's fixed text. |
| `guidance` | Redacted guidance text. |
| `publishStoppedHere` | `true` on the publish finding at which publish stopped (FR-014). |
| `routine` | `true` for a code the code table marks routine, such as the null-policy Info added to every definition. |

Optional members (`remedy`, `componentKind`, `componentName`, `nodeId`, `publishStoppedHere`, `routine`) are left out
when they do not apply, never written as `null`.

Findings within a definition are ordered by stage, then path, then code, all ordinal.

## Definition result

`definitionId`, `name`, `version`, `sourceVersionId`, `isReusable`, `bucket`, `blockedBy` (the source version ids of
the **root** refused definitions, the ones refused for a finding of their own, reached through any chain of
dependencies; empty when not blocked), `published` (boolean), `exportedTo` (a path relative to `--out`, or `null`; for a reusable, the activities file), `wrapperExportedTo` (for a
reusable, the path of its wrapper workflow file; `null` otherwise, always written like `exportedTo`), and `findings`.

**Bucket rule** (FR-017): `incompatible` if any finding has remedy `none`. Otherwise `convert-as-is` if there is no
`mustFix` and no `reconsider` finding. Otherwise `convert-with-adjustments`. A blocked definition has no own error
other than the blocked-by finding, so its bucket is `convert-with-adjustments`, and `blockedBy` names the root.

**Goes smoothly**: a definition with no finding other than routine ones (`routine: true`).

## Component

`kind`, `name`, `definitions` (the ids of the definitions that use it, sorted), `usages` (finding count). It is derived
only from findings whose code has a component kind (spec FR-018 item 2), so an unused component cannot appear.

## Missing target

`definitionId` (the target), `referencedBy` (sorted definition ids). It is listed as Take into account.

## Run section

`features [name]` (the composed features, sorted), `settingKeys [key]` (sorted, values never written),
`persistenceOverridden` (boolean, FR-009). Whether local configuration sources were applied follows from `command`:
`assess` applies them, `inventory` does not (FR-008).

## Code table

`Elsa3MigrationCodeTable`: a code maps to `{ remedy, componentKind?, routine, fixedText? }`. Every Elsa 3 entry has a
remedy, used whenever the code is emitted at Error, because constants carry no severity. The table covers the
converter's, the import's and reconciliation's codes, and the tooling's own `ELS3-MIGRATION-*` codes:
`ELS3-MIGRATION-INPUT-UNREADABLE` (a file that is not valid JSON; remedy `patch-definition`),
`ELS3-MIGRATION-UNSUPPORTED-INPUT` (an existing code, for valid JSON that holds no Elsa 3 definition) and
`ELS3-MIGRATION-NO-PUBLISHED-VERSION` (Info) and `ELS3-MIGRATION-SUPERSEDED-VERSION` (Info: an older selected workflow version, or an older reusable version's
wrapper, imported for its findings but neither published nor exported, as in startup reconciliation; an older
reusable version's activity is published and exported) and `ELS3-MIGRATION-RUN-REFUSED` (Error, remedy
`patch-definition`: a selected definition that a run-level refusal kept from being applied). The table lists `ELS3-BEHAVIOUR-CUSTOM-STORAGE-DRIVER-REPLACED` as a `storage-driver` component code
(a Reconsider warning; its remedy, `build-component`, applies only if it is ever emitted at Error).
`ELS3-BEHAVIOUR-MEMORY-STORAGE-PERSISTED` is a `storage-driver` component code too. `fixedText` is the
report text for a code the converter does not own. Every Elsa 3 code needs an entry, whatever its severity. An Elsa 4
code with no entry gets `patch-definition` and a default fixed text. Every skip is reported as an `error` finding. Codes with an `ELS3-BEHAVIOUR-` prefix are
`reconsider` by severity rule; their entries still carry a remedy, used only if one is ever emitted at Error. Every component code needs a kind. The completeness test enforces both rules. `ELS3-RECONCILE-BLOCKED-BY-DEPENDENCY` has remedy
`patch-definition`: the blocked definition is fixed by fixing its root, whose own finding carries the real remedy.
That keeps a blocked definition out of `incompatible`.

## Output folder

```text
<out>/
├── report.json
├── report.html
└── migrated/
    ├── workflows/<definitionId>.json   # JSON workflow reconciliation array (one envelope per exported version);
    │                                   #   the folder always exists, empty when nothing was exported
    └── activities/activities.json      # JSON activity reconciliation array, dependencies first; [] when none
```

The run replaces `report.json`, `report.html` and `migrated/` as a whole. It writes them to a hidden staging folder
`.staging-<guid>` inside `--out` and renames them into place last. That keeps every rename on the same volume as
`--out`. A failed run removes its staging folder and leaves the previous output intact.
