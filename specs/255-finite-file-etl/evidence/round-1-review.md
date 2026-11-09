# Whole-PR review round 1

Reviewed committed head: `36420eb8078fe65187edb01cf02a79a1b3f4154a`.
Outcome: changes required. [Public review receipt](https://github.com/elsa-workflows/elsa-foundation/pull/3111#issuecomment-6303936995). T034 is reopened; T035/T036 remain incomplete.

## Accepted corrections

- Refresh both Modularity test lock files after the Workbench dependency change. Current-head backend CI failed locked restore before tests. Both targeted locked restores now pass locally.
- Register the ETL Studio module in the current package inventory as a post-consolidation addition. Do not rewrite the frozen historical layout. The verifier retains exact source/project/package matching and exercises incorrect provenance, missing/extra/duplicate packages and inconsistent versions. Local pack and verification accepted exactly 19 packages with matching version, MIT license and icon; nothing was published.
- Discover durable terminal delivery independently from current file-binding scopes. Keep preparation and partition execution scoped, validate the retained invocation/address, preserve delivery fences and leave invalid work pending. A malformed earliest candidate must not prevent delivery for other scopes.
- Distinguish an output revision removed after admission (destination fault) from a pre-admission binding refusal (business error).
- Make the new ETL feature classes inheritable with virtual configuration methods, as required by the feature contract.
- Resolve all three publishing routes through the same normalized trusted identity used by permission evaluation. A secondary identity must not override the compilation, review or publication tenant.
- Independently prove durable byte-pressure refusal while record capacity remains, and simultaneous real two-process partition ownership with survivor recovery.

## Requirement reconciliation

FR-007 previously said every invocation owns at least two partitions, while its assumptions, plan, Studio wording and partition plan already permit empty/tiny inputs and explicitly requested single-partition runs. Root explicitly amends that universal wording under the owner's standing design authority: when both requested count and record count are at least two, at least two nonempty independently leased partitions are required. Empty or single-partition runs do not receive artificial empty ranges. **SC-002 remains unchanged:** qualification requires two actual nonempty partitions progressing independently and second-worker recovery. This is a documented requirement correction, not a claim that one-partition tests satisfy SC-002.

The existing-domain public API baseline concern is covered by #2591's phased activation. A separate root check found that the new Studio ETL package falls outside that backend guard and lacked tracking. This package now declares its analyzer and reviewed API files explicitly; removing its constructor baseline fails with RS0016 and restoring the file passes. This PR does not activate unrelated existing domains.

## Executed checks so far

Publishing API: **813 passed, zero failed/skipped**, after byte-exact restoration of the shared tenant helper. The focused baseline passed; replacing normalized selection with raw principal tenant lookup failed precisely with expected `tenant-a`, actual `tenant-b`. Restored SHA-256: `7ebb2b1475ff2d847427a927131c588fa975984e958edc52bf60acf6016a3a36`.

The first new-test runs found fixture defects: invalid nested byte ceilings, an owner assertion after terminal settlement clears the lease, an overly specific acknowledgement disposition, and a 201 expectation where the publishing capture response declares an existing publication (200). These failed runs are retained, not counted as passes. The native helper still requires the exact consumed receipt, retired bookmark, child/sibling/parent completion and persisted acknowledgement; it allows the already-established terminal-parent proof when scheduling completes before acknowledgement.

ETL: **374 passed, zero failed/skipped** after exact restoration of the durable byte condition. With record capacity still available, removing only that byte condition fails with expected `BudgetExceeded`, actual `Applied`. Restored store SHA-256: `7cba3753311021ede8f2a7fec543f63e4b55d2d58b692e58b2022e1e50f21042`.

The corruption-isolation case was then strengthened to a one-row page size. It passes across the page boundary; replacing candidate isolation with immediate rethrow fails with the retained scope-mismatch error, and byte-exact restoration passes again. The malformed candidate remains pending/unowned/unacknowledged while the valid later scope receives its claim.

Architecture: **583 passed, zero failed/skipped**. Navigation maps were regenerated; committed dependency-data freshness, solution filters and catalog links pass. After the isolated Studio API-tracking addition, its Release build, API-baseline negative control and exact restoration pass; the Studio restore command with `--locked-mode` and refreshed maps/filter checks pass too. Studio's independent build props do not currently opt its projects into lock-file generation, so that restore command is not claimed as per-project locked-dependency evidence. The two server-side Modularity lock files are present and their locked restores passed.

The complete Studio .NET suite passed **462 tests, zero failed/skipped** with the isolated ETL API tracking enabled.

A bounded repair review found that the scoped delivery path, including the earlier committed implementation, lacked an explicit comparison between the retained invocation's run ID and the durable row's run ID. The global path already checked it. The comparison is now shared by both paths. A two-case regression consistently recomputes the envelope hash after changing only the retained invocation identity: before the fix, the scoped case fails because no exception is thrown, while the global case passes. The missing-binding native journey also now compares the completed activity's typed result with the persisted refusal result, in addition to checking receipt consumption and acknowledgement.

After the shared identity check, both identity cases and the strengthened native refusal journey pass: **3 passed, zero failed/skipped**. Final store SHA-256 at this repair checkpoint: `2e3f9a107c408513e57673651a2a5efcbd8018ac2aa5860059c4cbad234a9743`. The earlier 374-test run and negative-control hashes describe the preceding repair state; integrated full-suite qualification remains required.

Workbench rebuilt successfully before that final shared identity check (zero errors, 92 warnings). The final HTTP qualification will use the integrated main revision, not this intermediate build.

Rebuilt Workbench HTTP regression after these repairs, integration with newer main, final review and fresh-head CI remain pending. Earlier whole-suite evidence belongs to its recorded source state and is not a green gate for these changes.
