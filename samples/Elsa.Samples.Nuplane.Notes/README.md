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

## The Add note activity

The workflow half of this sample is a package of its own,
[Elsa.Samples.Nuplane.Notes.Activities](../Elsa.Samples.Nuplane.Notes.Activities/README.md): an "Add note" activity released
with the module, 1.0.0 with a text and 1.1.0 with optional tags too, which a workflow host such as `Elsa.Workbench` loads beside
it. Keeping it out of this package lets this one load on a host that composes no workflow modules, `Elsa.Foundation.Host` among
them. Its README shows what an in-place upgrade does to a workflow pinned to the activity's first version.

## Build, pack, feed layout

Release 1.0.0 into `artifacts/demo/hosts/a/feed`, then release 1.1.0 into the same feed, then release 1.1.0 into the feed of host b only:

```bash
bash tools/demo/pack.sh 1
bash tools/demo/pack.sh 2
bash tools/demo/pack.sh 2 --host b
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

Requirements: `dotnet`, `rsync`, `curl`, `jq` and a working `python3` (the scripts check for the tools they use and name the one that is missing). The PostgreSQL walkthrough also needs Docker and the `postgres:16-alpine` image.

To present it, use `tools/demo/RUNBOOK.md`: the script for both acts, with every command, expected output and timing.
`bash tools/demo/prepack.sh` does everything slow beforehand and stages both releases; `bash tools/demo/publish.sh <1|2> --host NAME`
is then a file copy into that host's feed; `bash tools/demo/reset.sh` puts everything back; `bash tools/demo/rehearse.sh` plays
both acts unattended. The commands the presenter types (`note`, `withtags`, `reload`, `status`, `waitfor`, ...) are the shell functions of
`tools/demo/helpers.sh`, which the runbook sources and `rehearse.sh` sources too, so the rehearsal asserts the output of exactly what is typed.

### Walkthrough: one host

Release 1.0.0 first, then 1.1.0, on one host. The policy is `Validate`, so a host whose database is behind refuses to start
the module rather than migrate underneath the operator; the migration is applied by the tool.

Every block here can be pasted into an interactive zsh: there is no comment inside one (zsh runs a pasted `#` line as a command), so what a
command prints or means is said in the text around it.

First, release 1.0.0. The tool installs the feed's packages for the host and creates the database.

```bash
HOST=(--host artifacts/demo/hosts/a --environment Development --provider Sqlite --modules Samples.Notes)
bash tools/demo/pack.sh 1
bash tools/demo/run-host.sh a --port 5101 --prepare-only
bash tools/demo/elsa.sh persistence apply --restore "${HOST[@]}"
```

Start the host in its own terminal, and use it from this one. Release 1.0.0 has no `with-tags` endpoint, so the last request is a 404.

```bash
bash tools/demo/run-host.sh a --port 5101
```

```bash
curl -X POST localhost:5101/demo/notes -H 'content-type: application/json' -d '{"text":"hello"}'
curl localhost:5101/demo/notes
curl -i localhost:5101/demo/notes/with-tags
```

Second, stop the host, pack release 1.1.0 into the same feed, and start it again. The host installs 1.1.0 and refuses the shell, saying why:
`NotesSqliteDbContext has pending migrations: <id>_AddTags. Apply them out of process (dotnet elsa persistence apply) ...`.

```bash
bash tools/demo/pack.sh 2 --no-host
bash tools/demo/run-host.sh a --port 5101
```

The request is a 500, and `/health/ready` is 503:

```bash
curl -i localhost:5101/demo/notes
```

Third, apply the migration (`AddTags`). The next request activates the shell, and `with-tags` answers 200 within seconds, with `"tags": []` on the old notes.
Replace `NOTE_ID` with the id of a note that `GET /demo/notes` printed.

```bash
bash tools/demo/elsa.sh persistence apply "${HOST[@]}"
curl localhost:5101/demo/notes
curl localhost:5101/demo/notes/with-tags
curl -X POST localhost:5101/demo/notes/NOTE_ID/tags -H 'content-type: application/json' -d '{"tags":["demo"]}'
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

First, a database. Cached images only: `postgres:16-alpine`.

```bash
docker run -d --name elsa-demo-pg -e POSTGRES_PASSWORD=demo -e POSTGRES_DB=elsa -p 127.0.0.1:55432:5432 postgres:16-alpine
export ELSA_EF_CONNECTION="Host=127.0.0.1;Port=55432;Database=elsa;Username=postgres;Password=demo"
MODULES=Samples.Notes,Cluster.Membership
DB=(--environment Development --provider PostgreSql --modules $MODULES)
```

Second, both hosts on release 1.0.0, each with its own feed. The `apply` creates both modules' tables.

```bash
bash tools/demo/pack.sh 1 --host a --host b
bash tools/demo/run-host.sh a --port 5101 --provider PostgreSql --cluster host-a --fast-membership --prepare-only
bash tools/demo/elsa.sh persistence apply --restore --host artifacts/demo/hosts/a "${DB[@]}"
```

Start host a in terminal 1, wait until it is ready, then host b in terminal 2:

```bash
bash tools/demo/run-host.sh a --port 5101 --provider PostgreSql --cluster host-a --fast-membership
```

```bash
bash tools/demo/run-host.sh b --port 5102 --provider PostgreSql --cluster host-b --fast-membership
```

The membership table lists host-a and host-b, both Active. A note written on one host is listed from either: one database.

```bash
docker exec elsa-demo-pg psql -U postgres elsa -c 'select "HostId","Status" from elsa_cluster_members'
curl -X POST localhost:5101/demo/notes -H 'content-type: application/json' -d '{"text":"written on a"}'
curl -X POST localhost:5102/demo/notes -H 'content-type: application/json' -d '{"text":"written on b"}'
curl localhost:5101/demo/notes
```

Third, host b moves to release 1.1.0 while host a stays on 1.0.0. Stop host b (Ctrl-C), then pack and start it again. It is refused: `AddTags` is pending.

```bash
bash tools/demo/pack.sh 2 --host b
bash tools/demo/run-host.sh b --port 5102 --provider PostgreSql --cluster host-b --fast-membership
```

Apply `AddTags`. The next request activates b's shell (200), and `with-tags` on b is 409 `schema-version-not-finalized` because host a reads only 1.0.0. A write to host a still answers 200.
The status ends with `2.0.0: pending, held by nothing; waits for every counted member to read it`.

```bash
bash tools/demo/elsa.sh persistence apply --host artifacts/demo/hosts/b "${DB[@]}"
curl localhost:5102/demo/notes
curl -i localhost:5102/demo/notes/with-tags
curl -X POST localhost:5101/demo/notes -H 'content-type: application/json' -d '{"text":"a, still 1.0.0"}'
bash tools/demo/elsa.sh persistence status --host artifacts/demo/hosts/b "${DB[@]}" --family SamplesNotes
```

Fourth, host a follows. Stop host a (Ctrl-C), then pack and start it again. With host a gone `with-tags` answers 200 on both hosts, and the old notes have `"tags": []`.
The status says `finalized at 2.0.0`, and `complete from 2.0.0` once the backfill has rewritten the old rows (see the timings below).

```bash
bash tools/demo/pack.sh 2 --host a --no-host
bash tools/demo/run-host.sh a --port 5101 --provider PostgreSql --cluster host-a --fast-membership
```

```bash
curl localhost:5101/demo/notes/with-tags
curl localhost:5102/demo/notes/with-tags
bash tools/demo/elsa.sh persistence status --host artifacts/demo/hosts/a "${DB[@]}" --family SamplesNotes
```

When done:

```bash
docker rm -f elsa-demo-pg
```

Timings on a laptop, to plan the pauses: `pack.sh` 5 to 15 s once the host is built (up to a minute or two while other builds
compete for the machine, so pack before you present); a host is ready 10 to 15 s after it starts; `persistence apply` about 1 s
(the first `elsa.sh` call builds the tool and takes 25 s); the shell of a host that was refused activates on the first
request after the apply. `2.0.0` is finalized as soon as the last 1.0.0 host has left the fleet, not when it comes back on
1.1.0: about 0 to 3 s after host a is stopped cleanly (host b is then the only member and reads `2.0.0`, so its `/with-tags`
answers 200 before host a is back), and about 11 s after a crash, which is the expiry plus the skew allowance less what the last
heartbeat had already used. `complete from 2.0.0` follows the finalization by 16 to 20 s: the backfill waits for the settle
margin (the membership expiry plus the skew allowance, 12 s here) and then rewrites the old rows and records completion in its
next rounds, which run every 5 s. Measured over four two-host runs on PostgreSQL, with host a stopped cleanly in three and killed
in one: 16.5, 19.7, 20.0 and 20.3 s. Repacking and restarting host a takes longer than that on a busy machine, so by the time
it is ready the status usually says `complete from 2.0.0` already; before then it says `complete from 1.0.0`.

### Keeping 2.0.0 dormant on purpose

A hold keeps a version from finalizing until an operator releases it: a canary. Place it before the host runs 1.1.0.

Hold the family, start the host, and ask for `with-tags`. The answer is 409: "It is held by an operator: version '2.0.0' of schema family 'SamplesNotes' ...".
Writing a note still works: the row is stamped 1.0.0 with no tags.

```bash
bash tools/demo/elsa.sh persistence hold "${HOST[@]}" --family SamplesNotes --version 2.0.0 --reason "canary" --operator me
bash tools/demo/run-host.sh a --port 5101
```

```bash
curl -i localhost:5101/demo/notes/with-tags
bash tools/demo/elsa.sh persistence status "${HOST[@]}" --family SamplesNotes
```

Release the hold. `with-tags` answers 200 at the next evaluation (2 seconds here), with no restart.

```bash
bash tools/demo/elsa.sh persistence release "${HOST[@]}" --family SamplesNotes --version 2.0.0 --operator me
curl localhost:5101/demo/notes/with-tags
```

## Known limitations

- `dotnet elsa persistence` reads the packages a host has installed, so a host must have reconciled its feed at least once
  (or the tool must be run with `--restore`) before the tool can see the module.
- Start the two hosts one after the other, not in the same instant: when both create the membership database's identity row
  at once, the loser logs a duplicate-key error on that insert at startup and carries on.
- The walkthroughs below stop, pack and start a host. Since in-place upgrades work, that is the fallback: dropping 1.1.0 into a
  running host's feed installs it, the host refuses to switch while `AddTags` is pending (`/reload` answers 409 naming the module
  and the `persistence apply` command), and after the apply `/reload` answers 200. `tools/demo/RUNBOOK.md` shows that route.

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
