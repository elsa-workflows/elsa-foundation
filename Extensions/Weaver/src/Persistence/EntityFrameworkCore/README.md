# Agent EF session record store

`Elsa.Weaver.Persistence.EntityFrameworkCore` is the opt-in durable backing for Weaver sessions
([#3441](https://github.com/elsa-workflows/elsa-foundation/issues/3441),
[spec 1189](../../../../../specs/1189-weaver-recovery/spec.md)). It implements the Preview
`IAgentSessionRecordStore` and replaces the audit sink, reader and ownership together. The default
session owner is not replaced: it writes through this store and hydrates from it, so session policy,
autonomy and durable recovery stay supported. Replacing a lifecycle service (session, streaming, invoker or
proposal) or `IAgentTurnStateStore` while this store is selected fails shell activation
(`AgentDurableRecoveryValidator`), because such a composition cannot recover what it records. The package carries EF Core and Relational APIs; the
host supplies its selected engine. Enable shell feature `WeaverEntityFrameworkCore` (it
depends on `Weaver` and `Secrets`), or register directly:

```csharp
services.AddAgentEntityFrameworkCore(new()
{
    Provider = "Sqlite",
    ConnectionString = "Data Source=agent.db"
});
```

SQLite, SQL Server, Npgsql PostgreSQL and Oracle MySQL have separate context/migration sets under
`Migrations/Agent/<Provider>/`. The module uses the shared EF startup policy, named connections,
schema selection and pooling. The stable history module is `ElsaAgent`.

## Selection

The store and the audit trio are chosen through `SelectAgentSessionRecordStore` and
`SelectAgentAuditStore` (`Elsa.Weaver.Extensions.AgentPersistenceSelection`). A selection replaces
only the process-local default, whichever of the two registrations runs first. A second selection,
or a selection over a store registered some other way, throws at registration; the
`AgentRegistrationValidator` still refuses two record stores or two of any audit facet at shell
activation.

An application can instead call `modelBuilder.AddAgentModel(Database.ProviderName!)` in its own
`DbContext`, then `services.AddAgentStores<ApplicationDbContext>()`. Its host owns migrations.
Every operation resolves a fresh scoped context and commits independently; it never flushes pending
application changes or enlists in an application transaction.

## Tables

| Table | Holds |
| --- | --- |
| `elsa_weaver_sessions` | Owner columns (actor, tenant, workspace), list columns (title, status, update and expiry time), policy revision, `ContinuedFrom`, the reached session limit, owner token, fence, lease and version; the protected session, paused turn and the audit records a recovery settlement still owes |
| `elsa_weaver_messages` | One protected `AgentMessageRecord` per message, with role, status and status reason |
| `elsa_weaver_disclosure_snapshots` | Each distinct snapshot once per session: digest, size and the protected canonical text |
| `elsa_weaver_context` | Each context attachment once per session, protected, keyed by the SHA-256 of its identity |
| `elsa_weaver_proposals` | One protected `AgentProposalRecord` per proposal, with status, prepared deadline and its record version |
| `elsa_weaver_call_keys` | The call, invocation, turn-mutation and call-input keys, keyed by SHA-256 of the key |
| `elsa_weaver_audit`, `elsa_weaver_audit_owners` | Protected audit records and the first-wins owner association that scopes audit queries |
| `elsa_weaver_audit_sequence` | One counter row that allocates audit append positions |

Messages, snapshots, context, proposals and keys keep their first-insertion order. Owner and identity
columns compare and order ordinally on every provider. Owner and workspace filters are exact: SQL Server's
`Latin1_General_100_BIN2` compares with PAD SPACE (and `LEN` ignores trailing spaces), so those filters also
compare each side with a fixed suffix appended, and an actor, tenant or workspace with a trailing space is a
different one.

An audit append bumps the counter row with a locking update inside its transaction and takes the new value
as its position, so concurrent appends across the deployment (for example, many sessions recovering after a
restart) wait on that row instead of colliding on the next position. The order stays distinct and gap-free,
because a rolled-back append rolls its position back too.

## Ownership, fence and lease

Every write first moves the session row's version under the writer's owner token and fence. That
conditional update takes the aggregate's row lock until commit, so competing writers of one session wait
for each other instead of racing, and the key and proposal checks that follow cannot change before the
write commits. Ownership acquisition takes the same lock before it decides. A transient provider conflict
or a racing insert of the same proposal identity by another session retries the whole operation through
the shared bounded `EfWriteRetry`. A stale owner or fence returns `OwnershipLost`,
a key recorded with another value `KeyConflict`, a proposal of another session `AlreadyExists`, and a
proposal record that moved since its writer read it `VersionConflict`; none changes anything. The same
write extends the lease, or re-acquires a lapsed lease nobody took over. Acquisition takes ownership
when the owner is this process, the lease has lapsed or was released; a takeover increments the fence,
and another process's live lease answers `OwnedElsewhere` with its expiry. Lease time is database
time (each provider's own UTC clock), so hosts share one clock. The lease is 30 seconds by default
(`LeaseSeconds`, 15 to 86400), longer than the owner's 10-second renewal.

`FindOwnerAsync` and the owner list read only plain columns; nothing is decoded before authorization.
The owner list filters actor and tenant, then workspace, then orders by update time and session id
for its cursor before the limit.

## Protection and unreadable state

Every payload column holds ciphertext from the selected `ISecretValueProtector`, bound to its purpose,
session and row identity, so ciphertext moved to another row does not authenticate. Retain the Secrets
keys across restarts. Snapshot text is returned byte-identical, so recorded digests and fingerprints
re-verify after any number of restarts. Credentials, provider SDK objects and cancellation sources are
never part of a record; the store records only the owner's Core records.

A row whose ciphertext does not authenticate, whose JSON does not decode, whose plain columns disagree
with its protected record, or whose schema version this build does not read makes hydration fail with
`AgentSessionUnavailableException` (`agent.session.unavailable_state`, reason `unreadable`, the cause
kept as the inner exception); nothing is settled or changed. A provider failure leaves the store as an
`AgentContributionException` naming the session and operation, with the original exception inside.

## Tests

`Extensions/Weaver/tests/Persistence/Tests` covers the store on SQLite with the actual migrations,
including the Agent restart, deduplication, execution and ownership scenarios run over this store.
`Extensions/Weaver/tests/Persistence/ProviderTests` applies the migrations on PostgreSQL 16, SQL Server
2022 and MySQL 8.4 through Testcontainers and exercises round-trips, fencing and competing writers. It
skips without Docker unless `ELSA_REQUIRE_NATIVE_PROVIDER_MATRIX=1`.

See [extension points](EXTENSION_POINTS.md). Regenerate the pre-GA Initial migrations with
`bash tools/ef/generate-module-migrations.sh 'Agent.*DbContext'`; temporarily remove the generated
migration API entries while their types are absent, then restore the API baseline and build normally.
