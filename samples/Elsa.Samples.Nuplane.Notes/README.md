# Elsa.Samples.Nuplane.Notes

A small EF module for `Elsa.Foundation.Host`, loaded from a package feed, that comes in two releases. The point is not notes:
it is what an operator sees when a release changes the database while other releases of the module are still running
against it.

## The module

Notes has one table, `elsa_samples_notes`, and two endpoints: `POST /demo/notes` adds a note and `GET /demo/notes` lists
them. It supports Sqlite and PostgreSql.

**Release 1.0.0** is the module as it starts. A note has an id, a text and a creation time, and the module has one migration,
`Initial`.

**Release 1.1.0** adds tags. Everything it adds is in the `V2/` folder, and that folder is the whole change:

- a nullable `TagsJson` column on the notes table, and the `AddTags` migration that adds it (one `AddColumn`, so hosts still
  on 1.0.0 keep working against the migrated database);
- two endpoints, `GET /demo/notes/with-tags` and `POST /demo/notes/{id}/tags`, in a feature of their own, `NotesWithTags`;
- the release's declaration of the module's persisted schema, which moves from version `1.0.0` to `2.0.0`, and one upcaster.

The upcaster is what lets 1.1.0 read what 1.0.0 wrote. A row written by release 1.0.0 has no tags column to hold, so its
`TagsJson` is empty; when 1.1.0 reads such a row, the upcaster (`NotesOneToTwo`) turns it into a row with an empty list of
tags before the code sees it, and the code that reads tags relies on that: a row that comes out of the upcast without tags is
treated as corruption and refused, not read as "no tags". A 2.0.0 row is left as it is.

`NotesWithTags` needs schema version `2.0.0` to be **finalized**, which happens once every host that shares the database can
read it. Until then its endpoints answer **409** with the reason, and release 1.1.0's base endpoints keep working: the rows
they write are stamped `1.0.0`, so every host on 1.0.0 can still read them.

To see the change on screen:

```bash
bash tools/demo/show-change.sh
```

| Folder | What is in it |
|---|---|
| the project folder | the names (`NotesModule.cs`), the module declaration (`AssemblyInfo.cs`), the model (`NotesDbContext.cs`, `NoteRecord.cs`), the store's shared half, the persistence feature and the base endpoints |
| `V1/` | compiled into release 1.0.0 only: its `NotesFamily.cs` (schema family at `1.0.0`) and how a note is added |
| `V2/` | compiled into release 1.1.0 only: its `NotesFamily.cs` (family at `2.0.0`, with the upcaster), `NoteRecord.Tags.cs`, the tags feature and store, `NotesOneToTwo.cs`, and `V2/Migrations` (`AddTags`) |
| `Migrations/` | `Initial` and the version-1 model snapshot, per provider; generated, never hand-written |

One project builds both releases: `DemoVersion=1` (the default) is 1.0.0 and `DemoVersion=2` is 1.1.0. Each builds into folders
of its own, so a build of one never overwrites the other.

## Build, pack, feed layout

```bash
bash tools/demo/pack.sh 1     # release 1.0.0, into artifacts/demo/hosts/a/feed
bash tools/demo/pack.sh 2     # release 1.1.0, into the same feed
bash tools/demo/pack.sh 2 --host b    # into the feed of host b only
```

`pack.sh` builds `Elsa.Foundation.Host` (skip with `--no-host`), packs the module into the feed of each `--host` given (default
`a`) or into a folder named with `--feed`, and fills `artifacts/demo/closure` with the `.nupkg` files of EF Core, the two
database engines and everything they need, copied from the NuGet package cache. Feeds and closure are **flat** folders of
`<id>.<version>.nupkg`: `dotnet pack -o` writes that, whereas `dotnet nuget push` writes a `<id>/<version>/` tree the host's
directory feed cannot read. Relative paths in every script are relative to the repository root.

```
artifacts/demo/hosts/a/feed/   host a's own feed: watched, so a package dropped here is installed
  Elsa.Samples.Nuplane.Notes.1.0.0.nupkg
  Elsa.Samples.Nuplane.Notes.1.1.0.nupkg
artifacts/demo/closure/        resolve-only: EF Core, Microsoft.EntityFrameworkCore.Sqlite, Npgsql.EntityFrameworkCore.PostgreSQL, ...
```

Every host has a feed of its own, so one host can stay on 1.0.0 while another is given 1.1.0. When the host you build carries
`Elsa.Persistence.EntityFramework` itself, `pack.sh` does not put a copy in the feed (a second copy would hide the module from
`dotnet elsa persistence`); it packs one only for a host that does not carry it.

The package version comes from `DemoVersion` in the project file, not from `-p:Version`, because a global `Version` would
also be the version asked of the Elsa projects the module depends on.

## Running the demo

`tools/demo/run-host.sh NAME` makes a directory `artifacts/demo/hosts/NAME` holding a copy of the built host, an
`appsettings.Development.json` naming the feeds, the database engine and `Migrate:Policy=Validate`, and a `shells.json` enabling
the module's three features, then starts it. `tools/demo/elsa.sh` is `dotnet elsa`, built from this checkout.

The database connection is taken from the environment variable `ELSA_EF_CONNECTION`, the one `dotnet elsa persistence` reads.
The scripts hand it to the host as an environment variable and never write it to disk or print it. When it is unset, Sqlite
uses the file `artifacts/demo/notes.db`; PostgreSql always needs it (`--provider PostgreSql`).

Requirements: `dotnet`, `rsync`, `curl` and a working `python3` (the scripts check for it and say so when it is missing).

### Walkthrough: one host

Release 1.0.0 first, then 1.1.0, on one host. The policy is `Validate`, so a host whose database is behind refuses to start
the module rather than migrate underneath the operator; the migration is applied by the tool.

```bash
HOST=(--host artifacts/demo/hosts/a --environment Development --provider Sqlite --modules Samples.Notes)

# 1. Release 1.0.0. The tool installs the feed's packages for the host and creates the database.
bash tools/demo/pack.sh 1
bash tools/demo/run-host.sh a --port 5101 --prepare-only
bash tools/demo/elsa.sh persistence apply --restore "${HOST[@]}"
bash tools/demo/run-host.sh a --port 5101                             # keep it running in its own terminal
curl -X POST localhost:5101/demo/notes -H 'content-type: application/json' -d '{"text":"hello"}'
curl localhost:5101/demo/notes
curl -i localhost:5101/demo/notes/with-tags                           # 404: release 1.0.0 has no such endpoint

# 2. Stop the host, pack release 1.1.0 into the same feed, start it again.
bash tools/demo/pack.sh 2 --no-host
bash tools/demo/run-host.sh a --port 5101
#    The host installs 1.1.0 and refuses the shell, saying why:
#    NotesSqliteDbContext has pending migrations: <id>_AddTags. Apply them out of process (dotnet elsa persistence apply) ...
curl -i localhost:5101/demo/notes                                     # 500, and /health/ready is 503

# 3. Apply the migration.
bash tools/demo/elsa.sh persistence apply "${HOST[@]}"                # applies AddTags
curl localhost:5101/demo/notes                                        # the next request activates the shell
curl localhost:5101/demo/notes/with-tags                              # 200 within seconds; the old notes have "tags": []
curl -X POST localhost:5101/demo/notes/<id>/tags -H 'content-type: application/json' -d '{"tags":["demo"]}'
```

If the shell does not activate by itself after the apply, re-compose it: start the host with a management key and post to
`/reload`, or restart the host.

```bash
export DEMO_KEY='choose-a-key'
bash tools/demo/run-host.sh a --port 5101 --management-key-env DEMO_KEY
curl -X POST localhost:5101/_module-management/reload -H "X-Elsa-Module-Management-Key: $DEMO_KEY"
```

`--management-key-env VAR` turns the host's module-management endpoints on with the key read from the environment variable
`VAR`; the key travels in the host's environment only.

### Walkthrough: two hosts sharing one database

Two hosts on one database show the point of the exercise: 1.1.0 finalizes schema version `2.0.0` only when **every** host
that shares the database can read it. For that the hosts join the durable cluster membership with `--cluster HOSTID`, each with
an id of its own, and the database gets one more module, `Cluster.Membership`. This needs a build of `Elsa.Foundation.Host`
that carries the EF cluster membership provider; `run-host.sh --cluster` says so when the build does not.

```bash
export ELSA_EF_CONNECTION="Data Source=$PWD/artifacts/demo/notes.db;Pooling=False"   # or leave it unset for this same file
MODULES=Samples.Notes,Cluster.Membership
HOST_A=(--host artifacts/demo/hosts/a --environment Development --provider Sqlite --modules $MODULES)

# 1. Both hosts on release 1.0.0, each with its own feed.
bash tools/demo/pack.sh 1 --host a --host b
bash tools/demo/run-host.sh a --port 5101 --cluster host-a --prepare-only
bash tools/demo/elsa.sh persistence apply --restore "${HOST_A[@]}"
bash tools/demo/run-host.sh a --port 5101 --cluster host-a            # terminal 1
bash tools/demo/run-host.sh b --port 5102 --cluster host-b            # terminal 2
curl -X POST localhost:5101/demo/notes -H 'content-type: application/json' -d '{"text":"written on a"}'
curl localhost:5102/demo/notes                                        # host b reads it: one database

# 2. Host a moves to release 1.1.0; host b stays on 1.0.0.
#    Stop host a, then:
bash tools/demo/pack.sh 2 --host a --no-host
bash tools/demo/run-host.sh a --port 5101 --cluster host-a            # refused: AddTags is pending
bash tools/demo/elsa.sh persistence apply "${HOST_A[@]}"
curl -i localhost:5101/demo/notes/with-tags                           # 409: host b cannot read 2.0.0 yet
curl -X POST localhost:5101/demo/notes -H 'content-type: application/json' -d '{"text":"written on a, still as 1.0.0"}'
curl localhost:5102/demo/notes                                        # host b reads it too

# 3. Host b follows. Stop host b, then:
bash tools/demo/pack.sh 2 --host b --no-host
bash tools/demo/run-host.sh b --port 5102 --cluster host-b
curl localhost:5101/demo/notes/with-tags                              # 200 within seconds on both hosts; old notes have "tags": []
curl localhost:5102/demo/notes/with-tags
```

### Keeping 2.0.0 dormant on purpose

A hold keeps a version from finalizing until an operator releases it: a canary. Place it before the host runs 1.1.0.

```bash
bash tools/demo/elsa.sh persistence hold    "${HOST[@]}" --family SamplesNotes --version 2.0.0 --reason "canary" --operator me
bash tools/demo/run-host.sh a --port 5101
curl -i localhost:5101/demo/notes/with-tags        # 409: "It is held by an operator: version '2.0.0' of schema family 'SamplesNotes' ..."
curl -X POST localhost:5101/demo/notes ...          # still works; the row is stamped 1.0.0 with no tags
bash tools/demo/elsa.sh persistence status  "${HOST[@]}" --family SamplesNotes
bash tools/demo/elsa.sh persistence release "${HOST[@]}" --family SamplesNotes --version 2.0.0 --operator me
curl localhost:5101/demo/notes/with-tags           # 200 at the next evaluation (5 seconds here), no restart
```

## Known limitations

- **Hot-installing 1.1.0 into a running 1.0.0 host is not refused yet, and does not complete.** The folder watcher installs the
  package and reloads the shell without a restart, and the new endpoints appear, but the host goes on counting the 1.0.0
  assembly it already loaded, so it never finalizes `2.0.0` and `NotesWithTags` stays at 409 until the host restarts. Stop the
  host, pack, and start it again, as in the walkthroughs.
- `dotnet elsa persistence` reads the packages a host has installed, so a host must have reconciled its feed at least once
  (or the tool must be run with `--restore`) before the tool can see the module.
- `POST /_module-management/reload` re-composes the shells; it does not by itself say why a shell refused to activate. The host's
  log does.

## Regenerating the migrations

They are generated with dotnet-ef through a design-time startup project, `tools/demo/Elsa.Samples.Nuplane.Notes.Tooling`, which has
one factory line per provider. `bash tools/demo/generate-notes-migrations.sh initial` and `... tags` build the release each
describes; `tags` diffs the version-2 model against the committed version-1 snapshot, so it needs `initial` to have run first.
Do not run `tools/ef/generate-module-migrations.sh` for this module: it regenerates the first-party modules, and this one is
not among them.

## Tests

`tests/essentials/Samples/Nuplane/Notes/Tests` builds both releases and checks what each declares (schema version, upcaster,
content columns, migrations, feature requirement); applies `Initial` and then `AddTags` to a real Sqlite database and checks the
schema and that each release's model matches its snapshot; writes a note under release 1.0.0 and reads it through release 1.1.0
as a note with no tags; runs `AddTags` through the expand-only migration guard; and proves the upcaster over a committed pair
of fixture rows (`Fixtures/SchemaUpcasters/SamplesNotes/1.0.0-to-2.0.0`).
