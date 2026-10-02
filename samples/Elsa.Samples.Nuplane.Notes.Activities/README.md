# Elsa.Samples.Nuplane.Notes.Activities

The workflow half of the [Notes sample](../Elsa.Samples.Nuplane.Notes/README.md): an "Add note" activity that writes a note
through the Notes module's `NoteStore`, released with the module in two versions. It shows what happens to a workflow when
the package behind one of its activities is upgraded in place, on a host that is never restarted.

## The activity

**Release 1.0.0** (`DemoVersion=1`, the default) contributes activity version `1.0.0`: a required `Text` input, and a note
written as `POST /demo/notes` writes one. Its feature, `NotesActivities`, depends on the module's `Notes` feature.

**Release 1.1.0** (`DemoVersion=2`) contributes activity version `1.1.0`, with an optional `Tags` input (comma-separated)
stored in the column release 1.1.0 of the Notes module adds. Its feature depends on `NotesWithTags`. With tags, the activity
asks the shared dormancy check first: while schema version `2.0.0` of the notes family is not finalized it faults with the
reason and writes nothing. Once it is finalized, the note and its tags are two saves, as the two endpoints are. Without tags
it writes the note as release 1.0.0 does, in the format of the version the host may write.

Each release contributes its own catalog version, so after an upgrade the designer offers `1.1.0` beside `1.0.0`, and a node
stays on the version it pins until it is moved to the new one.

## Upgrading in place

Drop release 1.1.0 of both packages into a running Workbench's feed. The Workbench installs them, refreshes its feature
catalog, and switches at the next shell reload: `POST /_admin/shells/reload/{name}`, or by itself when
`Elsa:Shells:ReloadOnPackageChange` is `true` (see [Hot reload](../../docs/foundation-host-feeds.md#hot-reload-after-a-package-change)).
While the module's `AddTags` migration is pending, a host whose policy is `Validate` refuses that reload with the
`dotnet elsa persistence apply` command to run, and keeps serving release 1.0.0.

A node pinned to version `1.0.0` keeps running after the upgrade, but on release 1.1.0's class: the activity's alias is its
CLR type name, and one type is registered per alias (see
[Elsa.Activities.Runtime](../../src/essentials/Activities/Runtime/README.md#activity-versions-after-an-in-place-upgrade)).
That is why release 1.1.0 keeps `Text` as it was and adds `Tags` as optional. A release that renamed or removed an input would
break every workflow pinned to an earlier version, and nothing warns of that yet.

## Build and drop

Pack each release of both packages into the Workbench's feed; this one depends on the Notes package of the same release, and
the feed resolves it from there:

```bash
for project in Elsa.Samples.Nuplane.Notes Elsa.Samples.Nuplane.Notes.Activities; do
  dotnet pack samples/$project/$project.csproj -c Release -p:DemoVersion=1 -o src/apps/Elsa.Workbench/packages
done
```

The Notes module also needs its EF engine from a resolve-only feed, and its features need a connection; the
[Notes README](../Elsa.Samples.Nuplane.Notes/README.md#build-pack-feed-layout) describes both. Enable the features on a shell:

```json
"NotesEntityFrameworkCore": { "Provider": "Sqlite", "ConnectionString": "Data Source=notes.db" },
"Notes": {},
"NotesWithTags": {},
"NotesActivities": {}
```

It is a package of its own, not part of the Notes package, so the Notes package keeps loading on a host that composes no
workflow modules, such as `Elsa.Foundation.Host`: its activity types reference the activity contracts, which only a workflow
host carries.

| Folder | What is in it |
|---|---|
| the project folder | the feature (`NotesActivitiesFeature.cs`) |
| `Activities/` | the activity, `AddNote`, and what both releases share of it |
| `Reconciliation/` | its catalog entry, `NotesActivityReconciliationSource` |
| `V1/` | compiled into release 1.0.0 only: its version, its feature dependency, and how a note is written |
| `V2/` | compiled into release 1.1.0 only: the same, with the `Tags` input and the dormancy check |

Every type keeps the package's namespace, whatever its folder: the activity's full name is its type key and its alias, which
every workflow that uses it stores.

## Tests

`tests/essentials/Samples/Nuplane/Notes/Activities/Tests` runs both releases of the activity against a real SQLite database:
release 1.0.0 writes a note; release 1.1.0 writes a note with its tags once schema version `2.0.0` is finalized, refuses a
tagged note and writes nothing while it is not, and writes an untagged note in the old format while it is not. It also reads
each release's catalog entry: `1.0.0` with `Text`, `1.1.0` with `Text` and `Tags`.
