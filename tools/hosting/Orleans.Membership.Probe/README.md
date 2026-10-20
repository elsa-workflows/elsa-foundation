# Orleans membership and report-facet probe

This is a non-production experiment for Foundation issue #3098. It starts two Orleans silos in one OS process, configures both with Orleans 10.4.0 ADO.NET PostgreSQL clustering, and resolves the public `IMembershipTable` from each silo. Both must read the same two active membership identities. For each host, a fresh membership-table read is combined with one parameterized query for every report associated with those identities; a missing row or unsupported report schema yields unknown state with no capabilities, and query errors abort the complete projection.

The container is owned by the probe and is disposed on success or failure. PostgreSQL is pinned to major version 16. No external database, Elsa runtime interface, heartbeat provider, or production registration is involved.

Run from the repository root with Docker available:

```sh
dotnet restore tools/hosting/Orleans.Membership.Probe/Orleans.Membership.Probe.csproj --locked-mode
dotnet run --project tools/hosting/Orleans.Membership.Probe/Orleans.Membership.Probe.csproj --no-restore -- --output /tmp/orleans-membership-probe.json
```

A successful run exits 0 and emits the same structured result to stdout and the caller-selected JSON path. Assertions use stable `FailureReason` values and a failed run exits nonzero. Optional mutation runs should fail and exit nonzero:

```sh
dotnet run --project tools/hosting/Orleans.Membership.Probe/Orleans.Membership.Probe.csproj --no-restore -- --mutation hide-report-read
dotnet run --project tools/hosting/Orleans.Membership.Probe/Orleans.Membership.Probe.csproj --no-restore -- --mutation ignore-incarnation
```

The default path asserts that a missing report stays unknown, an actual PostgreSQL `REPEATABLE READ` transaction retains revision 1 while two later host reads see committed revision 2 with its removed capability, and a stale revision write is rejected. It then stops silo A and rejoins the same stable silo name and endpoint, checks the provider-generated Orleans generation changed, verifies the new key is unknown, publishes a new report visible from silo B, and confirms a late old-incarnation write remains isolated. It marks an unsupported schema version unknown and renames the report table to prove a failed query returns no partial projection, restoring the table before cleanup. The JSON receipt includes the active silo names, full identities and generations; Orleans, PostgreSQL and schema-script versions; mutation mode; and cleanup outcome. It contains no database connection string.

Optional mutation `hide-report-read` replays the initial unknown report snapshot at the later read to model loss of authoritative report refresh. `ignore-incarnation` strips the generation from report storage and lookup keys to model an incomplete identity key. Both must fail with a stable assertion reason.

[Root verification receipts](evidence/20261020-root.json) retain one baseline, both intended failures and restoration, with source/binary hashes and the observed backend image. They are pre-commit file-bound results, not a claim that their base commit already contained this probe.

## Script provenance

The bundled PostgreSQL schema scripts are copied from the Orleans source revision pinned for this experiment, so a run does not fetch schema files from the network:

- [PostgreSQL main schema](https://github.com/dotnet/orleans/blob/d114d7522a53b8cce4c6b97d44293e5c7273d766/src/AdoNet/Shared/PostgreSQL-Main.sql), SHA-256 `623c15320e640cdd3e84d0668dcae481312f2631ac4977bf2eb8cf22e05deb8b`.
- [PostgreSQL clustering schema](https://github.com/dotnet/orleans/blob/d114d7522a53b8cce4c6b97d44293e5c7273d766/src/AdoNet/Orleans.Clustering.AdoNet/PostgreSQL-Clustering.sql), SHA-256 `39dff7cbb72a390223cd48a9bc7d4a90252092dcba5677318bd510cc9fc1af08`.
- Both are distributed under the upstream [MIT license](https://github.com/dotnet/orleans/blob/d114d7522a53b8cce4c6b97d44293e5c7273d766/LICENSE), included as `scripts/LICENSE-Orleans.txt`.

The probe verifies the loaded scripts against these hashes before executing them as PostgreSQL multi-statement commands through Npgsql; the probe applies the main schema before the clustering schema.

## Limits

This checks that both configured Orleans ADO.NET membership providers return the two expected active identities through the same PostgreSQL backend used by the report facet, and observes one actual same-name restart with a new Orleans generation. It does not establish production adoption or an Elsa provider implementation. Two silos in one process do not prove multi-process or multi-machine behavior. This restart does not prove duplicate-host policy, lapse semantics, or recovery. A held old transaction remains stale after a later commit; each subsequent independent host projection sees the committed revision. Commit fencing and durable work remain independent concerns.
