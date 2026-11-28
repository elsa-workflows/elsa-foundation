# Timer consumers and durable restart qualification — 22 November 2026

This is runtime qualification for [#5315](https://github.com/elsa-workflows/elsa-foundation/pull/5315), part of [#3849](https://github.com/elsa-workflows/elsa-foundation/issues/3849). Studio acceptance and final feature closeout remain with [#4368](https://github.com/elsa-workflows/elsa-foundation/pull/4368).

## Source and local execution

The complete runtime implementation was clean at `b5f7cf0f5d01afafd6ff60c89ea60339aa9eacb1`. The final first-fault control uses a version after the admitted profile, preserving the unsupported-profile refusal after v4 became supported. The preceding `5ed0ac321037f89a52f65b85be0d12ed41cfc938` adds the first-party readability reference to the real published-Delay fixture. No test skips or retries are included in these pass counts.

| Whole owning project | Executed / passed | Failed / skipped | Raw TRX SHA-256 |
| --- | ---: | ---: | --- |
| Runtime Scheduling, clean `accbaa0b0bab4cabb6b547c53d5aac9eb92086b9` | 141 | 0 / 0 | `784412546c5601d77d3ccbfe22226b641cd32ad8e3b62ead259e5e9a6ab03890` |
| Activities Scheduling, clean `5ed0ac321037f89a52f65b85be0d12ed41cfc938` | 54 | 0 / 0 | `4827ecfd1aa4fe4da9b6e4ef517f064c503b680c67b0add80300480900a461cf` |
| Runtime, clean `b5f7cf0f5d` | 4,680 | 0 / 0 | `c9f39c9fa56b388d45b974ab54158602abf6dab911025849546ed61f4d610966` |
| Runtime API, clean `b5f7cf0f5d` | 641 | 0 / 0 | `f8b270684c72589873492d378fdd301a248c59eb6f54c35fc30b21b394ad5e66` |
| Runtime EF, clean `b5f7cf0f5d` | 2,943 | 0 / 0 | `ab6769efd312068ae629195d007cda4aa3a15683d5cd25d80647ce8285905342` |
| Activities Runtime, clean `b5f7cf0f5d` | 895 | 0 / 0 | `10b1ae0b6d551e62563c0160f60b9e3be828eb8396382af57c66bdf4d0ef214f` |
| Architecture, clean `b5f7cf0f5d` | 624 | 0 / 0 | `b4f2d6c20ba9860494c401cd61cc59c0abd1f6de24a89be9d1b40451d11ab880` |

The three earlier test-only corrections do not change production scheduling inputs. The post-qualification main reconciliation contains Studio/prototype changes, with no changes under `essentials/`, `apps/` or shared build inputs. This ledger does not restamp those executions onto a later commit. Final source review and resulting-main receipts identify their own exact revisions.

## What the process and Delay cases establish

The six actual published-Delay cases cover memory and SQLite in Durable, Balanced and Ephemeral modes. They publish and admit a real executable, suspend its actual Delay, bind the timer to its owning activity/scope and bookmark, materialize the original scheduled observation, advance explicit test time, resume through the real pump and native bookmark handler, and preserve single schedule/dispatch/consumption/completion identities on repeated pumping. SQLite composes first-party readability/finalization and adopts operational format 1.2.

The EF owning suite includes five process-restart cases that launched **17 distinct child processes**: three phases for each timer persistence mode (nine), and four phases for each Durable/Balanced general-history case (eight). Raw child replies establish distinct PID/start-time pairs and one unchanged artifact hash per fixture. Timer identity/time, captured-from-start coverage, bookmark consumption, activity completion and duplicate-free confirmation survive reopening the same owned store. General-history cases also preserve frozen occurrence windows, replay fingerprints, commit groups and retention invalidation. These are real process/storage boundaries, with explicit logical clock advancement; no process failover claim is inferred from an in-process service restart.

## Failure evidence and corrections

- Four terminal-mutation controls failed before the pump isolated recoverable history failures; later legacy timers were previously starved. The whole Scheduling suite now passes, covering pre/post-commit faults on claim and list/delete paths.
- Three known-pending timer controls and two held occurrence authorization controls failed before settlement/authorization correction. Unknown evidence from another source no longer erases a still-qualified positive witness.
- Disabling the final metadata access guard made all eight held page/event/bookmark controls fail. The guard was restored before the complete 641-case API pass.
- The initial Delay compile failed for its missing readability reference. The initial whole Runtime pass had one obsolete future-v4 fixture failure. Neither is represented as an executed successful control; corrected whole suites above provide the pass evidence.

## Graph, migration and release scope

The complete solution restores in locked mode for Release and isolated Debug architecture assets. Generated maps and solution-filter checks pass; the solution filters did not change. No generated dependency/documentation maps are committed. The root catalog now links the EF catalog directly so discovery and root indexing agree.

All four additive TimerHistoryOwnership migrations already landed in [#5275](https://github.com/elsa-workflows/elsa-foundation/pull/5275) at `62092572693fedb738316befbcd410106eaa3e97`. Its actual resulting-main qualification passed 2,937 EF owning and 360 native-provider cases, including all 81 history controls and six fresh/retained migration paths. [Resulting-main receipt](https://github.com/elsa-workflows/elsa-foundation/pull/5275#issuecomment-7071954763).

CI is skipped under the owner's explicit feature-scoped instruction. Independent source review, local functional qualification, actual Studio acceptance and resulting-main verification remain required. There is no package release, deployment, shared workflow/protection change or credential change in this unit.
