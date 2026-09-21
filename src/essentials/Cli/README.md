# dotnet-elsa

The `dotnet elsa` command-line tool: `list`, `plan`, `script`, `script-check`, `apply` and `validate`.
`post-migrate` is not in this build.

Designed by [spec 171](../../../specs/171-persistence-script-cli/spec.md) and
[ADR 0076](../../../docs/adr/0076-persistence-tooling-runs-inside-the-host-closure.md).

## Why there are two projects

`Elsa.Cli` references no EF Core package, no provider engine, and no Elsa persistence assembly, and
neither does `Elsa.Cli.Worker` beside it. A tool that shipped its own EF Core would almost certainly bind a
different EF Core — and a different provider engine — than the host it is scripting for, and would then
produce SQL that host will never run.

So the front end parses the command line and launches the worker inside the **target host's** own
dependency closure:

```
dotnet exec --runtimeconfig <host>.runtimeconfig.json --depsfile <host>.deps.json Elsa.Cli.Worker.dll
```

The worker resolves assemblies exactly as that host's own process would, reaches one frozen entry point
already inside it — `Tooling.EfToolingHost.RunAsync` in the host's `Elsa.Persistence.EntityFramework` —
reflectively, and exchanges versioned JSON with it over two streams. The request travels over **stdin**, not
in process arguments: arguments are world-readable, which is also why `apply` and `validate` take their
connection exclusively via `--connection-env`/`--connection-stdin`
([#1876](https://github.com/elsa-workflows/elsa-foundation/issues/1876)) and never as a flag value. With
`--connection-env NAME` (default `ELSA_EF_CONNECTION`), only the variable's *name* travels to the worker;
the value itself reaches it by ordinary environment inheritance from this process, and the worker reads it
from its own environment. With `--connection-stdin`, this process reads the value from its own stdin — a
stream distinct from the worker's, which is a fresh pipe this process opens for that child — and carries it
inside the request that already travels the worker's stdin.

For a host whose modules arrive as packages, the worker boots that host's own Nuplane loader rather than
resolving assemblies itself. It never reconciles and never downloads: a reconcile pass rewrites the host's
state file and can delete under its install root.

## Commands

| Command | What it does |
|---|---|
| `persistence list` | Names every EF module the host declares. With no selector, that is every one of them. |
| `persistence plan` | Reports the migrations each selected module would apply, `0 → head`, offline. |
| `persistence script` | Writes flat `NN-<slug>.sql` files plus one `migration-plan.json`. |
| `persistence script-check <dir>` | Regenerates that directory's artifact from its own plan and byte-compares. |
| `persistence apply` | Runs each selected module's compiled migrations against a database, through `EfDatabaseMigrator`/`DbContext.Database.MigrateAsync`. |
| `persistence validate` | Fails (exit 1) if any selected module has a pending migration against a database. Applies nothing. |

`post-migrate` is not in this build; asking for it is a usage error rather than a command that quietly does
nothing.

## Flags

| Flag | Rule |
|---|---|
| `--host <dir>` | Required. The host's published or built output directory. |
| `--packages <dir>` | Repeatable. A package root to resolve modules from; never populated by this tool. |
| `--provider` | Required for `plan`, `script`, `apply` and `validate`, and authoritative: no command substitutes another. |
| `--modules`, `--all`, `--from-host` | Exactly one, for `plan`, `script`, `apply` and `validate`. `--from-host` selects every module the host's own enabled shell features declare a dependency on. `list` defaults to every module; `script-check` takes none of them, because the committed plan is what it checks against. |
| `--shell <name>` | The shell whose features are read. With none given, every shell the host configures is read. |
| `--schema`, `--output` | `--output` is required for `script`. |
| `--environment <name>` | Which `shells.<name>.json` overlay is read, and what the manifest records. Default `Production` — ASP.NET Core's own default, never this tool's own `ASPNETCORE_ENVIRONMENT`. |
| `--idempotent` | Accepted and implied: every script is idempotent. |
| `--connection-env <NAME>` | `apply`/`validate` only. Default `ELSA_EF_CONNECTION`. Reads the connection from this process's own named environment variable. |
| `--connection-stdin` | `apply`/`validate` only. Reads the connection from this process's own stdin instead. |

There is no `--connection` flag (D7): giving one is a usage error, not a silently ignored value.

## Provider agreement

`--provider` is authoritative (D4). Whenever the host's shell configuration is found beside its output —
`shells.json` plus the `shells.<environment>.json` overlay when that file exists — the tool compares it
against `--provider` and exits 3 on any disagreement, under **any** selector and not only `--from-host`.

The comparison is **per feature, not per module**. `Workflows.Runtime` alone is backed by eight shell
features that each carry their own `Provider` while registering against the same context, so taking any one
of them for the module's would let a correctly configured feature mask a wrongly configured one. Every
offender is listed, by feature name, the selected module(s) it backs, and its configured value; agreement by
one feature of a module never speaks for another.

Which features are compared is decided by `[UsesEfModule("<module>")]` on the feature class:

- A feature that is not enabled in the shell is ignored.
- A feature that declares a `Provider` setting and leaves it unset is compared as `Sqlite`, which is that
  feature's own default.
- A feature that declares **no** `Provider` setting at all — `WorkflowsDashboardEntityFrameworkCoreFeature`,
  which reads two modules' contexts and registers migrations for neither — is skipped entirely. It is not
  defaulted to `Sqlite`: the provider of the contexts it reads is decided by the features that register
  their migrations, and those are the ones compared.

The manifest records `host.providerAgreement` as `checked` whenever that configuration was found, and
`not-checked` only when none was found at all. What the check cannot see is stated in the tool's own report
output rather than in the manifest: it reads those two files, not the process environment a running host
additionally consults, so a live host's effective provider can differ from the one verified here.

## Exit codes

| Code | Meaning |
|---|---|
| 0 | Success. |
| 1 | A negative result: `script-check` found a difference, or `validate` found a pending migration. |
| 2 | A usage error or a refusal, including `script --provider Sqlite` and `--connection`. |
| 3 | A resolution failure: the host, a module, a provider engine, or a package set. |
| 4 | A database failure from `apply` or `validate`. Nothing else in this build opens a database. |

## What `script-check` reports

A difference is exactly one of two kinds, never both:

- **SQL differs** — at least one `.sql` file's bytes differ from what regenerates. A file whose module
  gained a migration is reported as out of date; one whose migrations did not move is reported as edited,
  because a statement changed and someone has to read it. A `.sql` file the plan does not name is reported
  as an orphan.
- **manifest versions differ, SQL identical** — every statement is byte-identical and `efCoreVersion`,
  `engine.version` or a module's `package.version` moved, which a package upgrade alone explains:
  regenerate and commit, nothing new to apply.

Both exit 1.

## SQLite

`script` refuses SQLite outright (exit 2). EF cannot express an idempotent script for it — SQLite has no
conditional statement to wrap a migration in — and a plain script would sit in the same directory looking
identical to every other file while being unsafe to re-run.

## MySQL

A MySQL `.sql` file carries one `elsa_migrate_<module-slug>` stored procedure, which it creates, calls and
drops. MySQL allows `IF … THEN` only inside a routine, so an idempotent MySQL script has nowhere else to
put its per-migration guards; that is true of any correct idempotent MySQL script, not just this one. Two
consequences for whoever applies it:

- **`CREATE ROUTINE` and `ALTER ROUTINE` are required** on the target database, alongside the usual DDL
  rights — `ALTER ROUTINE` for the file's own leading `DROP PROCEDURE IF EXISTS`, which MySQL
  privilege-checks before it checks whether the routine exists, so a missing grant fails the file on its
  first statement (`ERROR 1370`) rather than part-way through. `EXECUTE` is needed too only on a server
  with `automatic_sp_privileges=0`. `apply` and `validate` need none of them — they run the compiled
  migrations through EF rather than the scripted file.
- **Apply it with a client that understands `DELIMITER`** (the `mysql` CLI, MySQL Workbench). `DELIMITER`
  is a client directive that is never sent to the server; a raw ADO client has to consume it itself.

The file carries no `START TRANSACTION`/`COMMIT`: MySQL commits implicitly on every DDL statement, so the
pair EF emits would promise an atomicity the server cannot deliver.
