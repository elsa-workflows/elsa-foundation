# Data model: Elsa 3 workflows migration CLI

The report is the data model. Its normative form is [contracts/report.schema.json](contracts/report.schema.json).
This page explains the entities and their rules.

## Report (`report.json`)

| Field | Meaning |
|---|---|
| `schemaVersion` | Integer, `1`. FR-020 says when it changes. |
| `tool` | `{ name, version }` of `dotnet-elsa`. |
| `command` | `inventory` or `assess`. |
| `exitCode` | `0` or `1`, the exit code the run returns (FR-004). A refused run writes no report. |
| `redacted` | Always `true` (FR-019). It lets a consumer check the privacy promise without trusting the version alone. |
| `options` | `{ allVersions }`. |
| `summary` | The counts below. |
| `components` | Custom-component inventory. |
| `definitions` | One entry per converted or refused definition version, ordered by definition id, then version. |
| `notConverted` | Versions skipped by selection (FR-012), ordered by definition id, then version. |
| `missingTargets` | Dispatched or executed workflow targets absent from the export. |
| `inputFindings` | Findings that belong to no definition, such as an unreadable file or duplicate input. |
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
| `componentName` | The Elsa 3 name, for component codes only. |
| `stage` | `input`, `conversion`, `import` or `publish`. |
| `path` | JSON path into the Elsa 3 definition, or `null` for publish findings that have none. |
| `nodeId` | Activity node id, when known. |
| `message` | Redacted message text (FR-019). For a code the converter does not own, the code table's fixed text. |
| `guidance` | Redacted guidance text. |
| `publishStoppedHere` | `true` on the publish finding at which publish stopped (FR-014). |

Findings within a definition are ordered by stage, then path, then code, all ordinal.

## Definition result

`definitionId`, `name`, `version`, `sourceVersionId`, `isReusable`, `bucket`, `blockedBy` (the source version ids of
the **root** refused definitions, the ones refused for a finding of their own, reached through any chain of
dependencies; empty when not blocked), `published` (boolean), `exportedTo` (a path relative to `--out`, or `null`), `findings`, and
`needsCompositeSourceSupport` (true for every exported reusable, and for every exported workflow that uses one,
spec Q2).

**Bucket rule** (FR-017): `incompatible` if any finding has remedy `none`. Otherwise `convert-as-is` if there is no
`mustFix` and no `reconsider` finding. Otherwise `convert-with-adjustments`. A blocked definition has no own error
other than the blocked-by finding, so its bucket is `convert-with-adjustments`, and `blockedBy` names the root.

**Goes smoothly**: a definition with no findings at all.

## Component

`kind`, `name`, `definitions` (the ids of the definitions that use it, sorted), `usages` (finding count). It is derived
only from findings whose code has a component kind (spec FR-018 item 2), so an unused component cannot appear.

## Missing target

`definitionId` (the target), `referencedBy` (sorted definition ids). It is listed as Take into account.

## Run section

`imageVersion` (the migration image's version, which equals the essentials engine version), `packages [{ id, version }]`
(the user's packages only), `features [name]`,
`settingKeys [key]` (sorted, values never written), `persistenceOverridden` (boolean, FR-009).

## Code table

`Elsa3MigrationCodeTable`: a code maps to `{ remedy, componentKind? }`. Codes with an `ELS3-BEHAVIOUR-` prefix are
`reconsider` by severity rule and need no remedy. Every Error-severity code needs a remedy, and every component code
needs a kind. The completeness test enforces both. `ELS3-RECONCILE-BLOCKED-BY-DEPENDENCY` has remedy
`patch-definition`: the blocked definition is fixed by fixing its root, whose own finding carries the real remedy.
That keeps a blocked definition out of `incompatible`.

## Output folder

```text
<out>/
├── report.json
├── report.html
└── migrated/
    ├── workflows/<definitionId>.json   # JSON workflow reconciliation array (one envelope per exported version)
    └── activities/activities.json      # JSON activity reconciliation array, dependencies first (absent if none)
```

The run replaces `report.json`, `report.html` and `migrated/` as a whole. It writes them to a hidden staging folder
`.staging-<guid>` inside `--out` and renames them into place last. That keeps every rename on the same mount, so a
bind-mounted `--out` works. A failed run removes its staging folder and leaves the previous output intact.
