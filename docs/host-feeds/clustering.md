# Running several hosts as a cluster

**Part of:** [Elsa.Foundation.Host package feeds](../foundation-host-feeds.md).

Several hosts that share one database must know about each other, so that a database schema change is only finalized once **every** host can read it. This page shows how to configure that, how to roll out a new module version, and how to see which host is holding a version back.

> **Good to know:** with nothing configured, each host is a cluster of one, and **nothing detects** the misconfiguration of several hosts sharing a database without cluster membership.

## Why a cluster must be declared

With nothing configured each `Elsa.Foundation.Host` is a cluster of one: the in-process membership default, which writes
nothing and counts only itself (spec 183, FR-017, FR-018). Several hosts that share one database must be configured as a
cluster, or each will finalize a feed-loaded EF module's new schema version as soon as it alone can read it, however many
hosts on the older release still write the same rows (spec 183, FR-018a). Nothing detects that misconfiguration.

A cluster is declared by enabling the durable EF membership provider on every host, with the same membership store and a
host id of each host's own ([#2151](https://github.com/elsa-workflows/elsa-foundation/issues/2151); ADR 0078; spec 183,
FR-024):

```bash
Elsa__Cluster__Membership__HostId=foundation-host-a            # distinct per host, stable across its restarts
Elsa__Cluster__Membership__EntityFrameworkCore__Enabled=true
Elsa__Cluster__Membership__EntityFrameworkCore__Provider=PostgreSql
Elsa__Cluster__Membership__EntityFrameworkCore__ConnectionString="Host=db;Database=elsa;Username=elsa;Password=…"
```

- **The keys.** Under `Elsa:Cluster:Membership`: `HostId`, `HeartbeatInterval` (default 10 s), `ExpiryPeriod` (30 s) and
  `SkewAllowance` (5 s); under its `EntityFrameworkCore` subsection: `Enabled`, `Provider` (`Sqlite`, `SqlServer`,
  `PostgreSql` or `MySql`; default `Sqlite`), `ConnectionString`, or `ConnectionName` naming an entry under
  `ConnectionStrings` (with `ConnectionStrings:Elsa` as the fallback), `Schema`, `Pooling` and `CleanupPeriod` (10 min;
  it must exceed the expiry period plus the skew allowance). `ClusterMembershipConfigurationExtensions` in
  `Essentials/src/Cluster/EntityFrameworkCore` is the reference.
- **The host id.** It is required once the provider is enabled: a host without one refuses to start, naming
  `Elsa:Cluster:Membership:HostId` (FR-003a). Give each host its own and keep it across restarts; two live processes
  under one id are refused rather than allowed to displace each other (FR-004b). A host restarted after a crash under
  the same id waits until its earlier incarnation's entry expires, at most the expiry period plus the skew allowance,
  before it joins and becomes ready.
- **The shared database.** Every host's membership connection must reach the same primary, never a read replica
  (FR-032). It is normally the database the EF modules' own features use, so each finalization record and the fleet that
  decides it live side by side. SQLite serves several processes on one machine only, which suits development and tests
  (FR-033); use PostgreSQL, SQL Server or MySQL across machines.
- **Locks.** A lock excludes only the processes that reach the same lock store, so a cluster composes
  `DatabaseDistributedLocking` (`Elsa.Locking.Database`), which holds its locks in the shared PostgreSQL, SQL Server or
  MySQL database (#2192). A shell that composes `FileSystemDistributedLocking` on its default, node-local folder refuses to
  start once the durable provider is enabled, naming both ways out: the database lock, or a `LocksFolderPath` every host
  shares. SQLite has no database lock, so a SQLite composition keeps the file-system lock and is single-node by
  definition: several processes on that machine share one explicit `LocksFolderPath`. What a lock guarantees to a
  `[SingleNodeTask]` is one at a time, at shell start, and no more; see `Essentials/src/Tasks/Elsa.Tasks/EXTENSION_POINTS.md`.
  The git workflow export takes no lock at all: every Writer node exports, and the fast-forward-only push keeps one
  writer per branch (#2197; see the section "Several Writer nodes of one catalog" in
  `Essentials/src/Workflows/Design/Reconciliation/Git/README.md`).
- **Migrations.** The membership table is an EF module of its own (`Cluster.Membership`). Under the default policy the
  first host to start creates it; under `Elsa:Persistence:EntityFramework:Migrate:Policy=Validate` a host refuses to start
  until `dotnet elsa persistence apply` has created it, like any other module.
- **Half a configuration is refused.** Settings under `EntityFrameworkCore` without `Enabled` stop the host at startup,
  naming the key, because a host that meant to join a cluster and silently stayed a cluster of one is exactly the failure
  that looks like success. `Enabled=false` is the explicit way to stay alone.
- **The engine selection is separate.** `Nuplane:Capabilities:ef-provider` still has to be set for the feed-loaded EF
  modules, which Nuplane refuses without it ([Selecting the EF provider engine](pinning-and-closures.md#selecting-the-ef-provider-engine)); each
  module's own `Provider` feature setting chooses the engine it binds, and membership's `Provider` the one its table
  uses. They are normally all the same.
- **Share the Data Protection key ring too.** Membership does not share it. A feed-loaded feature that signs users in
  or checks antiforgery tokens protects them with the host's key ring, so hosts behind one load balancer refuse each
  other's cookies unless every host sets `Elsa:DataProtection:EntityFrameworkCore:Enabled=true` (with its `Provider`,
  and the same database), and a certificate under `Elsa:DataProtection:Certificate` to encrypt the keys at rest. A
  clustered host without it logs a warning as it starts and still starts; a cluster whose features sign nobody in has
  nothing to share. See [Data Protection key ring](../reference/identity-configuration.md#data-protection-key-ring).

**Rolling a new module version out.** Install the new release of an EF module on one host and it reads the new schema
version, but the version is finalized only once every live host can read it (spec 181). Until then that host writes the
old version, and a feature that needs the new one answers `409` with code `schema-version-not-finalized`, saying it
becomes available once every host can read the version (spec 182); the gate's status names each host that cannot yet
(spec 181, FR-022; `dotnet elsa persistence status` prints it, below). Upgrade the other hosts to the new release, in place or by restarting each on it. In place, drop the
new package into each host's feed: the host reconciles, reloads its shells onto it, and stops counting the previous
release once nothing on it can run that release any more, although the release stays loaded in the load context
Nuplane gave it, which is never unloaded (spec 183, FR-021, amended 2026-09-29; see [Cluster membership and EF module packages](pinning-and-closures.md#cluster-membership-and-ef-module-packages)). A host reports only the
versions every declaration it has not retired reads, so until then it goes on counting as unable to read the new
version. Once the last host is on the new release, the version finalizes on its own and the feature serves on every
host, with no restart of the hosts that already had it. A host that is stopped leaves the fleet; one that crashes is still counted until its
entry expires.

**Seeing which host holds a version back.** One command, from any machine that can reach the shared database, shows the
family's finalization state, names each host that cannot read a pending version, and lists the cluster's members:

```bash
dotnet elsa persistence status --host /app --provider PostgreSql --modules SamplesNotes,Cluster.Membership \
  --family SamplesNotes --connection-env ELSA_EF_CONNECTION
```

```text
SamplesNotes (SamplesNotes): finalized at 1.0.0; this host reads [1.0.0, 2.0.0]
  2.0.0: pending, held by nothing; waits for every counted member to read it
    waits for: foundation-host-a (reads 1.0.0)

members: 2 in Cluster.Membership, judged at 2026-09-29T12:00:00Z with a skew allowance of 00:00:05
  foundation-host-a: Active, live, last heartbeat 2026-09-29T11:59:58Z
    SamplesNotes: reads 1.0.0
  foundation-host-b: Active, live, last heartbeat 2026-09-29T11:59:58Z
    SamplesNotes: reads 1.0.0, 2.0.0
```

Each `waits for` line is a live host that counts toward the version and does not report reading it, with the versions it
does read; a host whose report the tool cannot interpret is named as reading nothing, which blocks the version just as it
blocks the gate. When no member counted here blocks a pending version the line says exactly that, and no more: it is what
this reading of the table shows, not a promise about the next evaluation. The members list every row of the membership
table, so a host that left (`Left`, not counted) or stopped heartbeating (`expired`, not counted) is visible until the
cleanup period removes it. A live member that reports nothing about a family says `reports nothing, so it is not counted
for it` under that family, and one whose row this build cannot interpret shows the status `unknown`, since it did not
state one this tool reads. `--modules` need not name `Cluster.Membership`: the members are read through the host's own
closure whenever it carries the membership module, on the connection the command uses. Three things to know:

- **Liveness is judged on the machine running the command, with a skew allowance the command prints** (`with a skew
  allowance of 00:00:05` above). It is the `--skew-allowance` you give, else the host's `Elsa:Cluster:Membership:SkewAllowance`
  from its `appsettings.json` and the `appsettings.<environment>.json` overlay of `--environment`, else 5 s. Hosts started with
  `--fast-membership` (2 s) or the setting in their process environment (`Elsa__Cluster__Membership__SkewAllowance`) are not
  visible to the command, so pass `--skew-allowance 00:00:02`: with the default, a host killed a moment ago still reads as
  live and blocks the version for three seconds after the hosts themselves count it expired.
- A host with no membership provider in its closure prints `members: this host only (this host's closure carries no cluster
  membership provider)`; one whose database has not had the `Cluster.Membership` migrations applied prints that the table is
  missing, a cluster of one. Pending versions then keep the plain "waits for every counted member" line, since nothing was
  read to name anyone. A host whose tooling predates this reading prints `members: cluster membership not reported by this
  host's tooling`, which says nothing about whether it runs alone.
- With `--skew-allowance` the command needs a host whose `Elsa.Persistence.EntityFramework` is the release beside it; an
  older host's tooling lists no members and judges no liveness, so the option is simply not sent to it.

The running host reports the same blockers itself: a feature that needs the pending version answers `409` with
`schema-version-not-finalized`, and the Modularity API's Attention items carry the reason (spec 182). There is no separate
status endpoint on `Elsa.Foundation.Host`; the CLI is the operator's view before and beside a running host.

`FoundationHostClusterBootTests` (`Essentials/tests/Cluster/EntityFrameworkCore/Tests`) runs this whole sequence on two
built hosts over one SQLite database, and `PostgreSqlFoundationHostClusterTests` (`.../ProviderTests`, in a container) on
one PostgreSQL database.

---

All pages: [Overview](../foundation-host-feeds.md) · [Point the host at a feed](feeds.md) · [Pinning and closures](pinning-and-closures.md) · [Updating packages](updating-packages.md) · [When things fail](failure-modes.md) · [Clustering](clustering.md)
