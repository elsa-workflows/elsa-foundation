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
- the release's declaration of the module's persisted schema, which moves from version `1.0.0` to `2.0.0`, one upcaster and
  one rewriter.

The upcaster is what lets 1.1.0 read what 1.0.0 wrote. A row written by release 1.0.0 has no tags column to hold, so its
`TagsJson` is empty; when 1.1.0 reads such a row, the upcaster (`NotesOneToTwo`) turns it into a row with an empty list of
tags before the code sees it, and the code that reads tags relies on that: a row that comes out of the upcast without tags is
treated as corruption and refused, not read as "no tags". A 2.0.0 row is left as it is. The rewriter (`NotesRewriter`) is what
the host's post-finalization backfill calls, one row at a time, once `2.0.0` is finalized: it upcasts each row still
stamped `1.0.0` and writes it back stamped `2.0.0`, after which the family is recorded complete at `2.0.0` (`persistence status`
says `complete from 2.0.0`). Without it the host would log the family as blocked, because a family with an upcaster and no
rewriter cannot be completed.

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
| `V2/` | compiled into release 1.1.0 only: its `NotesFamily.cs` (family at `2.0.0`, with the upcaster and the rewriter), `NoteRecord.Tags.cs`, the tags feature and store, `NotesOneToTwo.cs`, `NotesRewriter.cs` with the store's rewrite, and `V2/Migrations` (`AddTags`) |
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

Requirements: `dotnet`, `rsync`, `curl` and a working `python3` (the scripts check for it and say so when it is missing). The PostgreSQL walkthrough also needs Docker and the `postgres:16-alpine` image.

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

### Walkthrough: two hosts sharing one PostgreSQL database

Two hosts on one database show the point of the exercise: 1.1.0 finalizes schema version `2.0.0` only when **every** host
that shares the database can read it. For that the hosts join the durable cluster membership with `--cluster HOSTID`, each with
an id of its own (`Elsa:Cluster:Membership:HostId`; `run-host.sh` sets it together with the
`Elsa:Cluster:Membership:EntityFrameworkCore:*` keys from `ELSA_EF_CONNECTION`), and the database gets one more module,
`Cluster.Membership`. This needs a build of `Elsa.Foundation.Host` that carries the EF cluster membership provider;
`run-host.sh --cluster` says so when the build does not. `--fast-membership` shortens the membership timings (2 s heartbeat,
10 s expiry, 2 s skew allowance; the defaults are 10 s, 30 s and 5 s). A host stopped cleanly with Ctrl-C leaves the fleet at
once and needs no wait; a host that crashed or stalled is dropped only after the expiry, so one restarted after a crash rejoins
in about 12 s rather than 35. The margin is deliberate: a heartbeat that stalls for a few seconds on a loaded laptop does not
drop a 1.0.0 host from the count in the middle of the demo, which would finalize `2.0.0` early. The same steps run on Sqlite (one machine only):
leave the container out and leave `ELSA_EF_CONNECTION` unset, and drop `--provider PostgreSql`.

```bash
# 0. A database. Cached images only: postgres:16-alpine.
docker run -d --name elsa-demo-pg -e POSTGRES_PASSWORD=demo -e POSTGRES_DB=elsa -p 127.0.0.1:55432:5432 postgres:16-alpine
export ELSA_EF_CONNECTION="Host=127.0.0.1;Port=55432;Database=elsa;Username=postgres;Password=demo"
MODULES=Samples.Notes,Cluster.Membership
DB=(--environment Development --provider PostgreSql --modules $MODULES)

# 1. Both hosts on release 1.0.0, each with its own feed. Start host a, wait until it is ready, then host b.
bash tools/demo/pack.sh 1 --host a --host b
bash tools/demo/run-host.sh a --port 5101 --provider PostgreSql --cluster host-a --fast-membership --prepare-only
bash tools/demo/elsa.sh persistence apply --restore --host artifacts/demo/hosts/a "${DB[@]}"   # creates both modules' tables
bash tools/demo/run-host.sh a --port 5101 --provider PostgreSql --cluster host-a --fast-membership   # terminal 1
bash tools/demo/run-host.sh b --port 5102 --provider PostgreSql --cluster host-b --fast-membership   # terminal 2
docker exec elsa-demo-pg psql -U postgres elsa -c 'select "HostId","Status" from elsa_cluster_members'   # host-a and host-b, Active
curl -X POST localhost:5101/demo/notes -H 'content-type: application/json' -d '{"text":"written on a"}'
curl -X POST localhost:5102/demo/notes -H 'content-type: application/json' -d '{"text":"written on b"}'
curl localhost:5101/demo/notes                                        # both notes, from either host: one database

# 2. Host b moves to release 1.1.0; host a stays on 1.0.0. Stop host b (Ctrl-C), then:
bash tools/demo/pack.sh 2 --host b
bash tools/demo/run-host.sh b --port 5102 --provider PostgreSql --cluster host-b --fast-membership  # refused: AddTags is pending
bash tools/demo/elsa.sh persistence apply --host artifacts/demo/hosts/b "${DB[@]}"                # applies AddTags
curl localhost:5102/demo/notes                                        # the next request activates b's shell: 200
curl -i localhost:5102/demo/notes/with-tags                           # 409 schema-version-not-finalized: host a reads only 1.0.0
curl -X POST localhost:5101/demo/notes -H 'content-type: application/json' -d '{"text":"a, still 1.0.0"}'   # 200
bash tools/demo/elsa.sh persistence status --host artifacts/demo/hosts/b "${DB[@]}" --family SamplesNotes
#    SamplesNotes: finalized at 1.0.0; this host reads [1.0.0, 2.0.0]
#      2.0.0: pending, held by nothing; waits for every counted member to read it

# 3. Host a follows. Stop host a (Ctrl-C), then:
bash tools/demo/pack.sh 2 --host a --no-host
bash tools/demo/run-host.sh a --port 5101 --provider PostgreSql --cluster host-a --fast-membership
curl localhost:5101/demo/notes/with-tags                              # 200 on both hosts; old notes have "tags": []
curl localhost:5102/demo/notes/with-tags
bash tools/demo/elsa.sh persistence status --host artifacts/demo/hosts/a "${DB[@]}"
#    SamplesNotes: finalized at 2.0.0, and within seconds "complete from 2.0.0": the backfill has rewritten the old rows

docker rm -f elsa-demo-pg                                             # when done
```

Timings on a laptop, to plan the pauses: `pack.sh` 5 to 15 s once the host is built (up to a minute or two while other builds
compete for the machine, so pack before you present); a host is ready 10 to 15 s after it starts; `persistence apply` about 1 s
(the first `elsa.sh` call builds the tool and takes 25 s); the shell of a host that was refused activates on the first
request after the apply; the second host's upgrade finalizes `2.0.0` within a second of that host being ready.

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
- No host or tool lists the cluster's members. The walkthrough reads the membership table (`elsa_cluster_members`) with `psql`,
  and `persistence status` shows the finalized version and that `2.0.0` "waits for every counted member to read it" without
  naming the member that cannot yet.
- Start the two hosts one after the other, not in the same instant: when both create the membership database's identity row
  at once, the loser logs a duplicate-key error on that insert at startup and carries on.
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
as a note with no tags; runs the rewriter against a real database, once for each outcome it can answer (rewritten, already current,
missing, and a conflict provoked by a write between its read and its save) and checks it refuses to write below the version it was
asked to reach; runs `AddTags` through the expand-only migration guard; and proves the upcaster over a committed pair
of fixture rows (`Fixtures/SchemaUpcasters/SamplesNotes/1.0.0-to-2.0.0`).
