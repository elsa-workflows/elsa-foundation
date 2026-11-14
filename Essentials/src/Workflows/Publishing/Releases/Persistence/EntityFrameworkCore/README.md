# Frozen release archive persistence

This adapter stores a complete bounded release transport under a capture ID. A capture ID differs from the normalized
release digest. Transport-only persistence proves neither owner semantic validity nor publisher origin, and does not pin
source catalogs, activate serving roots or journal deployment attempts.

Only `Microsoft.EntityFrameworkCore.Sqlite` is supported by this initial adapter. Other providers are explicitly refused.
The focused suite uses real file databases, independent contexts and a killed/reopened child process. SQL Server,
PostgreSQL, MySQL and multi-host storage semantics are not qualified by these SQLite witnesses.

The consumer owns an ordinary `DbContext`, its SQLite provider/options and its migrations. The context needs a public
constructor accepting its own `DbContextOptions<TContext>`. In `OnModelCreating`, call
`modelBuilder.AddReleaseArchiveModel(Database.ProviderName!)`. Register the selected `IReleaseBundleCodec` and the existing
`IPersistenceAccessContextAccessor`, then select this backend:

```csharp
services.AddReleaseArchiveStore<ApplicationDbContext>(provider =>
    new SourceScope(trustedPartition, trustedTenant, "workflow-release-archive", trustedAuthority));
```

Only connection-string configured options are accepted. `UseSqlite(existingConnection)` is refused with
`archive_shared_connection_unsupported` before archive I/O, including when the context owns that connection.
No caller connection or transaction is reused or disposed by an archive-owned context.

Every archive-owned context uses a cloned concrete `DbContextOptions<TContext>` with the existing
`EfRelationalProviderBinding.ConfigureSqliteConnectionSafety` mitigation, including the fresh template handed to
`EfSharedTransaction`. Caller options and tracked state are untouched; unknown/nonconcrete options shapes refuse explicitly.
Consumer code sharing the same SQLite pool must use the common provider binding or that helper for **all** openers, as the
shared persistence contract requires for pinned Microsoft.Data.Sqlite 10.0.10. The pooled witness uses real concurrent
capture/read/reopen operations; it does not claim a timing-shaped reproduction of the library race.

The scope factory must use authenticated server/domain context. Its partition must equal the current ordinary persistence
partition. Authority/environment, partition and nullable tenant remain separate identities; a null tenant never means global.
Archive payloads and request-supplied manifest fields cannot select scope. The adapter checks full encoded identities after
scoped hash lookups, preserving ordinal .NET identities without lossy Unicode conversion.

`requireDurable` defaults to `true`: creation refuses SQLite memory/temporary modes before opening a write transaction.
Explicit `requireDurable: false` permits disposable transport consumers. `IReleaseArchiveStoreCapabilities` always reports
the actual file/memory mode independently of that option, plus mapped schema readability in the selected scope. Schema
readiness is a local observation, not a promise of disk health, owner validity or source retention. Future source release
creation must require durable capture and a successful archive acknowledgement before releasing its retained source roots.

Creation validates the caller-owned ZIP stream, then commits header, original manifest, binary blob rows and attachment
rows in one owned `EfSharedTransaction`. Fresh contexts neither flush caller-tracked changes nor enlist caller transactions.
Capture timestamps use the host's registered `TimeProvider`, defaulting to `TimeProvider.System`; retries retain the first timestamp.
The first raw manifest and each blob/attachment have independent SHA-256 and byte-length stamps. These detect changed
stored representation, including attachments and manifest whitespace outside the normalized release digest; they establish
local archive integrity, not publisher origin. Reads first project actual SQLite `length(BLOB)` metadata and enforce every
individual/expanded byte bound. Only then do bounded payload queries recheck the metadata and raw hashes. A changed row
cannot grow a payload allocation between preflight and fetch. SQLite BLOB storage class is required explicitly; bounded
TEXT metadata guards run before returning strings. Encoded entry names have a separate conservative metadata budget of
`MaxExpandedBytes` (four bytes per projected character), checked consistently on create/read. Readiness probes reference all mapped columns through
metadata/length projections without loading stored payloads.
Readers use one deferred read snapshot on a qualified `Microsoft.Data.Sqlite.SqliteConnection` and revalidate stored parts
directly through `ValidateParts`; they do not recompress ZIP files or reserve SQLite's writer lock. Concurrent corruption
after that snapshot leaves its coherent original bytes readable; a subsequent read observes and refuses the corruption.
No store/registration method creates tables, migrates schemas or
changes schema finalization. Operators prepare the host separately; a missing/partial schema is visibly unavailable.
Payload reads use one bounded query per entry to recheck each row; large attachment sets therefore add round trips.

An equal-content retry under the same capture ID returns `Existing` and retains the first manifest bytes and creation time.
Manifest whitespace/property/set order and ZIP layout are transport formatting; exact blob bytes and attachment path/bytes
still have to match. Conflicting retries refuse without overwriting. Several capture IDs may hold the same release digest.
There is no update/delete/GC or cross-capture deduplication API in this foundation.

`Created` and `Existing` are explicit nonzero confirmations. Cancellation before a commit attempt remains cancellation.
Any failed acknowledgement after commit reached the provider is `ReleaseArchiveCommitOutcomeUnknownException`, including
provider cancellation. Recover with a fresh read/retry using the original capture ID; do not infer a confirmed failed write.

Run the focused qualification from the repository root:

```sh
dotnet test Essentials/tests/Workflows/Publishing/Releases/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Publishing.Releases.Persistence.EntityFrameworkCore.Tests.csproj
```

The test project builds its own `RestartProbe`. Its interruption witness kills that process after the uncommitted header
insert, verifies a live reader sees no partial capture, and then verifies an independent process reopens all original bytes.
Test setup explicitly creates test schemas; this is not adapter startup behavior. See the separate
[archive contract](../../../../../../../specs/1225-workflow-release-promotion/contracts/archive-store.md) for boundaries.

Backend registration rejects competing selections. Deliberately replacing a valid prior default selection requires
`replaceExisting: true`; all three ports move together. Late duplicates or partial replacements are detected before
default facade/context resolution and through `ValidateOnStart`. Generic Host runs startup validation automatically;
CShell hosts use the existing shell startup-validation bridge. Standalone providers must call
`provider.GetService<IStartupValidator>()?.Validate()` after final composition and before using archive ports,
including when custom registrations bypass the default factories. No separate hosted service is installed.
