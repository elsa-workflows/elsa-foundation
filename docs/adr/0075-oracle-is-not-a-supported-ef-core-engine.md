---
status: accepted
date: 2026-09-19
decision_context: Owner decision on issue #1803 after the Elsa 3 versus Elsa 4 Oracle assessment posted there, closing the Elsa 3 parity gap ADR 0073 D1 left unaddressed.
---

# Oracle is not a supported first-party EF Core engine

Status: accepted (2026-09-19). Sipke Schoorstra accepted the recommendation that came out of the
#1803 assessment: keep the supported set at four engines, say so where an operator will read it, and
record a named condition for reopening the question.

Program goal: [EF Core Persistence](../program-goals/ef-core-persistence.md).

Tracking:

- [Decide whether Elsa 4 supports Oracle in EF Core #1803](https://github.com/elsa-workflows/elsa-foundation/issues/1803)
- [Program #1665](https://github.com/elsa-workflows/elsa-foundation/issues/1665)
- [#1837](https://github.com/elsa-workflows/elsa-foundation/issues/1837), the ordinal-collation fix a future Oracle engine inherits
- [#1801](https://github.com/elsa-workflows/elsa-foundation/pull/1801) and [#1816](https://github.com/elsa-workflows/elsa-foundation/pull/1816), the wrapped-transient chain-walk a future Oracle engine inherits

[ADR 0073](0073-ef-core-is-the-only-first-party-persistence-family.md) D1 named SQLite, SQL Server,
PostgreSQL and MySQL as the supported relational engines. It did not say what that means for the
Elsa 3 users who run `Elsa.Persistence.EFCore.Oracle`, and the four-engine list lived only in an ADR.
This decision constrains the engine set and its documentation; nothing else in ADR 0073 is reopened,
and D1's selection is not amended.

[ADR 0074](0074-first-party-ef-stores-retry-in-bounded-application-loops.md) is the other follow-up
that constrains one ADR 0073 question without reopening the rest.

## Context

Inventory snapshot: `main` at `29d45e732a1fcfd6378722b2262b837c169de8a9`.

**The mechanical cost of a fifth engine is bounded and knowable.** There are 13 Elsa module EF
contexts, each with four sealed provider-derived subclasses, and 171 committed migration and snapshot
files under `src/**/Migrations/` (41 SQLite, 43 SQL Server, 43 PostgreSQL, 41 MySQL). Provider
dispatch is positional four-arity, `EfRelationalProviderBinding.Select<T>(provider, owner, sqlite,
sqlServer, postgreSql, mySql)` at `EfRelationalProviderBinding.cs:107`, over four `ProviderEngine`
descriptors at `:28-48`, so a fifth engine changes that signature and the compiler then requires an
edit at every module registration. The program that built all of this closed on 2026-09-16, so the
context set has stopped moving and the cost is a one-off rather than a multiplier against a growing
target. None of this is the reason for the decision.

**Elsa 4 writes almost no provider-specific SQL.** Across `src/` there are no `FromSqlRaw`,
`FromSqlInterpolated`, `ExecuteSqlRaw`, `ExecuteSqlInterpolated` or `migrationBuilder.Sql(` calls, and
no `IsSqlite()`-style provider branching in any store. The one place a provider's dialect is written
out is the `ContentAuthorityIsValid` computed column in Activities Design, and that is now a clause
enumeration rather than one long predicate per provider: `ActivityAuthorityCheck` names each property
the marker proves, each provider supplies SQL per member
(`ActivityAuthorityValiditySql.cs:142`, `:176`, `:210`, `:252`), and
`ActivityAuthorityValidityTests.cs:15,35` fails when a provider leaves a check unanswered.

**Two of the three things Elsa 3 could not seed have since been built for the other four engines.**

1. Ordinal collation. Elsa 3's Oracle module configures no collation at all. Elsa 4's string
   comparison and keyset ordering depend on one, and #1837 established the mechanism: one binary
   collation constant per provider and one `Apply`, per column rather than at model level
   (`EfOrdinalCollation.cs:27`, `:30`, `:33`, `:40`, `:73`), proved against generated migrations and
   against real SQL Server and PostgreSQL databases created with a linguistic default.
2. Wrapped transients. #1801 and #1816 established that a store classifies from what `SaveChanges`
   reported by walking the exception chain (`EfRelationalExceptionClassifier.IsSaveConflict` at
   `:72`), because the non-retrying default strategies of SQL Server and PostgreSQL wrap a transient
   error in an `InvalidOperationException`. ADR 0074 D3 makes that a standing rule for new stores.

A future Oracle engine inherits both as prerequisites rather than starting cold. What neither supplies
is Oracle's own error codes: `EfRelationalExceptionClassifier` carries per-provider arms for
unique-key violations (`:30-50`) and for transient conflicts, deadlocks and lock timeouts included
(`:97-111`), and Elsa 3 maps exactly one Oracle code, `ORA-00001`
(`Elsa.Persistence.EFCore.Common/DbExceptionClassifier.cs:60`), with no Oracle arm in its transient
set at all. There is nothing to port.

**Elsa 3's Oracle lane never met the bar Elsa 4 holds every engine to.** The `ef-container-suites`
matrix on the PR gate exists to give each EF test project that starts Testcontainers its own leg,
fifteen of them today (`.github/workflows/ci.yml:147-197`), precisely because the fast "Build & test"
lane excludes any project referencing Testcontainers and "without this job these suites would run in
no gate at all" (`ci.yml:140-141`). Fixtures pin exact image tags. Most legs also carry a
`require` variable that arms the suite's required-provider check, and the comment says why in terms
that decide this ADR: it is "measured per project rather than assumed: a fixture that reads a
different name would otherwise leave the lane silently skipping while the job still reported green"
(`ci.yml:143-146`).

A lane that skips silently while reporting green is exactly what Elsa 3's Oracle lane is. Its coverage
is gated behind an operator-supplied connection string
(`ConformanceProviders.cs:29`, `RelationalUserTaskStoreFixtures.cs:26`), has no Testcontainers
fixture, and appears nowhere in Elsa 3's `.github/workflows/`. What that costs is on the record:
`V3_6OracleMigrationTests.cs:9-18` records that **both** V3_6 Oracle migrations were generated as
in-place LOB alterations, which Oracle refuses (ORA-22858, ORA-22859), so "neither could ever apply."
Nothing caught it until someone tried to run one.

**Oracle's own mechanics are a standing cost, not a one-time translation.** Elsa 3's
`MigrationHelper.cs:10-15` records why: Oracle commits implicitly before and after every DDL
statement, so a migration that fails halfway leaves its executed statements committed and EF's
migration transaction cannot roll them back. Every helper there is therefore written to be re-runnable
off `ALL_TAB_COLUMNS` or `ALL_INDEXES` (`:61`, `:168`, `:205`, `:232`, `:264`, `:303`, `:328`), and it
escapes its own identifiers and literals (`:315`, `:321`) because it builds PL/SQL text by hand.
Oracle also diverges on uniqueness semantics: "Oracle alone rejects null-tenant duplicates"
(`EFCoreSecretRepository.cs:119-124`).

**The provider package is proprietary, and the binding already limits where that lands.**
`Oracle.EntityFrameworkCore` ships under the Oracle Free Distribution, Hosting, and Use Terms
(`<authors>Oracle</authors>`, `<license type="file">LICENSE.txt</license>`), which permit use and
redistribution of the unmodified program subject to shipping the license, not charging additional fees
for the program itself, retaining proprietary markings, and complying with export-control and sanctions
law. All four current engines ship under OSI licenses. Because `EfRelationalProviderBinding` resolves
`Use*` by reflection and no module project references an engine package, those terms would attach to
`Elsa.Workbench`, the EF tooling, the test projects and the CI images rather than to Elsa's published
packages. That is a materially smaller surface than it first appears, and it is still a review the
other four never required.

**Elsa 4 never reads an Elsa 3 database.** The Elsa 3 compatibility boundary takes definitions through
`IActivityCollectionJsonSource`, a UTF-8 JSON stream of Elsa 3 workflow definitions, and applies them
as a reviewed dependency-closed mutation. Combined with ADR 0073 D5's pre-GA clean break, an Elsa 3
Oracle user's route to Elsa 4 does not require Elsa 4 to speak Oracle.

## Decision

### D1 — The supported relational engines remain SQLite, SQL Server, PostgreSQL and MySQL

Oracle is not a supported first-party EF Core engine. `EfProviderNames`,
`EfRelationalProviderBinding.Select`, and every module's provider-derived context set stay at four.
A host that names Oracle is refused by the existing binding message, which lists the four it accepts
(`EfRelationalProviderBinding.cs:116-118`). That refusal is already pinned, for Oracle by name:
`EfRelationalProviderBindingTests.cs:32-35,52-54` asserts that `Normalize` passes `oracle` through
unchanged and that `ExpectedProviderName`, `Use` and `Select` each throw on it.

This restates ADR 0073 D1's boundary rather than narrowing or widening it. D1 is not amended.

### D2 — The reason on the record is the standard, not the effort

Elsa 4 holds every supported engine to a container leg on the PR gate, on pinned images, with the
matrix built so that a lane which skips while reporting green is treated as a defect. Elsa 3's Oracle
lane is that defect by construction, and the two V3_6 migrations that could never apply are what it
costs in practice. Shipping a fifth engine visibly weaker than the other four would be a worse deal for Oracle
users than telling them plainly that Oracle is unsupported, because a weak lane invites a production
deployment onto migrations nobody has run.

The mechanical work of a fifth engine is real but bounded, and it is explicitly **not** the reason for
this decision. Anyone revisiting this should argue about the standard in D4, not about file counts.

### D3 — The unsupported status and the Elsa 3 route are stated where an operator reads them

The four supported engines, Oracle's absence from that list, and the migration route are stated in
[the EF persistence README](../../src/Elsa/Persistence/EntityFramework/README.md), beside the provider
tables an operator already consults, not only here.

The route is: export workflow definitions from Elsa 3 as JSON, install Elsa 4 fresh on one of the four
supported engines, and import through `Elsa3.Activities.Design.Import`. Because Elsa 4 never connects
to an Elsa 3 database, this works without Oracle support.

Its limit is stated in the same breath, because it is a real one. An organization whose database
policy forbids introducing PostgreSQL, SQL Server or MySQL has no route: for them Oracle being
unsupported is a hard no rather than an inconvenience, and the documentation says so rather than
implying a workaround exists.

### D4 — Revisit condition

This decision is revisited when **all three** of the following hold together. Any one or two of them
is not grounds to reopen it.

1. **A named adopter committed to Elsa 4 on Oracle**, who will supply and run the Oracle container leg
   against pre-release builds. Not an expression of interest: the party that runs the leg.
2. **Agreement to carry that leg in the `ef-container-suites` matrix** (`.github/workflows/ci.yml`)
   on the same terms as the other four, the database image, its distribution terms, and the suite's
   `require` variable included. An engine whose leg is not on the gate, or whose leg can skip while
   reporting green, is the Elsa 3 arrangement under a new name, which D2 rejects.
3. **A bounded spike that resolves the three items Elsa 3 cannot seed**, completed before any
   implementation-ready task is created:
   - Oracle's deadlock and lock-timeout error codes for
     `EfRelationalExceptionClassifier.IsTransientWriteConflict`, plus whether the Oracle provider's
     execution strategy wraps a transient error the way SQL Server's and PostgreSQL's do, which is
     what `IsSaveConflict` depends on (ADR 0074 D3).
   - An ordinal or binary collation equivalent for the 13 contexts: whether Oracle exposes a
     per-column binary collation `EfOrdinalCollation` can carry as a fifth constant, or whether its
     `NLS_SORT` and `NLS_COMP` model requires a different mechanism.
   - An Oracle clause list for `ActivityAuthorityValiditySql`, answering every `ActivityAuthorityCheck`
     member against Oracle's JSON functions and `NCLOB`, including whether the column can be a stored
     computed column as it is on PostgreSQL.

The spike answers those three; it does not implement Oracle. Items 2 and 3's collation and
chain-walk prerequisites already exist for the other four engines (#1837, #1801, #1816), so the spike
starts from a mechanism rather than from nothing.

## Consequences

Positive:

- One documented supported set, stated where an operator reads it rather than only in an ADR.
- Elsa 3 Oracle users get the route that actually exists, with its limit named rather than glossed.
- No fifth engine ships weaker than the other four, and no deployment lands on migrations that no CI
  leg has ever applied.
- The four-arity binding, the four-engine classifier arms, and the four provider clause lists stay as
  narrow as they are.

Costs and risks:

- An organization whose database policy forbids PostgreSQL, SQL Server and MySQL cannot adopt Elsa 4.
  This decision does not soften that and does not pretend a workaround exists.
- The Elsa 3 to Elsa 4 route is a fresh install plus a definition import, not a data migration. Runtime
  and instance history in an Elsa 3 Oracle database does not come across, which follows from ADR 0073
  D5 rather than from this decision.
- The three spike items stay unanswered, so if the revisit condition is met the answer is a spike
  rather than an estimate. That is deliberate: the items are correctness questions whose failure mode
  is silent.

## Decision record

| Date | State | Record |
|---|---|---|
| 2026-09-12 | Boundary drawn, gap unaddressed | ADR 0073 D1 named four supported engines and said nothing about Elsa 3's Oracle users. |
| 2026-09-17 | Assessment posted | Issue #1803 recorded what a fifth engine costs in Elsa 4 and what Elsa 3 actually did for Oracle, with file and line evidence on both sides. |
| 2026-09-17 | Recommendation | Defer with a named trigger, and do the unsupported-status documentation now. The deciding factor was the three items Elsa 3 cannot seed, not the migration file count. |
| 2026-09-19 | ADR 0075 accepted | Sipke Schoorstra accepted the recommendation. D1 stays at four engines, the status and the Elsa 3 route are documented, and the trigger is recorded as a three-part condition. |
