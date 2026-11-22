# Decisions EF Core persistence

`DecisionsEntityFrameworkCoreFeature` selects an internal provider-specific context and the normal shared EF binding, schema chain and Prepare-phase migration lifecycle. Its migration history is isolated as `ElsaDecisions`. Engine packages are supplied by the host; product dependencies include only EF Core/Relational and the shared persistence policy. Existing databases use additive module migrations; this feature never calls EnsureCreated or resets another module's store.

Applications may instead install `AddDecisionsModel` in their own DbContext and register an `IDbContextFactory<TContext>` with `AddDecisionsEntityFrameworkCore<TContext>`. They own their migrations and database lifetime. Model installation applies ordinal comparison only to Decisions records and leaves unrelated entity configuration intact. Every operation obtains a fresh context, so a failed or competing write cannot flush unrelated tracked changes.

The provider stores domain JSON documents under hashed tenant, kind and opaque entity identity. Revision tokens are concurrency tokens. Serializable write transactions commit aggregate changes with their operation receipt. Publication appends an immutable version and updates the asset pointer in the same transaction. Operation identity includes trusted tenant, actor and operation ID; equivalent replay returns the original confirmation and different request content conflicts. Queries never fall back to another tenant, owner or version.

A decision retains at most 1,000 completed test receipts. The same serializable insertion enforces that bound after existing-operation replay. At capacity, explicitly delete retained runs through the authorized history API before testing again; no history is automatically removed.

Tests qualify actual SQLite connections, concurrent CAS and publication, completed-receipt replay and consumer-owned contexts. Migration generation for four engines is separate from executing their behavior suites; no PostgreSQL/SQL Server/MySQL runtime qualification follows from SQLite results. No background task or execution driver is registered.
