# Contract: Post-Commit Outbox Delivery Recording

**Feature**: 169-outbox-delivery-contention · **Date**: 2026-09-17

This is the contract surface the Elsa foundation exposes to anyone implementing a post-commit outbox store. It is a **breaking change**, accepted as decision D3.

---

## 1. `IRuntimePostCommitOutboxStore` — breaking change

### Before

```csharp
public interface IRuntimePostCommitOutboxStore
{
    ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> GetDeliverableAsync(
        RuntimePostCommitOutboxQuery query, CancellationToken cancellationToken = default);

    ValueTask RecordDeliveryResultAsync(
        RuntimePostCommitOutboxDeliveryResult result, CancellationToken cancellationToken = default);
}
```

### After

```csharp
public interface IRuntimePostCommitOutboxStore
{
    ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> GetDeliverableAsync(
        RuntimePostCommitOutboxQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a claim-less delivery result. Returns <see cref="RuntimePostCommitOutboxClaimCompletionOutcome.Persisted"/>
    /// when the result was written, or <see cref="RuntimePostCommitOutboxClaimCompletionOutcome.SupersededByOtherOwner"/>
    /// when another deliverer owns the item, in which case NOTHING is written and the owner's completion governs.
    /// </summary>
    ValueTask<RuntimePostCommitOutboxClaimCompletionOutcome> RecordDeliveryResultAsync(
        RuntimePostCommitOutboxDeliveryResult result, CancellationToken cancellationToken = default);
}
```

**Migration for an external implementer**: change the return type and return `Persisted` where the method previously completed. If the implementation currently throws when the item is claimed or fenced, return `SupersededByOtherOwner` instead.

**Why this is not an optional interface.** An optional capability can be declined, and a store that declines it reproduces this defect exactly. That is not hypothetical: this contract family already carries four additive interfaces and still arrived at a store unable to say "not mine", which is why the defect survived the Groundwork removal. A required signature change makes the omission a build error.

---

## 2. `RuntimePostCommitOutboxQuery` — breaking change

`OwnerId` is **removed**, along with its validation guard.

```csharp
// Before
public RuntimePostCommitOutboxQuery(
    DateTimeOffset now, int limit,
    string? workflowExecutionId = null,
    string? ownerId = null,          // ← REMOVED
    string? intentKind = null)

// After
public RuntimePostCommitOutboxQuery(
    DateTimeOffset now, int limit,
    string? workflowExecutionId = null,
    string? intentKind = null)
```

### Positional hazard — audited, does not materialize in this repository

`intentKind` moves from the fifth to the fourth parameter. A call site passing **four positional arguments** would silently reinterpret its fourth argument (`ownerId` → `intentKind`) instead of failing to compile.

**All 63 construction sites were audited.** The maximum positional-argument count anywhere in `src/` or `tests/` is **three**. No site is at risk, and no silent reinterpretation is possible here.

Three sites reference `ownerId` **by name**, so all three become **compile errors** — loud, not silent:

| Site | Shape | Disposition |
|---|---|---|
| `tests/…/EntityFrameworkCore/Tests/EfRuntimePostCommitOutboxStoreTests.cs:87-88` | Two assertion lines inside a larger, surviving test | Delete the assertion; the enclosing test keeps its other objectives |
| `tests/…/Runtime/Tests/RuntimePostCommitOutboxStoreTests.cs:36-47` | A **whole dedicated test method** asserting the in-memory refusal | **Test removal — §2.21.1 architect approval required** |
| `tests/…/Runtime/Tests/RuntimeOperationalRecoveryOutboxContractTests.cs:207` | One line asserting the blank-`ownerId` `ArgumentException` guard | Delete with the guard it asserts |

**External callers get no such protection.** An external caller passing four positional arguments reinterprets silently. This must be called out in release notes for the breaking change.

**Why removed rather than implemented.** No production caller sets it. Every store that has ever implemented this interface — EF, in-memory, and Groundwork before removal — rejects it with the same sentence. The parameter reads as the ownership hook, so each new store reimplements the refusal instead of solving the problem. Deleting it removes the invitation. Implementing it was explicitly rejected (FR-013): it would create an untested surface no caller needs.

---

## 3. `RuntimePostCommitOutboxClaimCompletionOutcome` — additive

```csharp
public enum RuntimePostCommitOutboxClaimCompletionOutcome
{
    Persisted,
    DeliveredOnChildEvidence,

    /// <summary>
    /// Another deliverer owns this item — it is Delivering under a different owner, or carries a fencing token from a
    /// claim this caller does not hold. NOTHING was written: status, owner, fence, attempt count and failure message
    /// are untouched, and the owning deliverer's completion governs. The item remains a crash backstop, recoverable
    /// by claim expiry and the resumption sweep.
    /// </summary>
    SupersededByOtherOwner
}
```

### Enum-extension hazard — **no compile-time signal exists**

Extending an enum usually gets caught by a non-exhaustive `switch` warning. **That safety net is absent here, on two counts:**

1. **No `switch` over this enum exists anywhere.** Every consumer compares with `==`.
2. **Warnings are not errors.** `Directory.Build.props` states it explicitly — *"Everything is warnings-only on purpose: no TreatWarningsAsErrors here."* Even if a `switch` existed, it would emit a warning nobody is forced to read.

So adding `SupersededByOtherOwner` produces **zero** build-time signal. Every `==` site silently takes its `else` branch for the new value. Each must be audited by hand:

| Site | Current expression | Risk if left unaudited |
|---|---|---|
| `RuntimePostCommitOutboxProcessor.cs:207` | `outcome == DeliveredOnChildEvidence ? Delivered : effectiveStatus` | **HIGH** — a superseded item would be logged with the *failure* status, misreporting an item this deliverer never wrote |
| `RuntimePostCommitOutboxProcessor.cs:280` | `dispatchFailure is not null && outcome == Persisted` | Low — correctly skips incident logging for a superseded item, but must be confirmed deliberate, not accidental |
| `RuntimePostCommitOutboxProcessor.cs:248` | returns `Persisted` alongside a captured exception | Must not be confused with a genuine supersession |

This is the single most likely way for this change to ship a silent defect. It is tracked as its own task, not folded into the implementation tasks.

---

## 4. Behavioural contract for implementers

An implementation of `RecordDeliveryResultAsync(result, ct)` MUST:

| # | Obligation |
|---|---|
| C1 | Return `SupersededByOtherOwner` when the item is in delivery under another owner, or carries a fencing token this caller does not hold. |
| C2 | Write **nothing** when returning `SupersededByOtherOwner` — no status, owner, fence, attempt count, failure message or availability change. |
| C3 | Leave a superseded item recoverable by claim expiry and the resumption sweep. |
| C4 | Continue to **throw** when the item is already terminal. That is double-completion, not contention, and must stay loud. |
| C5 | Continue to **throw** when the item does not exist. |
| C6 | Return `Persisted` when the result was written as presented. |
| C7 | Not consume a delivery attempt when returning `SupersededByOtherOwner`. |

A decorating implementation MUST propagate the inner store's outcome unchanged rather than discarding it.

---

## 5. Consumer contract — the processor

The delivery processor MUST classify a superseded item as **neither delivered nor failed**:

- It MUST NOT count toward the delivered count that the drain orchestrator uses as its loop-continuation signal (FR-004). Counting it would keep the drain cycling on work another deliverer already owns.
- It MUST NOT count toward the failed count or set a delivery-failed stop reason (FR-005). The item is not lost; it is being delivered by its owner.

A drain cycle in which every item is superseded therefore reports zero delivered, and the drain quiesces — which is correct.
