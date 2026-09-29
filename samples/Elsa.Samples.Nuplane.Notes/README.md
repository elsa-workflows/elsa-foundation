# Elsa.Samples.Nuplane.Notes

A small EF module for the schema-rollout demo: it is loaded from a feed by `Elsa.Foundation.Host`, it has one table, and it
comes in two releases built from one project. The point is not notes. It is what an operator sees when a release changes the
database while other releases of the module are still running against it.

## The module

Notes. `POST /demo/notes` adds one, `GET /demo/notes` lists them. Everything lives in one table, `elsa_samples_notes`, of
one schema family, `SamplesNotes`, owned by one EF module, `Samples.Notes` (history table
`__EFMigrationsHistory_ElsaSamplesNotes`). It supports Sqlite and PostgreSql: set `Provider` and `ConnectionString` on the
`NotesEntityFrameworkCore` feature, and select the same engine with `Nuplane:Capabilities:ef-provider`. The package's
`nuplane.json` is `HostIntegrated` and declares that capability, as every EF module package does, so the host acquires
the engine you select and nothing else.

| | release 1.0.0 (`DemoVersion=1`) | release 1.1.0 (`DemoVersion=2`) |
|---|---|---|
| Table | `Id`, `Text`, `CreatedAt`, `SchemaVersion` | the same, plus `TagsJson`, a nullable text column |
| Schema family version | `1.0.0` | `2.0.0`, reading `1.0.0` through one upcaster |
| Migrations | `Initial`: the table and the finalization-record tables | `Initial`, and `AddTags`: one `AddColumn`, nullable |
| Features | `NotesEntityFrameworkCore`, `Notes` | those, and `NotesWithTags` |
| Endpoints | `POST` and `GET /demo/notes` | those, and `GET /demo/notes/with-tags`, `POST /demo/notes/{id}/tags` |
| Upcaster | none | `NotesOneToTwo`: a row with no tags column reads as `[]` |

`NotesWithTags` declares `[RequiresSchemaVersion("SamplesNotes", "2.0.0")]`. It is composed and its endpoints are mapped, but
what they serve asks the shared dormancy check first, and until this host observes `2.0.0` as finalized they answer
**409** with the reason (`schema-version-not-finalized`, and why: not every host reads it yet, or an operator holds it).
Release 1.1.0's base endpoints keep working meanwhile: the store writes the version the host's finalization gate says it
may write, which is still `1.0.0`, so the rows it adds stay readable by every host on 1.0.0, with `TagsJson` left null.

## What is where

| | |
|---|---|
| `NotesModule.cs`, `AssemblyInfo.cs` | the names; `[EfModule]`, `[EfSchemaFamily]` and, from 1.1.0, `[EfSchemaContent]` for `TagsJson` |
| `NotesDbContext.cs`, `NoteRecord.cs` | the model, with `MapSchemaFinalization` so the baseline creates the finalization tables |
| `NoteStore.cs` | writes at the gate's write version, and checks every row's stamp on read |
| `NotesEntityFrameworkCoreFeature.cs`, `NotesFeature.cs` | the persistence feature (`[UsesEfModule]`) and the base endpoints |
| `V2/` | compiled into release 1.1.0 only: `NotesWithTagsFeature`, `NoteStore.Tags.cs`, `NotesOneToTwo`, and `V2/Migrations` (`AddTags` and the version-2 model snapshot) |
| `Migrations/` | `Initial` and the version-1 model snapshot, per provider; generated, never hand-written |

The few lines that differ in a shared file sit behind `#if DEMO_V2`. There is no backfill rewriter: spec 186's post-finalization
backfill is not on this commit, and `NotesWithTags` does not ask for completeness (`RequiresCompleteness`), because it reads
a 1.0.0 row through the upcaster.

## Build, pack, feed layout

```bash
bash tools/demo/pack.sh 1     # release 1.0.0
bash tools/demo/pack.sh 2     # release 1.1.0, into the same feed
```

`pack.sh` builds `Elsa.Foundation.Host` (skip with `--no-host`), then runs, from the checked-out commit:

```bash
dotnet pack samples/Elsa.Samples.Nuplane.Notes/Elsa.Samples.Nuplane.Notes.csproj -c Release -p:DemoVersion=<1|2> -o artifacts/demo/feed
dotnet pack src/essentials/Persistence/EntityFramework/Elsa.Persistence.EntityFramework.csproj -c Release -p:IsPackable=true -o artifacts/demo/feed   # once
```

and fills `artifacts/demo/closure` with the `.nupkg` files of EF Core, the two engines and everything they need, copied from the
NuGet package cache, as `tests/essentials/Cluster/EntityFrameworkCore/Tests/FoundationHostFeed.cs` does. Both are **flat**
folders of `<id>.<version>.nupkg`: `dotnet pack -o` writes that, `dotnet nuget push` writes a `<id>/<version>/` tree the
host's directory feed cannot read.

```
artifacts/demo/feed/      the host's own feed: watched, so a package dropped here is installed
  Elsa.Persistence.EntityFramework.4.0.0-dev.nupkg
  Elsa.Samples.Nuplane.Notes.1.0.0.nupkg
  Elsa.Samples.Nuplane.Notes.1.1.0.nupkg
artifacts/demo/closure/   resolve-only: EF Core, Microsoft.EntityFrameworkCore.Sqlite, Npgsql.EntityFrameworkCore.PostgreSQL, ...
```

The package version is not a `-p:Version`: the project derives 1.0.0 and 1.1.0 from `DemoVersion`, because a global `Version`
would also be the version asked of the Elsa projects it depends on.

## Running the demo on Elsa.Foundation.Host

`tools/demo/run-host.sh NAME` makes a directory `artifacts/demo/hosts/NAME` holding a copy of the host's build output, an
`appsettings.Development.json` naming the feeds, the engine and `Migrate:Policy=Validate`, and a `shells.json` enabling the
three Notes features, then starts it. `tools/demo/elsa.sh` is `dotnet elsa`, built from this checkout. The connection travels
in `ELSA_EF_CONNECTION`, never as an argument. Sqlite below; PostgreSql is the same with `--provider PostgreSql --connection ...`
(and it was run against `postgres:16-alpine`).

```bash
export ELSA_EF_CONNECTION="Data Source=$PWD/artifacts/demo/notes.db;Pooling=False"
HOST=(--host artifacts/demo/hosts/a --environment Development --provider Sqlite --modules Samples.Notes)

# 1. release 1.0.0 in the feed; the database is created by the tool, out of process, because the policy is Validate
bash tools/demo/pack.sh 1
bash tools/demo/run-host.sh a --port 5101 --prepare-only
bash tools/demo/elsa.sh persistence apply --restore "${HOST[@]}"      # installs the feed's packages for the host, then applies Initial
bash tools/demo/run-host.sh a --port 5101                             # in another terminal
curl -X POST localhost:5101/demo/notes -H 'content-type: application/json' -d '{"text":"hello"}'
curl localhost:5101/demo/notes
curl -i localhost:5101/demo/notes/with-tags                           # 404: release 1.0.0 has no such feature

# 2. release 1.1.0 into the same feed, and the host started on it
bash tools/demo/pack.sh 2 --no-host                                   # stop the host first, see the note below
bash tools/demo/run-host.sh a --port 5101
#    the host installs 1.1.0 and refuses to activate the shell, and says why:
#    NotesSqliteDbContext has pending migrations: <id>_AddTags. Apply them out of process (dotnet elsa persistence apply) ...
curl -i localhost:5101/demo/notes                                     # 500, and /health/ready is 503

# 3. apply, and it works
bash tools/demo/elsa.sh persistence apply "${HOST[@]}"                # reads the packages the host installed; applies AddTags
curl localhost:5101/demo/notes                                        # the next request activates the shell
curl localhost:5101/demo/notes/with-tags                              # 200 within seconds: 2.0.0 finalized, old rows read as tags: []
curl -X POST localhost:5101/demo/notes/<id>/tags -H 'content-type: application/json' -d '{"tags":["demo"]}'
```

**Installing 1.1.0 into a host that is already running 1.0.0 does not refuse, and does not finish** (observed on this commit).
The folder watcher reconciles and reloads the shell without a restart, the new features appear, and `Validate` passes with
`AddTags` still unapplied; `2.0.0` is then never finalized, so `NotesWithTags` stays dormant (409) until the host restarts. That
is consistent with the 1.0.0 assembly staying loaded in the host's default load context beside 1.1.0: the migrations lookup
is by assembly name, and the readability report intersects the readable versions of every loaded copy of a family. Stop the
host, install, start it: that is the sequence above, and the start is where `Validate` refuses. This is how a `HostIntegrated`
package loads today, not something the sample chooses.

### Keeping 2.0.0 dormant on purpose

A hold keeps a version from finalizing until an operator releases it: the canary. Place it before the host runs 1.1.0.

```bash
bash tools/demo/elsa.sh persistence hold    "${HOST[@]}" --family SamplesNotes --version 2.0.0 --reason "canary" --operator me
bash tools/demo/run-host.sh a --port 5101
curl -i localhost:5101/demo/notes/with-tags        # 409: "It is held by an operator: version '2.0.0' of schema family 'SamplesNotes' ..."
curl -X POST localhost:5101/demo/notes ...          # still works; the row is stamped 1.0.0 with TagsJson null
bash tools/demo/elsa.sh persistence status  "${HOST[@]}" --family SamplesNotes
bash tools/demo/elsa.sh persistence release "${HOST[@]}" --family SamplesNotes --version 2.0.0 --operator me
curl localhost:5101/demo/notes/with-tags           # 200 at the next evaluation (5 seconds here), no restart
```

### Two hosts sharing one database

`run-host.sh` takes any number of names, and two names given the same `--connection` are two hosts on one database, each with its own
directory, feed and Nuplane state (`--feed` per host, so one can stay on 1.0.0). **On this commit that does not show the
fleet rule**: `Elsa.Foundation.Host` composes only the in-process cluster membership, a cluster of one, so a host running 1.1.0
finalizes `2.0.0` at activation without knowing the other host is on 1.0.0, and that host then answers 409 to every write
(`it is finalized at '2.0.0', and this host reads only [1.0.0], so every write to the family is refused`). Finalizing only
once both hosts can read the version needs both hosts to join the durable EF membership (`Elsa:Cluster:Membership`), which
the Foundation host does not compose (see `docs/foundation-host-feeds.md`, "Cluster membership and EF module packages"). Use a hold, above,
for the dormancy half of the demo on the Foundation host.

## Regenerating the migrations

They are generated with dotnet-ef through a design-time startup project, `tools/demo/Elsa.Samples.Nuplane.Notes.Tooling`, which has
one factory line per provider on the shared `ModuleDesignTimeFactory` (the sample is deliberately not in
`tools/ef/Elsa.EntityFrameworkCore.Tooling`, which would put a demo module in every "all modules" command).
`bash tools/demo/generate-notes-migrations.sh initial` and `... tags` build the release each describes; `tags` diffs the
version-2 model against the committed version-1 snapshot, so it needs `initial` to have run. Do not run
`tools/ef/generate-module-migrations.sh` for this module.

## Tests

`tests/essentials/Samples/Nuplane/Notes/Tests`: builds both releases and checks what each declares (family version, chain, content
columns, migrations, feature requirement); runs the 1.1.0 `AddTags` migration through `ExpandOnlyMigrationGuard`, the public
form of spec 185's guard; and proves the 1.0.0 to 2.0.0 upcaster with spec 180's three FR-022 proofs over a committed fixture pair
(`Fixtures/SchemaUpcasters/SamplesNotes/1.0.0-to-2.0.0`, frozen in `tests/essentials/Architecture/Baselines/schema-upcaster-fixtures.sha256`).
