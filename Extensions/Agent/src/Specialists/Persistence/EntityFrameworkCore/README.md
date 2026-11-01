# Specialist EF turn journal

`Elsa.Agent.Specialists.Persistence.EntityFrameworkCore` implements the specialist conditional turn
journal. The package carries EF Core and Relational APIs; the host supplies its selected engine.
Enable shell feature `SpecialistsEntityFrameworkCore`, or register directly:

```csharp
services.AddSpecialistsEntityFrameworkCore(new()
{
    Provider = "Sqlite",
    ConnectionString = "Data Source=specialists.db"
});
```

SQLite, SQL Server, Npgsql PostgreSQL and Oracle MySQL have separate context/migration sets.
The module uses the shared EF startup policy, named connections, schema selection and pooling.
The stable history module is `ElsaAgentSpecialists`. Enabling the explicit development in-memory
feature before or after EF registration cannot replace this database backend.

An application can instead call `modelBuilder.AddSpecialistsModel(Database.ProviderName!)` in its
own `DbContext`, then `services.AddSpecialistTurnStore<ApplicationDbContext>()`. Its host owns
migrations. The context needs a public constructor accepting its own `DbContextOptions<TContext>`.
Writes create fresh owned contexts and commit independently; they do not flush pending application
changes or enlist in its transaction. Resolve a separate scoped store for each concurrent operation.

The operation index hashes the exact tenant, partition, workflow and operation identity, using a
structured encoding. Malformed UTF-16 is refused before hashing. Caller-supplied keys must match.
Identical stage replay preserves the original deadline and ciphertext. The store compares record
schema, version, lifecycle, fence, admission generation and finite lease/deadline in conditional database writes. It never replaces
an accepted result, and it refuses unsupported record schemas without mutation.

Physical model requests append unique reservations under the same conditional version/state/fence
and deadline checks. Concurrent reservations cannot exceed the staged finite request maximum.
Retained output ceilings and configured resource observations survive response loss and host restart;
missing usage does not release a reservation. Native runtime checks authenticate the staged request
maximum before creating its transport authority. This is not a hard input/total-token accounting claim.
Completing the exact reserved request set records nullable usage and the reported model ID while
freezing further reservations. Acceptance with reservations requires that completed response. Wrong
request sets, stale fences, expired completion and later observation replacement are refused. The
accepted protected receipt uses known single-request output usage or conservative reserved ceilings
when output usage cannot be attributed; no absent value is treated as zero.

Business input, accepted output and the conversation candidate are protected by the runtime using
the selected Secrets protector/key ring. Retain the required keys across host restarts. Store
capabilities describe configuration: file-backed SQLite and the three other supported relational
providers report durability; temporary, memory and custom-VFS SQLite profiles do not. This is not
a reachability or credential-durability check.

The specialist test project covers SQLite behavior, native workflow execution, protected pair recovery,
scope/schema refusals and consumer-owned contexts. `Elsa.Agent.Specialists.ProviderTests` applies the
actual migrations on PostgreSQL, SQL Server and MySQL and exercises competing stages/claims, live
leases, acceptance and fresh-host replay. It forces renewal commits between a response/acceptance read
and its conditional update, verifying safe metadata retries on all three actual database engines. Full tier sets `ELSA_REQUIRE_NATIVE_PROVIDER_MATRIX=1`
for this suite, so an unavailable required database fails rather than skipping.

The conversation table retains owner/version, revision, active turn/fence, uncertainty and the last
accepted receipt reference. Staging alone does not create a conversation. Claim atomically admits
one operation at its expected revision. Acceptance advances the revision and stores the turn result
in one transaction; a conversation CAS/write failure rolls back the turn mutation too. Known failure
releases the active revision without adding history; unknown outcome blocks further continuation.
The initial owner is a verified workflow execution in the exact tenant/partition and specialist
version/template. Cross-workflow ownership policies are not supplied by business input.

Lease generation/expiry are payload fields in the existing record schema; this increment adds no table
or migration. Each owned mutation validates the current payload, then binds its update to the row's
version. A rejected row/conversation CAS rolls back and retries a fresh metadata transaction, up to32
attempts. An exception or unconfirmed commit is never retried. Renewal cannot extend the original
deadline, and expired dispatch is recorded as OutcomeUnknown without releasing reservations.

New protocol1 records register matching native deliveries through conditional turn metadata writes.
Concurrent stage/accepted replay is bounded by row versions and preserves the original receipt,
request deadline and newer conversation history, including uncertainty from another operation.
The three actual providers qualify eight concurrent registrations/replays and fresh-host restoration.
No relational schema changed: associations are bounded payload metadata. Cancellation/applied
settlement and content retention remain part of M3. A fresh service provider/context test proves encrypted pair
recovery; it is not independent-process or live-model qualification.

See [extension points](EXTENSION_POINTS.md). Regenerate pre-GA Initial migrations with
`bash tools/ef/generate-module-migrations.sh 'Specialists.*DbContext'`; temporarily remove the generated
migration API entries while their types are absent, then restore the API baseline and build normally.
