# Divergence ledger — EF Core vs Groundwork behavioural differential

Work unit: `094-harden-groundwork-stores` (artifact), executed under [Elsa #646](https://github.com/elsa-workflows/elsa-foundation/issues/646).
Consumed by: [spec 144](../144-zero-ef-final-removal/) T011.
Status: both diagnostics seams and the one admissible IAM seam recorded.

## What this artifact is, and is not

This is the **correctness precondition** for #646, not its verdict.
[`contracts/performance-handoff.md`](contracts/performance-handoff.md) states that "timing is invalid
until the correctness digest and provider conformance scenario pass", and assigns real EF execution to
#646. This ledger records what that execution observed.

It writes **no `performanceVerdict`** into [`coverage-ledger.json`](coverage-ledger.json) and advances
**no ledger status**. `evidence-complete` requires non-empty provider evidence for all four mandatory
providers; this differential covers SQLite alone, because EF Core has no PostgreSQL or SQL Server
wiring anywhere in `src/`. A green differential here moves the diagnostics rows zero rungs, by design.

## Oracle availability

The differential only exists where both stacks implement the same contract. That set is small and does
not include any runtime seam — no runtime persistence seam has ever had an EF-Core-backed
implementation. See the [zero-EF decision map](../../docs/decision-maps/zero-ef-groundwork.md)
(`oracle-inventory`) for the derivation.

| Seam | Contracts | This ledger |
|---|---|---|
| Diagnostics | `IStructuredLogStore` | **recorded below** |
| Diagnostics | `IOpenTelemetryStore` | **recorded below** |
| Identity (Elsa IAM) | `ITenantMembershipStore` | **recorded below** |
| Identity (Elsa IAM) | `IUserStore`, `IRoleStore`, `IExternalIdentityStore` | blocked — ledger rows `externally-blocked` |
| Runtime (all 21 rows) | — | **no EF comparand exists; not gradable by EF ratio** |

## Row schema

| Column | Meaning |
|---|---|
| `dimension` | one of the six behavioural dimensions driven per contract |
| `fact` | the normalized observation key compared across both stacks |
| `verdict` | `equivalent` / `divergent` / `ef-only-mechanism` / `not-expressible` |
| `disposition` | `ContractIsGroundwork` / `ContractIsEf` / `Undecided` — which stack's behaviour is the contract |
| `testDisposition` | `Preserve` / `Convert` / `RemovePending` / `RemoveApproved`, verbatim from spec 144 so T014/T040 consume it directly |
| `authority` | the ratifying issue or decision, required when `disposition` is `ContractIsEf` or `Undecided` |

`disposition` is the load-bearing column. A divergence is **not** automatically a Groundwork defect —
sometimes the EF behaviour was the accident. `ContractIsEf` means Groundwork has a real bug and EF is
not deletable until it is fixed. `Undecided` fails closed and blocks spec 144's T011.

## `IStructuredLogStore` — SQLite, recorded 2026-08-03

Comparands: `efcore.sqlite` (`EfCoreStructuredLogStore`) and `groundwork.sqlite`
(`GroundworkStructuredLogStore`), both over **file-backed** SQLite with distinct connections, as
[`workloads/diagnostics.json`](workloads/diagnostics.json) requires
(`file-backed-distinct-connections`). That the retained EF implementation is the SQLite comparand is
recorded by the workload's `correctness.timingGate`; it was previously also encoded as a suffix on the
topology identifier, which no driver could report — see
[`diagnostics-provider-topology-basis.md`](contracts/diagnostics-provider-topology-basis.md).

Executable form: `tests/Elsa/Diagnostics/StructuredLogs/Persistence/Tests/Differential/`.
Surface digest (dimension + compared-fact + recorded-divergence names, never observed values):
`378e6e62c559c70e0256420e9f8a34627f6089aefec7e37e5d5a23a06f217704`.

**Result: 38 facts compared across 6 dimensions; zero divergences.**

| dimension | facts compared | verdict | disposition | testDisposition |
|---|---|---|---|---|
| `concurrency-conflict-shape` | 4 | `equivalent` | — | `RemovePending` |
| `producer-ordering` | 6 | `equivalent` | — | `RemovePending` |
| `null-and-default-materialization` | 13 | `equivalent` | — | `RemovePending` |
| `rollback-visibility` | 5 | `equivalent` | — | `RemovePending` |
| `restart-observation` | 6 | `equivalent` | — | `RemovePending` |
| `idempotent-replay` | 4 | `equivalent` | — | `RemovePending` |

Zero divergences is a finding, not an absence of one: it is the evidence that deleting
`EfCoreStructuredLogStore` forfeits no observed behaviour at this seam, on this provider.

### Dimension notes

- **`concurrency-conflict-shape`** — the stream is append-only and carries no revision token, so there
  is no optimistic-concurrency conflict to detect. The comparable failure shape is the one the
  interface specifies: trimmed, foreign and default cursors must fail the same non-disclosing way. Both
  stacks throw `StructuredLogReplayCursorUnavailableException` for all three, so the non-disclosure
  guarantee holds identically. Contract-level concurrency conflict is exercised at the OpenTelemetry
  seam instead, where catalog upserts use Groundwork document concurrency.
- **`producer-ordering`** — two concurrent producers, 25 appends each. Both stacks: no loss, no
  duplication, per-producer order preserved, and the high-water mark equal to the maximum *logical*
  `Sequence` rather than the record count. That last point is contract-correct — `Sequence` is
  caller-assigned display metadata that concurrent writers may duplicate; durable ordering is the
  replay cursor.
- **`null-and-default-materialization`** — a fully populated entry and a fully sparse one. All 13
  facts preserved on both stacks, including nested exception stack traces, scope text, properties, and
  the distinction between an empty message and a null `EventId`.
- **`rollback-visibility`** — 12 acknowledged appends, then provider release without completing the
  capture drain, then reopen. Both stacks: every acknowledged write durable, no torn batch, high-water
  preserved.
- **`restart-observation`** — graceful close and reopen. Both stacks preserve the high-water mark
  without rewind, expose a tail cursor, and still resolve a cursor issued before the restart.
- **`idempotent-replay`** — records that neither stack deduplicates a re-submitted entry instance
  (two records, distinct cursors, identically on both sides), and that a committed cursor resolves
  stably across repeated reads. **Coverage limit, stated rather than implied:** the interface's
  idempotency clause concerns a store's *internal* commit retry after acknowledgement loss, which
  neither stack exposes without a failure-injecting provider double. This differential does not cover
  that clause. Same-stack coverage exists on both sides
  (`GroundworkStructuredLogStoreTests`, `EfCoreStructuredLogStoreResilienceTests`); a shared
  differential probe for it is open follow-up.

## `IOpenTelemetryStore` — SQLite, recorded 2026-08-03

Comparands: `efcore.sqlite` (`EfCoreOpenTelemetryStore`) and `groundwork.sqlite`
(`GroundworkOpenTelemetryStore`), both over file-backed SQLite.

Executable form: `tests/Elsa/Diagnostics/OpenTelemetry/Persistence/Tests/Differential/`.
Surface digest: `b2310b9850d716dc844e1f8c7c04fb24650aaf35d28a700eda473a18c1e0dfb2`.

**Result: 37 facts compared across 6 dimensions; zero divergences.**

| dimension | facts | verdict | disposition | testDisposition |
|---|---|---|---|---|
| `concurrency-conflict-shape` | 6 | `equivalent` | — | `RemovePending` |
| `producer-ordering` | 6 | `equivalent` | — | `RemovePending` |
| `null-and-default-materialization` | 12 | `equivalent` | — | `RemovePending` |
| `rollback-visibility` | 2 | `equivalent` | — | `RemovePending` |
| `restart-observation` | 6 | `equivalent` | — | `RemovePending` |
| `idempotent-replay` | 5 | `equivalent` | — | `RemovePending` |

### Withdrawn divergence — `rollback-visibility` / `readable-trace-count`

**An earlier revision of this ledger recorded a divergence here (EF `1`, Groundwork `2`) with
disposition `ContractIsGroundwork`, on the reasoning that Groundwork commits a queued batch before drain
completion while EF's channel drain loses it. That finding was wrong and is withdrawn.**

It was an artifact of the harness, not of the stores. The two comparands' `AbandonAsync` implementations
were not symmetric: the EF target disposed its test host, which closes the connection its drain writes
through, while the Groundwork target only dropped references and left its drain running. EF was being
prevented from writing and Groundwork was not, which produces exactly the observed 1-versus-2 result
independently of any real durability difference.

The original note claimed stability across three repeated runs. That was true and it was not sufficient:
a deterministic harness asymmetry reproduces perfectly. Once the abandons were made symmetric, the same
probe returned EF `1` twice and EF `2` on the third run — the underlying question is a race, not a
behaviour.

**The dimension is now reported without that fact.** Neither stack exposes a seam that stops a capture
drain without letting it flush, so "does a queued-but-unflushed write survive process loss" is **not
expressible in-process** at this seam. What remains is answerable regardless of who wins the drain race,
and both stacks agree on it: a flushed write is durable (`flushed-write-durable=true`), and neither ever
exposes more writes than were issued (`partial-batch-visible=false`).

Answering the withdrawn question honestly needs a real out-of-process kill — `GroundworkProcessProbeRunner`
already exists in the test infrastructure for exactly this shape of evidence. Recorded as open follow-up.

**Method note worth carrying to the other seams.** Repeat-run stability was used here as the check that a
divergence was real, and it did not catch a systematic harness fault. The stronger check is symmetry:
before recording any divergence, verify that both comparands were actually offered the same opportunity
to exhibit the behaviour.

### Symmetry audit of the other two seams — 2026-08-04

Applied that check to every other recorded finding.

**`IStructuredLogStore` — same asymmetry present, findings unaffected, harness fixed.** Its EF comparand
disposed the host on abandon exactly as OpenTelemetry's did. It produced no false divergence only
because the probe awaits each `AppendAsync`, whose contract completes *after* the append commits — so
nothing was queued-but-unflushed to race over. The result was sound by luck of probe design rather than
by the harness being correct, and a later probe change could have made it silently racy. The abandon is
now symmetric; `acknowledged-writes-durable=true`, `durable-after-abandon=12` and `torn-batch=none` hold
identically on both stacks across three repeated runs, so the recorded zero-divergence result stands
on sound measurement.

**`ITenantMembershipStore` — asymmetry class does not apply; all seven divergences hold.** The contract
has two methods and no drain, so the target exposes no abandon seam at all. Reasoning through symmetry
directly rather than relying on stability: every probe hands both stores the *identical* record through
the *identical* call and compares the readback. The seven divergences are therefore properties of the
stacks, not of the treatment — EF's newline-joined case-insensitive set encoding, its absence of any
ambient scope concept, and `DbContext`'s thread-affinity. Each store also gets a freshly opened
instance per probe on both sides. No finding withdrawn.

### Correction to spec 139's ledger

[`specs/139-.../ef-test-removal-ledger.md`](../139-groundwork-diagnostics-persistence/ef-test-removal-ledger.md)
records `QueryTracesAsync_WhenTraceIdAppearsInMultipleBatches_ReturnsMergedSummary` as **"pending
contract; blocked. Current `LatestPerKeyField` returns only the newer record."**

That is now stale. The `idempotent-replay` probe writes one trace id across two batches and both stacks
return an identical merged summary — earliest start, latest end, worst status, summed span count, union
of workflow ids. `GroundworkOpenTelemetryQueryConformanceTests.Repeated_trace_records_merge_to_one_summary_across_durable_batches`
passes at this head. The 139 row has been corrected in place.

This is the differential doing the job it exists for: a ledger claim about divergence was carried
forward after the underlying behaviour changed, and executing the comparison caught it. Note the
direction — the risk in an inspection-derived ledger is not only missed divergence but **stale
divergence**, which over-reports risk and can block a removal that is actually safe.

## `ITenantMembershipStore` — SQLite, recorded 2026-08-03

Comparands: `efcore.sqlite` (`EfCoreTenantMembershipStore`) and `groundwork.sqlite`
(`GroundworkTenantMembershipStore`), both over real SQLite. The EF side reaches SQLite through the
parameterless `AddFoundationAspNetCoreIdentityEntityFrameworkCore()` registration so that no EF
registration token enters the test project and the fail-closed surface ratchet stays green.

Scope note: this is the **only** Elsa IAM contract the differential may currently touch. `iam-user`,
`iam-role` and `iam-external-identity` are `externally-blocked` in the coverage ledger and the
validator has no transition out of that state; the ASP.NET Core Identity oracle tree is separately
frozen under a content fingerprint.

Executable form: `tests/Elsa/Foundation/Identity/Tests/AspNetCoreIdentity/Differential/`.
Surface digest: `42f863e9213e8bef8c012cba81867f5fbac698df8f2bd830ceb2c0aa456af098`.

**Result: 25 facts compared across 6 dimensions; seven divergences, all `ContractIsGroundwork`.**

| dimension | facts | verdict | disposition | testDisposition |
|---|---|---|---|---|
| `concurrency-conflict-shape` | 4 | `equivalent` | — | `RemovePending` |
| `producer-ordering` | 5 | **`divergent`** (1) | `ContractIsGroundwork` | `RemovePending` |
| `null-and-default-materialization` | 8 | **`divergent`** (5) | `ContractIsGroundwork` | `RemovePending` |
| `rollback-visibility` | 3 | **`divergent`** (1) | `ContractIsGroundwork` | `RemovePending` |
| `restart-observation` | 3 | `equivalent` | — | `RemovePending` |
| `idempotent-replay` | 4 | `equivalent` | — | `RemovePending` |

### Divergence 1 — EF's set encoding is lossy in three ways

`EfCoreTenantMembershipStore` stores `RoleIds` and `DirectPermissions` as a single `'\n'`-joined
column and splits them back with `StringSplitOptions.RemoveEmptyEntries | TrimEntries` into a
`HashSet<string>(StringComparer.OrdinalIgnoreCase)`. Groundwork round-trips the set as stored.

| fact | efObserved | groundworkObserved |
|---|---|---|
| `case-variant-role-count` | `1` | `2` |
| `case-variants-collapsed` | `true` | `false` |
| `padded-role` | `trimmed-or-lost` | `preserved` |
| `embedded-newline-intact` | `false` | `true` |
| `embedded-newline-permission-count` | `2` | `1` |

Three distinct losses from one encoding choice: two role ids differing only in case silently become
one; surrounding whitespace is stripped; and a value containing a newline is split into two values.
The third is the sharpest — a permission string with an embedded newline becomes *two* permissions,
which is a privilege-shaped corruption rather than a cosmetic one.

**Disposition `ContractIsGroundwork`.** Preserving what was stored is the contract; EF's behaviour is
an artifact of joining a set into one column, not a decision anyone took about identity semantics.
Deleting EF removes the lossy encoding, so this does not block removal.

### Divergence 2 — EF accepts a cross-tenant write

| fact | efObserved | groundworkObserved |
|---|---|---|
| `foreign-tenant-write` | `accepted` | `threw:InvalidOperationException` |

Saving a membership whose `TenantId` differs from the caller's scope succeeds on EF and is rejected by
Groundwork's `IdentityPersistenceScopeGuard` before provider I/O. EF's store filters only on the
record's own columns and has no notion of an ambient scope.

**Disposition `ContractIsGroundwork`.** Tenant isolation is the property the scope guard exists to
hold, and deleting EF removes the permissive path.

**Reachability triage (2026-08-03): not reachable from untrusted input; no urgent fix required.**
`shells.Production.json` does enable the EF identity feature, so the permissive store is in shipping
configuration — but nothing routes attacker-controlled data into it:

- `ITenantMembershipStore` is referenced nowhere under `src/Elsa/Foundation/Identity/Api` or
  `src/Elsa/Api`. There is no endpoint that writes a membership.
- The only write consumers are the two seeders, and both take `identityOptions.Value.DefaultTenantId`
  — a configuration value defaulting to the constant `"default"` — never request input.
  `IdentitySeedCoordinator` is in fact Groundwork-only: it casts to the revision-aware contracts and
  throws when they are absent, which EF's stores are, so EF seeds through
  `EntityFrameworkCore/Seeding/IdentitySeeder.cs` instead. Both use the same configured tenant.
- Read consumers (`IdentityClaimsProjector`, the principal factories) pass `user.TenantId` from the
  persisted user record, not from the request.

So this is a **missing defence-in-depth guard on a store with no untrusted write path**, not an
exploitable tenant-isolation hole. It is correctly resolved by the removal programme rather than by a
separate fix. It is recorded because the reasoning is not obvious from the code: anyone adding a
multi-tenant administration endpoint over the EF store would reasonably assume the scope check exists,
and it does not.

### Divergence 3 — EF's store is not usable concurrently through one instance

| fact | efObserved | groundworkObserved |
|---|---|---|
| `concurrent-writes-through-one-instance` | `threw:InvalidOperationException` | `accepted` |

`EfCoreTenantMembershipStore` holds one `DbContext`, which is not thread-safe, so concurrent writers
through a single store instance fault with EF's concurrency detector. Groundwork's store accepts them.
Both stacks converge once the same writes are re-driven serially — `loss=none`, `readable=12` on both
— so this is a threading-model difference, not durability loss.

**Disposition `ContractIsGroundwork`.** EF's constraint is inherent to `DbContext` and is normally
hidden by scope-per-operation registration; it becomes visible only when the seam is driven directly.

### Precondition difference found while building this probe

Groundwork's membership store resolves the owning user document and rejects an orphan membership
(`The requested user does not exist in the current persistence scope.`); EF's has no such link and
persists one happily. This is referential integrity that Groundwork enforces and EF does not.

It is not recorded as a per-fact divergence because it would otherwise dominate all six dimensions and
mask everything else, so the probes seed a user first. Recorded here instead, with the same
disposition — `ContractIsGroundwork` — because an orphan membership is not a state the model should
be able to reach.

## App-level parity — recorded 2026-08-03

The three sections above compare persistence *semantics* at the store contract. This one compares what
an application does: two real HTTP hosts, one composed over EF identity and one over Groundwork
identity, driven through the shared identity surface (`POST /_elsa/identity/login` →
`GET /_elsa/identity/token`) and compared on observable outcome.

It exists because the store probes cannot reach composition: DI wiring, cookie and antiforgery
handling, token issuance, and claim projection are integration behaviour, not storage behaviour.

Executable form: `tests/Elsa/Foundation/Identity/Tests/AspNetCoreIdentity/Differential/IdentityAppParityTests.cs`.

**Result: 13 app-level facts compared; zero divergences.**

| fact | both stacks |
|---|---|
| `token-before-login` | `401` |
| `login-with-wrong-password` | `401` |
| `login-with-correct-password` | `200` |
| `identity-cookie-issued` | `true` |
| `token-after-login` | `200` |
| `token-payload-has-accessToken` / `has-expiresAt` | `true` / `true` |
| `access-token-non-empty` / `validates` | `true` / `true` |
| `token-carries-tenant-claim` | `true` |
| `token-carries-permission-claims` | `true` |
| `token-carries-identity-name` | `false` (shared behaviour, not a divergence) |
| `bearer-authenticates-without-cookie` | `200` |

Comparison is on **shape, not values**: each host seeds its own user, tenant and role, so the facts are
status codes, payload shape, and which claim kinds survive — never a literal username or tenant id.
That is what makes it apples to apples across two independently composed hosts.

### Limitation REINSTATED 2026-08-04 — OpenIddict keeps its own vendor persistence

**The lift below is withdrawn.** A product decision was taken to remove the Groundwork OpenIddict
adapter entirely and let OpenIddict use its own EF Core / MongoDB persistence packages, which are
adequate for anyone enabling OpenIddict. So the Groundwork host composes **EF OpenIddict** again and the
original caveat applies once more: the token-persistence facts compare EF to EF and are not evidence of
anything.

What still holds, and is unaffected: login, cookie issuance and claim projection run through the
identity stores and are genuine two-stack comparisons.

The defect found while the adapter briefly existed was real and its fix is retained — three stores built
a `DocumentQuery` without selecting the `First` result operation, and the test doubles that missed it now
validate it. Both outlive the adapter's removal.

### Previously lifted, then reinstated (retained for provenance)

Getting there found a defect no unit test could see. Three stores built a `DocumentQuery` and passed it
to `FirstOrDefaultAsync` without `Select(BoundedQueryResultOperation.First)`; `ResultOperation` defaults
to `Documents`, and every conformant bounded store rejects the mismatch. It blocked the **first refresh
token OpenIddict ever mints**, so it broke all first-party issuance — while 146 store unit tests stayed
green, because their doubles never inspected `ResultOperation`. The doubles now validate it.

The result matters for the removal programme: **the sixteen members the OpenIddict stores reject are
administrative and pruning paths, not the issuance path.** Token issuance, refresh rotation, logout
invalidation and bearer authentication all work on Groundwork. Replacing EF OpenIddict is therefore not
gated on the five missing declarations, contrary to what the rejected-member count suggests.

### Superseded caveat (retained for provenance)

### Load-bearing limitation: only part of this is a comparison

**OpenIddict is EF-backed on both sides.** `src/Elsa/Foundation/Identity/OpenIddict/Groundwork/` contains
a storage manifest, schema, serializer, query translator and session factory but **no store
implementations**, and the production registration is
`core.UseEntityFrameworkCore().UseDbContext<OpenIddictIdentityDbContext>()`. The Groundwork identity
host therefore also composes EF OpenIddict. Splitting the facts by what actually differed:

| fact | genuinely compares two stacks? |
|---|---|
| `login-with-wrong-password`, `login-with-correct-password`, `identity-cookie-issued` | **yes** — user lookup and password verification run through the identity stores |
| `token-carries-tenant-claim`, `token-carries-permission-claims` | **yes** — projected from the identity stores before issuance |
| `token-before-login`, `token-after-login` | partly — the cookie principal is stack-dependent, the endpoint is not |
| `token-payload-has-accessToken`, `has-expiresAt`, `access-token-non-empty`, `access-token-validates`, `bearer-authenticates-without-cookie` | **no** — token persistence is EF in both configurations |

So the honest reading is: **the identity-store-dependent behaviour of an application is identical across
the two stacks.** The token-issuance half is not yet evidence of anything, because there is no second
implementation of it to compare against. It becomes a real comparison only once #643 lands Groundwork
OpenIddict stores, and this section must be re-run then.

### Why this is the whole app-level surface

Identity is the only lane where two composable stacks exist. Every shipping shell already runs the
workflow runtime, design, publishing, secrets, studio preferences and diagnostics on Groundwork —
`src/Apps/Elsa.Server/shells.json` selects `GroundworkUnifiedPersistenceSqlite`,
`DiagnosticsGroundworkPersistence`, `SecretsGroundworkPersistence`,
`StudioPreferencesGroundworkPersistence` and `WorkflowsPublishingGroundwork`, with
`FoundationIdentityAspNetCoreIdentityEntityFrameworkCore` the sole EF selection.

So there is no "EF-powered app" to compare against: an app on either configuration executes byte-
identical code for every workflow start, resume, checkpoint, bookmark and query. The only surface that
can differ is login, token issuance and user management — which is exactly what this section covers.

### Reading these two layers together

The store differential found 7 divergences the app-level scenario cannot see: they need awkward data
(a role id differing only by case, a permission containing a newline) to surface, and a normal login
sails past them. The app-level scenario covers composition the store probes cannot reach. Neither
subsumes the other, and the pair is what supports the plain-language claim:

> An application's identity-store-dependent behaviour is identical across the two stacks, and where the
> stores themselves differ, every difference favours Groundwork.

**Final position, 2026-08-04.** The narrower claim is the supportable one: an application's
*identity-store-dependent* behaviour is identical across the two stacks. The token-persistence half is
not a comparison, because OpenIddict now deliberately keeps its own vendor persistence rather than
gaining a Groundwork adapter.

**`Microsoft.EntityFrameworkCore*` is therefore permanent in this repository**, reached transitively via
`OpenIddict.EntityFrameworkCore`. That directly contradicts ADR 0042's completion criterion — "no direct
or transitive `Microsoft.EntityFrameworkCore*` dependency remains in `elsa-foundation`, its reference
hosts, or its test graph" — which now needs a targeted amendment rather than being quietly missed.
`OpenIddict.MongoDb` would preserve zero-EF at the cost of requiring MongoDB for the identity lane; it is
not currently referenced.

### Follow-up resolved as not-expressible — 2026-08-04

1. **Internal-commit-retry idempotency cannot be compared symmetrically, and the follow-up is closed as
   `not-expressible` rather than left open.** The interface requires that an accepted append retried
   after *acknowledgement loss* returns the same cursor. Testing that needs a seam where the commit
   succeeds and the acknowledgement is then lost. The two stacks differ in whether such a seam exists at
   all:
   - **Groundwork** acknowledges across a provider boundary (`IDiagnosticRecordStore`), so a decorator
     can commit and then drop the acknowledgement — `AcknowledgementLosingStore` in
     `GroundworkStructuredLogStoreTests` already does exactly this.
   - **EF** acknowledges through a private in-process `TaskCompletionSource` completed immediately after
     `SaveChangesAsync`. Its only injection point, `FaultInjectingFactory`, intercepts *context
     creation* — strictly pre-commit. There is no post-commit, pre-acknowledgement seam to decorate.

   Building the probe on those two primitives would compare a pre-commit failure against a post-commit
   acknowledgement loss — the identical mistake as the withdrawn `rollback-visibility` divergence above,
   where the comparands were not offered the same opportunity. Adding a test seam to EF production code
   solely to compare a store scheduled for deletion is not worth the change either.

   Same-stack coverage exists on both sides and is unaffected
   (`GroundworkStructuredLogStoreTests`, `EfCoreStructuredLogStoreResilienceTests`); what does not exist,
   and cannot without changing EF, is a *differential* over this clause.
2. The four non-SQLite providers have no EF comparand and are covered by Groundwork conformance only.
   That is strictly less evidence than a differential and must not be reported as equivalent assurance.
