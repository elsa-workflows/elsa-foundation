# T006–T007 eligibility implementation checkpoint

The registration carrier captures the uniquely owned EF operational-state descriptors before decoration, then pins the expected decorated groups, stable built-in singleton HMAC codec and shared scoped/singleton access-context accessor. Later changes bypass reuse. Root and independent review accepted this bounded implementation checkpoint; the complete tasks remain open for their outstanding proof.

## Verification

Based on `68359ff961fa59326c4d7f7e0c1755e18ea4e861`:

- Registration source SHA-256: `6b1f831c899a85fddd8f595c9d6dcaebab99c54089f579afd617660b8d0b6bdd`.
- Eligibility tests SHA-256: `84229e6d5b49fd44dbc20f1684581e53842368dc7f72519136d5275c9431ffd0`.
- Root ran the eligibility, feature and memo filters in `Elsa.Workflows.Runtime.Tests.csproj`: 80 passed, zero failed/skipped (53 + 13 + 14). Retained result: `t006-t007-root-final.trx`.
- Root ran `EfDurableValuePageReuseTests` in `Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj`: one passed, zero failed/skipped. The actual CShells EF composition is eligible and its deterministic workflow still completes with the baseline six durable-value page SELECTs, including two nonempty pages. Retained result: `t006-root-real-ef-final.trx`.
- The isolated eligibility matrix tests synthetic ownership metadata and decorations. The real EF fixture separately exercises the production registration and wrapper activation.

The expanded matrix covers backend absence/ambiguity, concrete ownership changes, unsupported accessor/codec composition, descriptor removal/replacement, and malformed decoration. Before the inner-type guard, two new regression cases incorrectly remained eligible: a wrong generic inner service type and a malformed inner/marker collision. Both failed before the correction and passed afterward. The collision case is rejected by the same generic-type guard; it is not independent proof of a correctly typed marker collision. Production uses its own private marker type.

## Remaining proof

The measured registration-class coverage is 92.61% lines and 84.78% branches, not full branch coverage. T007 remains open for reconciliation of uncovered guards with reachable supported compositions and the remaining T014 integration cases. No test or criterion was removed to manufacture a pass.

T006 remains open for the literal requirement that the memo uses the same resolved singleton codec instance as the inner EF store. The carrier is passed to the wrapper, but the wrapper has not yet connected the memo or resolved the live access/codec dependencies. T008–T014 own that integration and its write, cancellation, scope and ownership guards. This checkpoint proves no query reduction, HTTP/REST improvement, final concurrency/recovery outcome, or latency saving. Full feature/provider/architecture/Maps and rebuilt-host gates remain required before delivery.
