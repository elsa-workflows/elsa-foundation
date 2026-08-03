# Divergence ledger — EF Core vs Groundwork behavioural differential

Work unit: `094-harden-groundwork-stores` (artifact), executed under [Elsa #646](https://github.com/elsa-workflows/elsa-foundation/issues/646).
Consumed by: [spec 144](../144-zero-ef-final-removal/) T011.
Status: structured-log seam recorded. OpenTelemetry and IAM seams pending.

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
| Diagnostics | `IOpenTelemetryStore` | pending |
| Identity (Elsa IAM) | `ITenantMembershipStore` | pending |
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
(`file-backed-distinct-connections-with-retained-ef-oracle`).

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

### Open follow-up for this seam

1. Internal-commit-retry idempotency needs a failure-injecting provider double shared by both stacks.
2. The four non-SQLite providers have no EF comparand and are covered by Groundwork conformance only.
   That is strictly less evidence than a differential and must not be reported as equivalent assurance.
