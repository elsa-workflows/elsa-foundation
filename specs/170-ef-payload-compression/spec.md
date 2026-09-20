# Feature Specification: Opt-in Compression for Large EF Payload Columns

**Feature Branch**: `170-ef-payload-compression`

**Created**: 2026-09-18

**Status**: Implemented — decoder unit merged as `8e2f388e2` (#1864); the opt-in surface follows in the unit that carries this line

**Input**: GitHub issue [#1805](https://github.com/elsa-workflows/elsa-foundation/issues/1805), the design comment accepted on it, and the owner's answers to that comment's four open questions. The verified column inventory is in [research.md](./research.md).

---

## Problem Statement

Elsa 3 can compress two large persisted payloads through a compression codec resolver. Elsa 4 stores every payload as an uncompressed JSON string and has no compression anywhere in its EF modules.

Elsa 4 also has roughly forty payload columns across ten EF modules rather than Elsa 3's two, so the mechanism Elsa 3 used to mark a compressed row does not carry over: Elsa 3 adds a sibling `…CompressionAlgorithm` column beside each payload column, which here would mean forty columns and a migration in every module, on four providers, before a single byte is compressed.

This spec defines a compression facility that is **opt-in, provider-neutral, and needs no schema change at all**, and it pins the three things that make the facility safe, because a later reader will otherwise treat them as optional.

**No performance measurement.** Per the retired-measurement policy ([#1668](https://github.com/elsa-workflows/elsa-foundation/issues/1668), ADR 0073), this spec makes no size or timing claim, states no expected ratio, and sets no threshold derived from measurement. Every argument below is about code shape and correctness. The default is off, and the decision to turn it on in any deployment is the operator's.

### Why this is feasible at all

Every first-party EF module already uses one envelope: a lossless JSON document in one text column, a `SchemaVersion`, a `Revision` concurrency token, and a **sibling projection column for every attribute a query needs**. That is why no query in the tree filters on JSON content, and it is the pre-existing decision this feature rests on. [PR #1836](https://github.com/elsa-workflows/elsa-foundation/pull/1836)'s independent sweep of hand-written provider SQL reached the same finding.

---

## Settled Decisions

Settled with the owner on #1805 before drafting. These are inputs, not open questions. Planning must not re-open them.

| # | Decision | Rationale |
|---|---|---|
| **D1** | Compression is **opt-in and defaults to off** on every module. | There is no evidence on which to pick anything else, measurement is retired, and turning a storage encoding on by default changes what every row looks like on its next write. |
| **D2** | The codec, the frame format and the column registration helper live in the **shared** `src/Elsa/Persistence/EntityFramework/`. Modules decide which of their columns are payload columns. | That package already owns provider-neutral, lossless transforms applied at the storage seam (`EfRelationalIdentity`, `UnicodeOrdinalCasingTable`). `GZipStream` is a BCL type, so the package's "EF Core and Relational only, never a provider engine" rule in its `.csproj` survives untouched. |
| **D3** | A compressed value is marked **in band, inside the value**, not by a sibling column. | Forty columns and a migration in every module on four providers, versus none. Every payload column is already the provider's largest text type, unbounded and unindexed, so a frame needs no column type change on any dialect. Mixed rows then coexist by construction rather than by a nullable-column convention. |
| **D4** | The codec identity rides on `DbContextOptions` as an **`IDbContextOptionsExtension`**, contributing to `GetServiceProviderHashCode` and `ShouldUseSameServiceProvider`. | This is a **deviation from the accepted design**, which said `IModelCacheKeyFactory`. It meets the owner's requirement by the mechanism this repo already uses for exactly this hazard. See *Deviation D4* below. |
| **D5** | **GZip only.** The enum stays extensible and the frame carries a codec segment, so a second codec is additive. | One enum value, one branch. Adding Brotli later is additive **because the frame carries the codec**, not because anything gains a column. |
| **D6** | **Per-module opt-in**, with a host-wide fallback key, exactly as `Schema` and `Pooling` already work. | Diagnostics is the plausible first user; design is the least plausible. The fallback is safe for the reason in *Correction D6* below. |
| **D7** | Compression **never changes `SchemaVersion`**. | `SchemaVersion` describes the document's shape; the frame describes the storage encoding. Keeping them orthogonal leaves every existing version check exactly as written, for example `state.SchemaVersion == RuntimeTriggerBindingEfModule.SchemaVersion` at `EfWorkflowTriggerBindingStore.cs:344`. If compression bumped it, every check would have to accept two versions per document shape, and a version-gated read would start depending on a storage choice. |
| **D8** | **`ContentAuthority` and its `*Json` siblings are permanently excluded**, not deferred. | Those columns are read by database-side SQL in all four dialects: the `ContentAuthorityIsValid` computed column, and SQL Server's digest comparison against `ContentAuthorityIntegrityHash`. A client-side encoding makes every row evaluate invalid, and because `EfActivityDesignStores` filters on `ContentAuthorityIsValid` before paging, the failure mode is **silent omission, not an error**. Lifting the exclusion requires removing the computed columns first, which #1836 explicitly declined to do and gave its reason for; that is a different decision. Context: [#1836](https://github.com/elsa-workflows/elsa-foundation/pull/1836), [#1809](https://github.com/elsa-workflows/elsa-foundation/issues/1809), [#1837](https://github.com/elsa-workflows/elsa-foundation/issues/1837). |
| **D9** | **Every hash, fingerprint and parse is defined over the decoded plaintext**, never over the stored bytes. | Three sites read the stored string and each fails differently. See *Checklist D9*. |
| **D10** | **Standing invariant**: a payload column never appears in a translated predicate, an index or an `ORDER BY`. Enforced by an architecture guard. | It holds today for a different reason, the envelope shape. Compression makes it load bearing, and an invariant that holds by accident is one nobody will preserve deliberately. |
| **D11** | The encoder **writes plaintext whenever the framed result is not shorter than the plaintext**, so base64 inflation can never make a row larger. `MinimumLength` is a short-circuit, not a correctness rule. | Base64 inflates by four thirds, so a small payload's frame is larger than the payload. Comparing and choosing makes that impossible by construction rather than by threshold tuning. |
| **D12** | **The decoder ships before, and independently of, the encoder flag.** | A database written with compression on cannot be read by a build that predates the decoder. Pre-release policy owes no shim for the format, but that ordering is real. |
| **D13** | Losslessness is proven on **SQLite in the fast lane**, on every PR. Proof that compression reached the column is **Testcontainers-gated**. | The container test proves the bytes landed compressed; the SQLite test proves losslessness, and losslessness is the property that actually hurts if it breaks. |

### Deviation D4 — options extension rather than `IModelCacheKeyFactory`

The accepted design proposed writing an `IModelCacheKeyFactory` that folds in the codec identity. Since that design was written, `origin/main` gained `EfSchemaOptionsExtension` ([`src/Elsa/Persistence/EntityFramework/EfSchemaOptionsExtension.cs`](../../src/Elsa/Persistence/EntityFramework/EfSchemaOptionsExtension.cs)), whose own remarks state the identical hazard for the schema setting:

> EF caches one model per internal service provider, and that provider is cached by the hash every extension contributes. Two contexts of the same type bound to different schemas therefore have to disagree here, or the second would silently reuse the first one's model and write to the first one's schema.

The compression setting has the same shape as the schema setting, so it should use the same mechanism. Doing so is better than a bespoke factory on three counts:

1. **Pooling.** Contexts are now registered through `EfModuleBinding.AddContext`, which selects `AddDbContextPool` when the module's `Pooling` option is set. A pool is keyed by options, so a setting that rides on options is correct under pooling for free; a model cache key factory addresses only half the problem.
2. **Construction purity.** The context reads the setting back in `OnModelCreating` through a static `Find(DbContext)`, taking no injected service. That preserves `ModuleSchemaTests.Every_module_context_is_constructed_from_its_options_alone`, which is the invariant that makes pooling safe at all.
3. **One mechanism, not two.** A second, differently-shaped answer to "a per-host setting that changes the model" is how a codebase acquires two half-correct caching stories.

The owner's stated requirement is met in full: the codec identity participates in the cache key, and the acceptance test is the one the owner asked for. **If the owner prefers the literal `IModelCacheKeyFactory`, say so and this reverts to D4-as-accepted;** the rest of the spec is unaffected.

### Correction D6 — why a host-wide fallback is safe

The owner's reason for per-module opt-in was that "a global switch would turn it on for `ContentAuthority`'s module". That reasoning does not quite hold, and the difference matters: **the exclusion is per column, not per module.** The Activities Design module also owns `PlanJson`, `ReceiptJson`, `AuthoritativeResultJson`, `MutatedUnitsJson` and `DefinitionMaterialJson`, which are ordinary payload columns. `ContentAuthority*` is safe under any switch because it is simply never registered as a payload column (FR-012), not because its module is never switched on.

Per-module opt-in is therefore kept as the primary control, for the reason the owner gave second and which does hold: the modules differ in kind, and diagnostics and design deserve separate answers. A host-wide fallback key is added alongside it, mirroring `EfSchema.ConfigurationKey`, because that is the shape operators are already taught and because the column list, not the switch, is what protects the excluded columns.

### Checklist D9 — the three sites that read the stored string

The implementation must verify each of these, and each fails differently, which is why they are enumerated rather than summarized:

| Site | Failure if it sees a frame |
|---|---|
| `Elsa3ImportRecordCodec.cs:45, 91` writes `ContentHash = Hash(json)`; `:195` re-verifies it on **every read** | fails closed on every read of a framed row |
| `EfDesignSupport.IsResultFingerprintValid` (`EfDesignSupport.cs:178-201`), from `EfDesignAtomicWriter.cs:382, 410`, calls `JsonDocument.Parse` on the raw column | **throws**, rather than mismatching |
| `StructuredLogAppendFingerprint.Compute` (`StructuredLogAppendFingerprint.cs:21`) must keep folding plaintext | a retried append after the flag flips computes a different fingerprint and stops deduplicating |

Everything else that looks like a payload hash hashes a re-serialization of the domain model and is unaffected. The full list is in [research.md](./research.md).

### Rejected alternatives (recorded, not revisited)

- **A sibling `…CompressionAlgorithm` column per payload column**, as Elsa 3 does. Rejected under D3 on migration cost; Elsa 3 had two such columns and Elsa 4 would have about forty.
- **Transforming at each store's `Serialize`/`Deserialize` call site.** About forty sites, and a missed one silently stores a frame that another reader hands to `JsonDocument.Parse`, which is the D9 failure mode reintroduced by omission.
- **A `SaveChangesInterceptor` plus a materialization interceptor.** Change tracking would hold framed values as current values, muddying the `Revision` concurrency story for no gain.
- **A `byte[]` column.** A schema break on every payload column, and it discards the provider column types already pinned per dialect.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - An operator turns compression on for one module, on a database that already has rows (Priority: P1)

An operator running a deployment with existing data sets the codec for one module and restarts. The host starts, applies no migration, reads every pre-existing row unchanged, and writes new rows framed. Nothing else in the deployment changes.

**Why this priority**: This is the feature. It is also where the whole design either holds or does not: if a pre-existing row cannot be read after the flag flips, nothing else matters.

**Independent Test**: Write rows with the codec off, flip it on, read them all back and assert each equals what was written; then write new rows and read them back. Fully testable on SQLite in process, with no container and no timing budget.

**Acceptance Scenarios**:

1. **Given** a database whose rows were all written with the codec off, **When** the operator sets the codec to GZip and restarts, **Then** every existing row still reads back byte-identically to what was written.
2. **Given** compression is on, **When** a row is written and read back, **Then** the value returned equals the plaintext that was handed to the store, exactly.
3. **Given** compression is on and rows have been written, **When** the operator sets the codec back to off and restarts, **Then** the framed rows still read back correctly and new writes are plaintext.
4. **Given** compression is on, **When** a module applies its migrations, **Then** no migration is pending and none was added for this feature.

---

### User Story 2 - Two hosts in one process disagree about compression (Priority: P1)

A process composes two hosts against the same module type, one with compression on and one with it off. Each reads and writes its own rows correctly; neither silently adopts the other's encoding.

**Why this priority**: The owner named this specifically. It is the failure that shows up once, in someone else's environment, as unreadable payloads, and it cannot be found by a test that composes one host.

**Independent Test**: Compose two hosts in one process with different codec settings, write through each, and assert each reads its own rows. Fully testable in process.

**Acceptance Scenarios**:

1. **Given** two hosts in one process configured with different codecs, **When** each writes and reads its own rows, **Then** each reads back exactly what it wrote.
2. **Given** those two hosts, **When** their models are built, **Then** they do not share one model, and the difference is attributable to the codec identity on the options.
3. **Given** a module registered with pooling on, **When** two hosts with different codecs both draw contexts from their pools, **Then** neither receives a context carrying the other's encoding.

---

### User Story 3 - A maintainer adds a payload column, or a predicate over one (Priority: P2)

A maintainer adds a new payload column, or writes a query that filters on an existing one. The first is a one-line registration next to the existing column-type declaration. The second fails the build.

**Why this priority**: The invariant in D10 holds today by accident. This story is what converts it into something the codebase keeps.

**Independent Test**: Add a predicate over a registered payload column in a test fixture and assert the architecture guard reports it.

**Acceptance Scenarios**:

1. **Given** a registered payload column, **When** it is given a max length or placed in an index, **Then** the architecture guard fails and names the column.
2. **Given** a new payload column added to a module, **When** the maintainer registers it in the module's existing provider-context enumeration, **Then** it participates in compression with no other change.

---

### Edge Cases

- **A plaintext value that begins with the frame prefix.** The encoder frames it with the identity codec so the mapping stays total; there is no ambiguous input and no failure mode. Note that this is not merely theoretical: `WorkflowTriggerBindingProjectionStateEntity.ContentJson` deliberately stores a hex fingerprint rather than a JSON document, so payload columns are not all documents.
- **A malformed or truncated frame.** Fails closed with `InvalidDataException`, matching what the stores already throw for unreadable content (`EfWorkflowTriggerBindingStore.cs:319-325`). Explicitly **not** the Elsa 3 behavior, which logs a warning and substitutes a default state (`WorkflowInstanceStore.cs:251-255`); that is silent state loss.
- **A frame naming a codec this build does not have.** Fails closed by name. It is the forward-compatibility direction D12 addresses, and it must not be mistaken for corruption.
- **A payload whose framed form is not shorter.** Written as plaintext (D11).
- **A null payload column** (`BookmarkStateEntity.PayloadJson`, `WorkflowAlterationPlanEntity.CleanupSafeFailureJson`). Null stays null; the codec never converts null to a frame or a frame to null.
- **An empty string.** Round-trips as an empty string, distinct from null.
- **A payload containing a lone surrogate**, which `RuntimeArtifactJson`'s `LosslessUtf16StringConverter` exists to preserve. Must survive a frame round-trip unchanged.

---

## Requirements *(mandatory)*

### Functional Requirements

**Format**

- **FR-001**: The system MUST mark a compressed value in band, within the stored value, and MUST NOT add any column to any entity.
- **FR-002**: The frame MUST be self-describing, carrying a format marker and a codec identifier, so a value can be decoded without consulting any other column or configuration.
- **FR-003**: A stored value that does not carry the frame marker MUST be treated as plaintext and returned unchanged.
- **FR-004**: The encoder MUST produce a frame for any plaintext that would itself be mistaken for a frame, so that encoding is total and injective.
- **FR-005**: Decoding MUST be lossless: the decoded value MUST equal the encoded plaintext exactly, for every input including empty strings, lone surrogates and non-JSON text.
- **FR-006**: The encoder MUST write plaintext whenever the framed result is not shorter than the plaintext.
- **FR-007**: Decoding a malformed frame, or one naming an unavailable codec, MUST fail closed with a diagnostic naming the cause. It MUST NOT substitute a default value.

**Placement and configuration**

- **FR-008**: The codec, the frame format and the registration helper MUST live in `Elsa.Persistence.EntityFramework`, which MUST NOT gain a dependency on any database provider engine.
- **FR-009**: The system MUST support GZip and an identity codec. The codec identifier MUST be an extension point such that adding a codec requires no format change and no schema change.
- **FR-010**: Each module MUST be able to select its codec independently, with a host-wide fallback key, resolved the way `EfSchema.Resolve` resolves the schema setting.
- **FR-011**: The default for every module MUST be no compression.
- **FR-012**: Payload columns MUST be registered explicitly per module. `ContentAuthorityJson`, `ContentAuthorityCanonicalJson`, `ContentAuthorityAuthorityKeyJson` and `ContentAuthoritySourceIdJson` MUST NOT be registrable, and the refusal MUST state why.
- **FR-013**: `SecretRecord.Payload` MUST NOT be registered pending an explicit security decision.

**Model and correctness**

- **FR-014**: The codec identity MUST participate in whatever EF uses to decide that two contexts of the same type may share a model, so two hosts in one process with different settings cannot share one.
- **FR-015**: A module context MUST remain constructible from its `DbContextOptions` alone, preserving `ModuleSchemaTests.Every_module_context_is_constructed_from_its_options_alone` and therefore pooling safety.
- **FR-016**: Enabling compression MUST NOT change any module's `SchemaVersion` constant, and MUST NOT change any store's existing schema-version comparison.
- **FR-017**: Enabling compression MUST NOT produce a pending model change against any committed migration snapshot, on any provider, keeping `ModuleMigrationTests.Every_module_context_has_migrations_that_match_its_model` green.
- **FR-018**: All hashing, fingerprinting and JSON parsing of payload content MUST operate on decoded plaintext. The three sites in *Checklist D9* MUST each be verified.
- **FR-019**: A registered payload column MUST NOT appear in a translated predicate, an index, an `ORDER BY`, or carry a max length. An architecture guard MUST enforce this and name any column that violates it.

**Rollout**

- **FR-020**: The decoder MUST be able to read every frame the encoder can produce, and MUST ship in a unit of work that precedes the one enabling any encoder.
- **FR-021**: The feature MUST require no data migration, no backfill and no rewrite pass, on any provider.

### Key Entities

- **Payload frame**: the stored form of a compressed value. Carries a format marker, a codec identifier, and the encoded bytes. Its only consumers are the encoder and the decoder.
- **Payload codec**: the named, lossless `string` to `string` transform a frame identifies. GZip and identity at first.
- **Payload compression setting**: one module's chosen codec plus its short-circuit length, resolved from the module's own option then a host-wide key, and carried on `DbContextOptions`.
- **Payload column registration**: the explicit, per-module list of properties the codec applies to, declared alongside the existing per-provider column-type declarations.

---

## Success Criteria *(mandatory)*

Correctness and shape only. No size or timing criterion appears here, by policy.

- **SC-001**: A value written with any codec and read back equals the original exactly, for every case in *Edge Cases*, on all four providers.
- **SC-002**: A database written entirely with the codec off reads correctly after the codec is turned on, and a database containing framed rows reads correctly after it is turned off. Both directions are covered by a test.
- **SC-003**: Zero migrations are added by this feature, and `ModuleMigrationTests.Every_module_context_has_migrations_that_match_its_model` passes on all four providers with compression both off and on.
- **SC-004**: Two hosts in one process with different codec settings each read their own rows, with pooling both off and on.
- **SC-005**: A test reading the column through a path that does not decode confirms the stored bytes are framed when compression is on and plaintext when it is off. May be Testcontainers-gated.
- **SC-006**: A round-trip test on SQLite runs in the fast lane on every PR.
- **SC-007**: An architecture guard fails when a registered payload column gains a max length, an index, or a translated predicate.
- **SC-008**: An attempt to register any excluded column fails with a diagnostic naming the reason.
- **SC-009**: Every test asserting a compression behavior is proven to go red when the production change is reverted.
- **SC-010**: `Elsa.Persistence.EntityFramework` has no new `PackageReference`.

---

## Assumptions

- Payload columns are unbounded text on every provider and are not indexed. Verified in [research.md](./research.md); an architecture guard keeps it true.
- Pre-release policy applies: no back-compat shim is owed for the frame format itself. D12's ordering constraint is a deployment obligation, not a format-compatibility one.
- The `…SearchKey` and projection columns are out of scope. They are the query surface and stay plaintext.
- `ContentAuthority`'s computed columns remain as #1836 left them. If they are ever removed, D8 may be revisited as a separate unit of work.
- `EfModuleBinding.AddContext` remains the single registration seam for every module context.

---

## Out of Scope

- Any change to `ContentAuthority*` or to the computed columns that read it.
- Compressing `SecretRecord.Payload`, or any encryption question.
- The collation divergence in [#1837](https://github.com/elsa-workflows/elsa-foundation/issues/1837).
- Compressing anything outside EF persistence: wire payloads, exports, the diagnostics ingestion path.
- Any performance measurement, benchmark, or threshold derived from one.
