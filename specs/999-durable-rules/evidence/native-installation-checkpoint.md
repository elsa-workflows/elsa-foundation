# Additive Rules installation with retained native state

The owned package-backed Workbench proves the native half of T025/T026's upgrade acceptance.
The [receipt](native-installation-checkpoint-receipt.json) records exact inputs, commands' logs,
retained snapshot paths/hashes and assertions. The paired [retained Rules deployment proof](retained-upgrade-checkpoint.md)
completes T025/T026; final leaf qualification remains a separate gate and this checkpoint does not close #3181.

## Executed boundary

The host is the already-qualified six-module deployment from immutable product source
`26c645ba4691ac0634ae3232f9bd3868f1f72ecd`, not a newly built product. Its Workbench DLL, all six Rules
DLLs, deployment receipt and qualification manifest were rechecked against retained hashes before use.
Rules assemblies are available in the host, but no Rules feature is enabled during the first process.
This proves feature/schema installation, not an upgrade from an earlier binary release.

1. Create an owned content root and SQLite database; publish and start a native Sequence that stores
   a unique variable, waits on an Event and later exposes that variable through SetOutput.
2. Stop the owned process with one suspended Event, one completed Set and no following output.
   A read-only SQLite snapshot requires one workflow, one bookmark, persisted activities and an
   executable artifact. No Rules tables exist. It retains all rows of the native workflow, activity,
   bookmark, durable-value and executable tables, plus all existing database schema objects.
3. Enable the actual Rules features and migrate into **the same database**. Start another process,
   read the same waiting workflow and resolve RunRules from the actual catalog. Stop that process.
   The second snapshot proves all prior schema objects and the captured native rows are identical.
   Rules adds 12 schema objects, including its three data tables and module-specific history,
   identity and finalization tables. There are no Rules runs yet.
4. Start a third process against that database and resume the original Event. Exactly one workflow
   resumes and completes with the original variable, one Set and one following output activity.
   A duplicate ResumeOnly stimulus resumes zero workflows; no additional output activity appears.

The snapshots include the original execution and artifact identities, bookmark payload, activity
state and inline variable values. They compare actual stored content; matching API status alone
is not accepted as preservation proof. No schema rollback, repinning, identity override or native
runtime change was used.

## Verification and review

`e2e-tests/rules/Test-NativeRulesInstallation.ps1` passed the complete journey. Raw snapshot files
remain outside Git with hashes and row counts in the receipt. Both are about 250 KB because they
include the existing shared database schema. Failed fixtures retain their database/logs; successful
fixtures remove only their owned content root after stopping their own server.

The first attempt correctly stopped at a faulty probe assumption: it required a nonempty separate
durable-value table. Inspection showed three persisted activities, one workflow, one bookmark and
one artifact; this workflow's variable is inline in activity state. The corrected probe still
captures and compares the durable-value table, including its empty state, and verifies the original
variable after actual resumption. The runtime was unchanged.

Shared Rules configuration/assertions were extracted into `_RulesCommon.ps1`. The existing
package-backed REST journey was rerun after extraction: all four success/fault/refusal scenarios
and terminal restart readback passed. Python compilation, PowerShell parsing and diff checks passed.
Read-only independent review found no concrete proof or ownership issue. Root review also added the
new script to the README's separate-package qualification list, so it cannot be silently counted
by the generic source-only loop. The Workbench package stage now invokes the installation journey
and records a separate `nativeInstallationPassed` result.

The prior full Workbench suite remains 61 passed / 1 failed, with an isolated 1/1 rerun. Its SQLite
lock source remains unproven. This targeted acceptance pass does not replace that unresolved full-suite
evidence, the separate four-provider matrix, or final integrated gates.
External effects remain at least once.
