# Agent EF session record store

`Elsa.Agent.Persistence.EntityFrameworkCore` is the opt-in durable backing for Weaver sessions
([#3441](https://github.com/elsa-workflows/elsa-foundation/issues/3441),
[spec 1189](../../../../../specs/1189-weaver-recovery/spec.md)). It implements the Preview
`IAgentSessionRecordStore` and replaces the audit sink, reader and ownership together. The default
session owner is not replaced: it writes through this store and hydrates from it, so session policy,
autonomy and durable recovery stay supported. Replacing a lifecycle service (session, streaming, invoker or
proposal) or `IAgentTurnStateStore` while this store is selected fails shell activation
(`AgentDurableRecoveryValidator`), because such a composition cannot recover what it records. The package carries EF Core and Relational APIs; the
host supplies its selected engine. Enable shell feature `FoundationAgentEntityFrameworkCore` (it
depends on `FoundationAgentAbstractions` and `Secrets`), or register directly:

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
`SelectAgentAuditStore` (`Elsa.Agent.Extensions.AgentPersistenceSelection`). A selection replaces
only the process-local default, whichever of the two registrations runs first. A second selection,
or a selection over a store registered some other way, throws at registration; the
`AgentRegistrationValidator` still refuses two record stores or two of any audit facet at shell
activation.

An application can instead call `modelBuilder.AddAgentModel(Database.ProviderName!)` in its own
`DbContext`, then `services.AddAgentStores<ApplicationDbContext>()`. Its host owns migrations.
Every operation resolves a fresh scoped context and commits independently; it never flushes pending
application changes or enlists in an application transaction.

## Module support preview (spec 1203)

This section describes current source awaiting qualification; it does not extend the historical
spec 1189 evidence to new binaries or consumers. Existing store-only hosts remain legacy-only.
To admit the new effects contract, select `FoundationAgentEfEffects`, represented by the public
extensible `FoundationAgentEfEffectsFeature`. It depends on the existing EF store feature and directly
declares Agent schema `1.1.0`. Do not also select the common `FoundationAgentEffects` sibling.
Both `AddAgentEntityFrameworkCore` and `AddAgentStores<TContext>` declare the EF effects type through
the owned store-selection boundary; they do not by themselves select the effects feature.

A specialization must directly repeat every base minimum schema declaration: the attributes are not
inherited. Missing or unfinalized schema observation refuses new effects through the shared checker;
loading a newer assembly is not schema finalization. Consumer-owned contexts must compose that shared
host gate explicitly. Without an admitted gate they may write only representable legacy 1.0 content;
unknown versions fail closed. A custom store declares its supported effects type with the additive
`SelectAgentSessionRecordStore(factory, requiredEffectsFeatureType)` overload. Existing two-argument
factories and explicit null remain valid without an effects selection, but selecting effects with them
is a configuration error. Store runtime types are never used to infer support.

Agent 1.1 adds content meanings rather than relational columns. The identity 1.0-to-1.1 upcaster
preserves protected content bytes; authenticate and decode through the normal store. A write uses the
version admitted in its metadata attempt and changes parent/children atomically. Existing audit sequence
counters retain their original stamp: 1.0 readability is required for their retained lifetime, including
after finalization. See the [recorded counter obligation](https://github.com/elsa-workflows/elsa-foundation/issues/3428#issuecomment-6671508360)
and [proposed durable contract](../../../../../specs/1203-weaver-module-support/contracts/outcomes.md).
Source fixtures and selected component tests do not establish native-provider, old-binary, fleet or
package-consumer compatibility; those gates remain open on [#3831](https://github.com/elsa-workflows/elsa-foundation/issues/3831).

## Tables

| Table | Holds |
| --- | --- |
| `elsa_agent_sessions` | Owner columns (actor, tenant, workspace), list columns (title, status, update and expiry time), policy revision, `ContinuedFrom`, the reached session limit, owner token, fence, lease and version; the protected session, paused turn and the audit records a recovery settlement still owes |
| `elsa_agent_messages` | One protected `AgentMessageRecord` per message, with role, status and status reason |
| `elsa_agent_disclosure_snapshots` | Each distinct snapshot once per session: digest, size and the protected canonical text |
| `elsa_agent_context` | Each context attachment once per session, protected, keyed by the SHA-256 of its identity |
| `elsa_agent_proposals` | One protected `AgentProposalRecord` per proposal, with status, prepared deadline and its record version |
| `elsa_agent_call_keys` | The call, invocation, turn-mutation and call-input keys, keyed by SHA-256 of the key |
| `elsa_agent_audit`, `elsa_agent_audit_owners` | Protected audit records and the first-wins owner association that scopes audit queries |
| `elsa_agent_audit_sequence` | One counter row that allocates audit append positions |

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

`Extensions/Agent/tests/Persistence/Tests` covers the store on SQLite with the actual migrations,
including the Agent restart, deduplication, execution and ownership scenarios run over this store.
`Extensions/Agent/tests/Persistence/ProviderTests` applies the migrations on PostgreSQL 16, SQL Server
2022 and MySQL 8.4 through Testcontainers and exercises round-trips, fencing and competing writers. It
skips without Docker unless `ELSA_REQUIRE_NATIVE_PROVIDER_MATRIX=1`.

See [extension points](EXTENSION_POINTS.md). Regenerate the pre-GA Initial migrations with
`bash tools/ef/generate-module-migrations.sh 'Agent.*DbContext'`; temporarily remove the generated
migration API entries while their types are absent, then restore the API baseline and build normally.
