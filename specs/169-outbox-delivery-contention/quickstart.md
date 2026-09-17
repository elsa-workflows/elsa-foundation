# Quickstart: Outbox Delivery Contention

**Feature**: 169-outbox-delivery-contention · **Branch**: `1305-outbox-delivery-contention`

For whoever picks up the implementation. Read this before `/speckit.tasks`.

---

## The one-paragraph version

The live-drain delivery path records results without presenting an owner or fencing token. The background resumption sweep claims rows across *all* executions on a timer. When the sweep claims a row between the live drain's read and its record, the store rejects the recording and the workflow start answers 500. Give the recording contract an outcome to return, return `SupersededByOtherOwner` instead of throwing, and stop offering fenced rows to the claim-less path in the first place.

---

## Four things that will mislead you

**1. The obvious fix is already in the code.** The deliverable candidate selection *already* excludes `Delivering` rows — `DeliverableAt` returns `null` for them. If you "fix" that, you will change nothing and the defect will come back. The race is between the read and the write; only a write-side change closes it.

**2. There are two bugs here, not one.** Besides the race, the fencing token never resets. Any row that was *ever* claimed becomes deliverable again while still carrying a fence, and the claim-less record then throws **deterministically, first try, no concurrency**. Both are fixed; don't stop after the race.

**3. There are three `ownerId` test sites, not one** — a whole test method (needs architect approval to remove), an assertion pair, and a validation-guard line. Only the first is a §2.21.1 test removal.

**4. The issue's repro is wrong and the reporter has corrected it.** "Ten concurrent starts, single starts stay clean" is not right — a single start reproduces it when background drain pressure exists (a recurring timer is enough). A test that starts N workflows and asserts N instances **would miss the case that reproduces most often**.

---

## Where the code is

| What | Where |
|---|---|
| The throw being replaced | `EfRuntimePostCommitOutboxStore.RecordDeliveryResultAsync` — the `Status == Delivering \|\| DeliveryFencingToken > 0` guard |
| The claim-less branch | `RuntimePostCommitOutboxProcessor.ProcessAsync` — the `else` after `DeliversInMemory(request)` |
| The competing deliverer | `RuntimeResumptionService.SweepAsync` — calls the processor with **no** execution or intent filter, on the claim path |
| The false invariant to correct | `RuntimeLiveDrainDeliveryScope` class doc — claims no other deliverer competes |
| The dead parameter to delete | `RuntimePostCommitOutboxQuery.OwnerId` + both `NotSupportedException` sites |
| Precedent for the outcome shape | `IRuntimePostCommitOutboxClaimCompletionStore.CompleteClaimAsync` already returns an outcome enum |

Three implementations change: the EF store, the in-memory store, and the coalescing overlay decorator (which must **propagate** the inner outcome, not discard it).

---

## Build and test

Build the runtime domain and its tests:

```bash
dotnet build src/Elsa/Workflows/Runtime/Elsa.Workflows.Runtime.csproj
```

Run the unit lanes that gate this change:

```bash
dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj
```

The container-backed provider lane skips gracefully when no container runtime is present:

```bash
dotnet test tests/Elsa/Workflows/Runtime/Persistence/EntityFrameworkCore/ProviderTests/
```

> This machine may be shared with parallel sessions — see `docs/agents/` on building without colliding.

---

## The acceptance gate — do not skip this

**Every new test must be proven to fail without the production change.** Not "should fail" — observed failing, and recorded in the PR.

```bash
# 1. Implement the fix. Run the new tests. They pass.
# 2. Revert ONLY the production change, keeping the tests:
git stash push -- src/
# 3. Re-run. They MUST fail. If any passes, that test is worthless — rewrite it.
dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj
# 4. Restore:
git stash pop
```

This is not ceremony. The reporter has had **four consecutive fixes** whose first-draft tests passed without the production change. A test that cannot fail is not evidence.

Note that the *fence* test (FR-017) and the *race* test (FR-016) guard different production changes — revert them independently, or you will not know which test guards which fix.

---

## What is not in this unit

- **The duplicate-key log noise.** Already handled correctly by the work-queue store; it is EF's logger printing before the catch runs. Expected to get quieter as a side effect, but **not diagnosed** — two other routes produce an identical line.
- **The missing concurrency guard on the EF claim-completion path.** Real, separate, filed as its own unit (D7). Do not fold it in — it would blur the revert-to-red evidence.
- **A host-level concurrent-start test.** No host fixture exists to build it on, and it would miss the single-start case anyway.

---

## Before you merge

- [ ] Every new test observed red on revert, recorded in the PR body (FR-019)
- [ ] **Architect approval for the removed test recorded in the PR body** — §2.21.1 requires this explicitly, and it is the one gate obligation code cannot discharge
- [ ] **Every `==` comparison against the outcome enum audited by hand.** There is no compiler help: no `switch` over it exists, and `Directory.Build.props` is warnings-only with no `TreatWarningsAsErrors`. `RuntimePostCommitOutboxProcessor.cs:207` is the one that is wrong if you skip it
- [ ] Breaking-change note for **external** callers added: deleting `OwnerId` shifts `intentKind` from the fifth to the fourth parameter, so a four-positional call reinterprets silently. Audited: **no site in this repository** passes four positional arguments, so internal code is safe and all three `ownerId` references fail loudly at compile time
- [ ] No FluentAssertions; xunit `Assert.*` only
