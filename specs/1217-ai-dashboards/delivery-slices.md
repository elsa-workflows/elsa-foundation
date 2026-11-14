# Delivered slices — 13 November 2026

#3604 implementation and exact-source resulting-main qualification are complete. Finishing [PR #4444](https://github.com/elsa-workflows/elsa-foundation/pull/4444) retains the reviewed acceptance artifacts and lifecycle/program closeout. Its exact final reviewed head and merge receipt live on the PR, avoiding a self-changing hash in this document. Issue closure follows the final evidence merge and descendant-main artifact verification.

The owner selected reviewable slices of roughly 3,000 changed lines, extended review rounds within reason, and explicitly authorized personal-account admin override for absent/slow CI. Independent correctness/standards/acceptance review and local functional/UX gates remain required. Each merge records the exact reviewed head, actual CI state and use of that exception. Pending/cancelled/skipped/unavailable checks are not passed checks. Normal security checks pass; no failed security check was waived.

| PR | Delivered scope | Merge commit |
| --- | --- | --- |
| [#4217](https://github.com/elsa-workflows/elsa-foundation/pull/4217) | Dashboard slice 1: define native contracts and delivery plan | `9cac4343633ef79ef68fcf072bca28621db91a55` |
| [#4222](https://github.com/elsa-workflows/elsa-foundation/pull/4222) | Dashboard slice 2: validate and execute bounded native queries | `dde53009b9e214f3eec5bd55c9cf6df929d0dd42` |
| [#4235](https://github.com/elsa-workflows/elsa-foundation/pull/4235) | Dashboard slice 3: persist and authorize SQLite definitions | `3caff65f50b118d27fef9fabe19452625d18464c` |
| [#4242](https://github.com/elsa-workflows/elsa-foundation/pull/4242) | Dashboard slice 4: bound reads and complete relational providers | `2779a1b049c944fab0102f44ced5bdacdea26d02` |
| [#4247](https://github.com/elsa-workflows/elsa-foundation/pull/4247) | Dashboard slice 5: expose authorized native APIs | `d89b76a68684123ff7af007681240d2f9950b05e` |
| [#4261](https://github.com/elsa-workflows/elsa-foundation/pull/4261) | feat(dashboard): add authorized Workflow query source | `8b88574adcddac8d96152f30feaf38444b246d7c` |
| [#4262](https://github.com/elsa-workflows/elsa-foundation/pull/4262) | feat(studio): add native saved questions and dashboards | `7528a71077d9cbd0ba7e14e9c1a034cb83761c70` |
| [#4265](https://github.com/elsa-workflows/elsa-foundation/pull/4265) | fix(studio): preserve personal Home layout revisions | `8699ba95631d176a236987fba281acb6d12d89dd` |
| [#4266](https://github.com/elsa-workflows/elsa-foundation/pull/4266) | feat(dashboard): author questions through model-agnostic Weaver | `153e2288b72f5a820a8e82dc7a3f978cd471b009` |
| [#4277](https://github.com/elsa-workflows/elsa-foundation/pull/4277) | fix(weaver): isolate Copilot SDK server state and callbacks | `e5df8e53a5192da1c8a0b4886590c8d158e73d50` |
| [#4280](https://github.com/elsa-workflows/elsa-foundation/pull/4280) | feat(dashboard): qualify independent Acme Processing package contribution | `a1d253c24a336826d9621c7cafeba521b2cb3f36` |
| [#4404](https://github.com/elsa-workflows/elsa-foundation/pull/4404) | Fix Dashboard authoring tool query grammar | `de27c3166b774a8f99ebaf47bb95a47fe274d67d` |
| [#4419](https://github.com/elsa-workflows/elsa-foundation/pull/4419) | Allow authorized preview of an unavailable question replacement | `8e8e2c362cba7da76a057f486481cbf9cf5112df` |
| [#4283](https://github.com/elsa-workflows/elsa-foundation/pull/4283) | Qualify native Dashboards and preserve composed source provenance | `a84f1f8cb23112137e667765ca11575a10ead19d` |
| [#4284](https://github.com/elsa-workflows/elsa-foundation/pull/4284) | Qualify real Copilot Dashboard authoring over Weaver | `c9c6e2274add1201b8e65ef93a1822a83d3e9bad` |
| [#4286](https://github.com/elsa-workflows/elsa-foundation/pull/4286) | Qualify saved Dashboard comparison results and membership | `ed5092c6ff467ede433e487d7c84f076c761bdea` |
| [#4434](https://github.com/elsa-workflows/elsa-foundation/pull/4434) | Retain private Dashboard trace replay without credential-shaped literals | `11e40ca7c7a2827f1f42270285ef56429929ccb5` |
| [#4438](https://github.com/elsa-workflows/elsa-foundation/pull/4438) | Qualify Dashboard native module repair and edge recovery | `6c6e2dbc84e56b74a1863c2ae63f8edc52895003` |
| [#4439](https://github.com/elsa-workflows/elsa-foundation/pull/4439) | Qualify unsupported Dashboard meaning without fabricated results | `b6f26b5e5a191c57aa5e93b475091dd5653c36bd` |
| [#4440](https://github.com/elsa-workflows/elsa-foundation/pull/4440) | Qualify keyboard authoring and editor/Home preservation | `3a90b64b9664146f51b082749914d47c488ecc53` |

## Qualification

The [downloadable ledger](evidence/2026-11-13/README.md) and [resulting-main receipt](evidence/2026-11-13/resulting-main-receipt.json) identify all exact tested sources, provider/model identities, independent math, actual host lifetimes and accepted original screenshots/ARIA/safe traces. Clean main `3a90b64b9664146f51b082749914d47c488ecc53` passes three actual-provider/native keyboard cases plus a real same-store provider-disabled host restart, all four host exits zero. Its entire tree is identical to reviewed `f10bd431ca8884a1c036455a1988c991c2150dde`, where canonical graph restore, Architecture 622/622, generated maps and 150-project test-tier checks pass. Seven keyboard proofs retain real Tab/Enter and zero pointer events; all 23 native phases add zero SDK sends with privacy failures0.

The current full model witness explicitly selects `gpt-5.6-sol`; historical weekly `gpt-6.1-sol` and separate unsupported-model receipts retain their own identities. Actual current source facts independently reproduce 62.5/50/+12.5 →75/25/+50 →80/25/+55, and Acme ownerRetail120000 versus reader60000 with Enterprise 300000. Editor UI save/reopen/copy with no sharing rights and settled missing-contribution Home preservation also pass.

## Preserved superseded and failed outcomes

Original [#3913](https://github.com/elsa-workflows/elsa-foundation/pull/3913) closed without merging and is not presumed delivered. Earlier integration PRs #4415/#4428/#4429/#4430/#4381 are superseded without merge by the clean stack and finishing evidence PR. Their source-specific actual receipts remain evidence at their tested commits, rather than claims that those PRs merged. Drafts #4435/#4436/#4437 used a moving patch base that reintroduced a synthetic fixture; they closed before readiness or merge and were replaced using immutable reviewed bases.

The literal JWT in the original privacy regression test was entirely synthetic, but GitGuardian scans every PR commit and correctly kept reporting its earlier presence after the final file changed. Clean replacement commits construct the same kind of redaction test at runtime and exclude the earlier literal-bearing commit from ancestry. The normal security scans pass; original failed checks remain failed and unmerged, with no history, check-status or security-setting rewrite.

Keyboard R1/R2 failed macOS process-group cleanup before browser/model use. R3 reached actual authoring/arrangement/sharing but its independent third API read exceeded the native two-read bound while both UI tile reads succeeded. R4 protocol passed while its Home screenshots still showed loading; those captures were not accepted as settled. The bounded retiring-group correction, explicit tile-completion observation barrier and settled Home/focused editor capture correction pass causal controls, independent source review and the fresh R5 full actual run. Exact resulting main subsequently passed independently too. No failed outcome was reclassified.

The independent Acme witness locally builds 20 archives and runs 19 native cases with offline package-only restore. Examples remain excluded from ordinary publication. Coverage remains unknown where the source cannot attest collection history; current-principal permission changes are qualified, while invalidation of outstanding signed tokens is not. Physical model HTTP counts are not exposed by the SDK seam. No deployment, release or remote package publication is part of this delivery.
