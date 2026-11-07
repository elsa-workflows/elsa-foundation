# Post-switch CI outcomes: first 20 code PRs

Measured on 6 November 2026 for [#3267](https://github.com/elsa-workflows/elsa-foundation/issues/3267). T038 remains incomplete. The 30-day observation and historical flake comparison are still open.

## Findings

- **SC-001 misses the accepted 13-minute bound:** median push/run-creation to `Build & test` report is **14.54 minutes**. 8/20 reports meet 13 minutes. The cohort includes 18 successful reports and two failed reports; the successful-only median is 14.54 minutes.
- **SC-002 misses 25 minutes on 12 of 20 first ready reports:** median is **26.58 minutes**, with 8/20 within 25 minutes. All selected full-tier reports are successful. Seven runs actually retried tests; they are identified below.
- **SC-004 cannot yet be accepted.** Story 3 shipped in [#3265](https://github.com/elsa-workflows/elsa-foundation/pull/3265) at 22 October 22:19:07 UTC. Its 30 days end on **21 November 2026 at 23:19 CET**. Selected PR-tier and full-tier workflow attempts are all attempt 1; that is a limited sample, not a completed audit of manual reruns or a September–October comparison.

These are the initial historical outcomes after the 23 October switch. They precede the 31 October shuffle-cadence change and later workflow consolidation. They do not measure current-head CI or establish the benefit of a pending optimization.

## Cohort and method

The switch [#3268](https://github.com/elsa-workflows/elsa-foundation/pull/3268) merged at 23 October 10:40 UTC (`4858b051fac957d608442751a09daae5d10cc159`). The collection covers `CI` pull-request runs from that instant through 26 October 23:59:59 UTC, including cancelled runs. A real executed `PR shard` identifies the full test path; a cancelled matrix placeholder with no steps does not. PR numbers are resolved through each run commit’s associated pulls and matching head branch. The first 20 distinct code PRs are ordered by their first executed shard run. Documentation-only #2655 is excluded: its two Markdown paths conservatively took the full CI path because `website/docs/**` was outside the fast-path list.

For each PR, use the first noncancelled `Build & test` report after the switch. Failed reports remain in the sample. Cancelled or superseded attempts are excluded from duration samples and retained in the event-exposure analysis rather than assigned a zero duration. The full-tier sample uses that PR’s first completed actual `Full tier` report whose plan log records `DRAFT: false`. A real full tier can also be forced while draft when tier tooling changes, so the aggregate name alone does not establish readiness. Draft reports and placeholders do not qualify. Its source head may differ from the first PR-tier report because marking ready can follow draft corrections. This is one report per PR, not a summary of every later ready push.

Elapsed minutes are `(aggregate.completed_at - run.created_at) / 60`, including scheduling and queue delay, as the issue requests. This measures GitHub run creation rather than a separately captured push or ready timestamp. Retry presence is read from successful downloads of every selected full-run log, matching the runner’s `Retrying the failed tests of ...` marker. All 20 full-run logs were available; no retry state is inferred solely from a successful job. The [data](evidence/2026-11-06-ci-outcomes.json) retains run URLs, timestamps, conclusions, observed retry markers and exposure matches.

| PR | Build & test, min | Result | Full tier, min | Retried tests |
|---|---:|---|---:|---|
| [#3279](https://github.com/elsa-workflows/elsa-foundation/pull/3279) | [13.40](https://github.com/elsa-workflows/elsa-foundation/actions/runs/39947367572) | success | [23.28](https://github.com/elsa-workflows/elsa-foundation/actions/runs/39950155218) | No |
| [#3264](https://github.com/elsa-workflows/elsa-foundation/pull/3264) | [11.38](https://github.com/elsa-workflows/elsa-foundation/actions/runs/39950593991) | success | [21.93](https://github.com/elsa-workflows/elsa-foundation/actions/runs/39959834252) | No |
| [#3294](https://github.com/elsa-workflows/elsa-foundation/pull/3294) | [10.40](https://github.com/elsa-workflows/elsa-foundation/actions/runs/39974163343) | success | [22.75](https://github.com/elsa-workflows/elsa-foundation/actions/runs/39974955666) | No |
| [#3298](https://github.com/elsa-workflows/elsa-foundation/pull/3298) | [16.42](https://github.com/elsa-workflows/elsa-foundation/actions/runs/40057434417) | success | [26.97](https://github.com/elsa-workflows/elsa-foundation/actions/runs/40057434389) | No |
| [#3299](https://github.com/elsa-workflows/elsa-foundation/pull/3299) | [20.02](https://github.com/elsa-workflows/elsa-foundation/actions/runs/40057613724) | success | [37.32](https://github.com/elsa-workflows/elsa-foundation/actions/runs/40057613761) | No |
| [#3300](https://github.com/elsa-workflows/elsa-foundation/pull/3300) | [23.23](https://github.com/elsa-workflows/elsa-foundation/actions/runs/40057632335) | success | [38.22](https://github.com/elsa-workflows/elsa-foundation/actions/runs/40057632647) | No |
| [#3258](https://github.com/elsa-workflows/elsa-foundation/pull/3258) | [11.88](https://github.com/elsa-workflows/elsa-foundation/actions/runs/40065035633) | success | [23.47](https://github.com/elsa-workflows/elsa-foundation/actions/runs/40073429368) | No |
| [#3302](https://github.com/elsa-workflows/elsa-foundation/pull/3302) | [25.97](https://github.com/elsa-workflows/elsa-foundation/actions/runs/40075068758) | success | [28.30](https://github.com/elsa-workflows/elsa-foundation/actions/runs/40075068772) | No |
| [#3304](https://github.com/elsa-workflows/elsa-foundation/pull/3304) | [35.40](https://github.com/elsa-workflows/elsa-foundation/actions/runs/40075251962) | success | [44.28](https://github.com/elsa-workflows/elsa-foundation/actions/runs/40075251992) | No |
| [#3307](https://github.com/elsa-workflows/elsa-foundation/pull/3307) | [40.38](https://github.com/elsa-workflows/elsa-foundation/actions/runs/40076245803) | success | [33.37](https://github.com/elsa-workflows/elsa-foundation/actions/runs/40076245750) | No |
| [#3308](https://github.com/elsa-workflows/elsa-foundation/pull/3308) | [36.35](https://github.com/elsa-workflows/elsa-foundation/actions/runs/40076452470) | success | [31.38](https://github.com/elsa-workflows/elsa-foundation/actions/runs/40076452456) | No |
| [#3309](https://github.com/elsa-workflows/elsa-foundation/pull/3309) | [25.07](https://github.com/elsa-workflows/elsa-foundation/actions/runs/40077381082) | success | [31.28](https://github.com/elsa-workflows/elsa-foundation/actions/runs/40077701908) | No |
| [#3311](https://github.com/elsa-workflows/elsa-foundation/pull/3311) | [15.23](https://github.com/elsa-workflows/elsa-foundation/actions/runs/40080012576) | success | [29.25](https://github.com/elsa-workflows/elsa-foundation/actions/runs/40080803189) | Yes |
| [#3313](https://github.com/elsa-workflows/elsa-foundation/pull/3313) | [12.43](https://github.com/elsa-workflows/elsa-foundation/actions/runs/40147354688) | success | [21.45](https://github.com/elsa-workflows/elsa-foundation/actions/runs/40152885465) | No |
| [#3315](https://github.com/elsa-workflows/elsa-foundation/pull/3315) | [10.25](https://github.com/elsa-workflows/elsa-foundation/actions/runs/40160480921) | success | [22.47](https://github.com/elsa-workflows/elsa-foundation/actions/runs/40161248854) | Yes |
| [#3318](https://github.com/elsa-workflows/elsa-foundation/pull/3318) | [13.85](https://github.com/elsa-workflows/elsa-foundation/actions/runs/40163065228) | success | [25.18](https://github.com/elsa-workflows/elsa-foundation/actions/runs/40164796361) | Yes |
| [#3319](https://github.com/elsa-workflows/elsa-foundation/pull/3319) | [10.80](https://github.com/elsa-workflows/elsa-foundation/actions/runs/40169516720) | success | [26.20](https://github.com/elsa-workflows/elsa-foundation/actions/runs/40190167614) | Yes |
| [#2825](https://github.com/elsa-workflows/elsa-foundation/pull/2825) | [10.88](https://github.com/elsa-workflows/elsa-foundation/actions/runs/40173957887) | success | [23.07](https://github.com/elsa-workflows/elsa-foundation/actions/runs/40173957813) | Yes |
| [#2728](https://github.com/elsa-workflows/elsa-foundation/pull/2728) | [5.90](https://github.com/elsa-workflows/elsa-foundation/actions/runs/40175898273) | failure | [23.10](https://github.com/elsa-workflows/elsa-foundation/actions/runs/40176537938) | Yes |
| [#3324](https://github.com/elsa-workflows/elsa-foundation/pull/3324) | [22.72](https://github.com/elsa-workflows/elsa-foundation/actions/runs/40179538600) | failure | [28.15](https://github.com/elsa-workflows/elsa-foundation/actions/runs/40181328926) | Yes |

## Queue and ready exposure

The 20 branches produced 52 CI-triggered runs in the collection window. Pairing the same branch and head SHA with the nearest full-tier workflow within 60 seconds, then reading its plan log’s actual `DRAFT` event flag, identifies 18 ready and 33 draft events; 1 remain unknown, where no plan payload was observed. The observed ready share is 35.3% of 51 classified events. This is workflow-event exposure, not an exact count of `synchronize` pushes: opened/reopened events are not distinguished by the runs API. It must not be reported as the final push-share acceptance result.

Across 160 executed jobs in the selected PR-tier runs, job creation-to-start queue time has median 0.04 minutes and nearest-rank p90 7.57 minutes. These job-level observations differ from R1’s former monolithic `Build & test` queue sample (0.9 / 21 minutes). The initial-job median of 0.18 minutes cannot establish that test shards were admitted that quickly; plans and other short jobs can start first. No like-for-like queue improvement is claimed.

## Remaining work and recommendation

- Keep T038 and #3267 open until SC-004 has its complete window and the historical flake baseline is compared. Audit actual push actions for the load-risk acceptance row rather than promoting the event proxy to an exact share.
- Take the measured SC-001 miss to the owner as the plan requires. Check which time is scheduling versus execution before selecting the method-level class split or a further cadence change. Preserve current required checks; do not infer permission to remove full-tier runs from these observations.
- Keep fixes and independent review on drafts until the review loop converges, following current repository policy. The seven retrying full runs and the queue observations are evidence to assess against existing CI changes, not proof of one exclusive root cause.
