# Review round 1 — source feedback recorded; acceptance pending

This records the first feature source-review round and its earlier scoped cancellation receipt. Fresh correctness and repository-standards reviews are complete at 8ba; two standards findings require a scoped second source round. Acceptance and final UX review remain open. No merge approval is asserted, and the six-round count is not reset.

## Fresh feature source reviews at 8ba

The supervisor commissioned separate fresh reviewers without the author conversation, author conclusions or another lens's output. Both inspected exact head `8baecc12ff1a6807abeeed2dce09badef4d87abb` against actual main `38136624cbdbdad19824118c997a6fa5a5517b58`. They ran no tests/builds/hosts and did not establish rendered UX acceptance.

- [Independent correctness report](https://github.com/elsa-workflows/elsa-foundation/pull/3850#issuecomment-6654107006): no confirmed actionable UI-critical correctness defect. Its source conclusions and explicit provider/runtime/rendering limits remain as recorded; this is not a whole-feature execution pass.
- [Independent standards report](https://github.com/elsa-workflows/elsa-foundation/pull/3850#issuecomment-6654110541): changes requested for P2, raw provider failures escaping the new EF history read/observation/retention ports, and P3, local generic toolbar controls bypassing public Studio controls/density.

Both findings were independently verified against framework §2.23.5, actual EF boundaries and Studio module UI guidance. P2 is being corrected with a named internal EF sibling exception in the existing IOException failure family, retaining original cause and localizing identifiers without a new public API or altered retry policy. P3 is being corrected with the actual exported Studio controls and their field/select recipe, preserving diagnostic-stream geometry and semantics. Source/test fixes are not yet qualified or published as a second-round candidate. No finding has been dropped.

Supporting source-bound results at 8ba: Activities 771, Dispatch 403 and API 324 passed, zero skips. The manifested preflight control has six predicted old-source failures/eight hard-classification passes, then 14 corrected-source passes. Compensation has 430 passes and one new fixture assertion failure before bulk materialization; its one-line null-contract correction is untested. Scheduling did not run. Final frontend, composed 18 and independently inspected downloadable UX remain pending. The prior Runtime 3,428 pass is at 732 with unchanged default Runtime/Core source through 8ba.

## Cancellation boundary correctness

The supervisor commissioned a fresh independent reviewer with no author conversation, author conclusions or other reviewers' output. Its scope was the mailbox acquisition cancellation guard, unchanged cleanup/idempotency behavior and deterministic regression, at exact head `693c7f8bd1cf2c3857cea008499fc72f20931ef7`.

- [Independent correctness receipt](https://github.com/elsa-workflows/elsa-foundation/pull/3850#issuecomment-6650364336): no actionable correctness findings in this boundary. The reviewer inspected guard placement, cleanup, absence of executor/control-scope/key changes before cancellation, controlled continuation and same-envelope retry. It ran no tests or builds.
- [Exact-head correspondence addendum](https://github.com/elsa-workflows/elsa-foundation/pull/3850#issuecomment-6650500620): provider, regression, actor/executor contracts and DrainControlContext are identical through `30c1da35e4f6f181fa84c61a7b5da00d5462baaf`.
- The author also verified an empty diff for those five files from 693 to fixture-only `02f9e6d04d27f9cfb676cead1e11076ae85a1862`. The final feature head must preserve that correspondence or obtain targeted re-review of a changed boundary.

Supporting evidence: the deterministic regression failed without the guard, then passed within the normal 3,417-test Runtime suite; ten additional bounded runs containing the two unchanged disposal cases and the new regression passed 30/30. Source-bound local logs are `/tmp/1211-mailbox-negative.log`, `/tmp/1211-source-runtime-final.log` and `/tmp/1211-mailbox-repeat/receipt.json`.

Limits retained from independent review: the negative log alone does not attest its source mutation; compact repeat logs do not name the selected tests; the passing suite was on frozen pre-commit source and is not exact-head whole-feature or native UX acceptance. This receipt resolves the scoped correctness uncertainty; it does not call for another unchanged actor review.

## Remaining round work

Freeze the coherent P2/P3 and fixture corrections for source round 2, run the permitted causal/affected qualification and obtain fresh independent follow-up without sharing another lens's output. Complete the remaining provider, long-history/access and native UX proof, obtain downloadable current-head visual evidence and commission independent acceptance/UX inspection. Record additive fixes, exact source and actual results here and on PR3850. Converge within the standing six-round cap; do not merge with incomplete UX or required checks.
