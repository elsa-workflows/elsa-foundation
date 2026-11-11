# Issue triage, 2026-11-09

Read-only analysis of every open issue (513) and open pull request (97) in `elsa-workflows/elsa-foundation` on 9 November 2026. Nothing on GitHub was changed. The verdict lists are in [final_lists.md](final_lists.md); the PR verdicts are in [verdicts_prs.md](verdicts_prs.md); the `0*.sh` scripts next to this file apply the recommended bulk actions and are meant to be read before they are run.

## 1. The numbers

| Signal | Value |
|---|---|
| Open issues | 513 (hygiene target < 100) |
| Opened / closed, weeks 42–45 | 171/65, 239/129, 133/109, 174/79 |
| Net growth per week, last four weeks | +106, +110, +24, +95 |
| `ready-for-agent` | 86 (cap 20) |
| `status:in-progress` | 52, of which 14 idle more than 14 days |
| Open issues without any triage label | 133 |
| Open issues with `needs-triage` | 0 |
| Open issues referenced by a commit on main | 105 |
| Open pull requests | 97, of which 34 red, 29 drafts |

### Where the inflow comes from (issues created since 2026-10-12: 722)

| Author | Filed | Closed so far |
|---|---|---|
| elsa-workflows-codex sessions | 392 | 277 |
| sfmskywalker | 102 | 31 |
| elsa-workflows-bot (review follow-ups, UX QA, product scout) | 87 | 8 |
| j03y-nxxbz | 73 | 25 |
| elsa-4-lead | 43 | 8 |
| elsa-workflows-claude | 17 | 2 |

Five structural causes, in order of weight:

1. **Codex program scaffolds.** Every new program is filed as program → epic → feature → task, often with single-child epics and "qualify and deliver end to end" leaves that restate the program's exit criterion. Roughly 100 issues a week come from this, and half of them are closed again within days, which is churn, not progress.
2. **Bot review follow-ups land as issues.** 42 "follow-ups from #N review" issues were filed on 2026-10-22 alone (#3206–#3247). They carry no triage label, so nothing ever picks them up or expires them. Hygiene policy says follow-ups belong on the PR.
3. **"Part of #N" commits never close their issue.** 56 open issues are fully delivered on main. The weekly hygiene report lists them, but nobody acts on the list.
4. **The stale rule is keyed to a label nobody uses.** Stale processing only fires on `needs-triage` and `needs-info`. Agents file with `status:todo` instead, so there are 0 `needs-triage` issues and the stale rule has nothing to act on. 133 issues have no triage label at all.
5. **Review throughput.** Code Review (`elsa-workflows-review-bot`) has answered only five of the open PRs' review requests. 34 PRs are red for the same three checks from a 6 November main (EF-3 Decisions migrations, fixed by #3973; Studio .NET build; Build & test): merging main and rerunning fixes most of them. Seven codex PRs filed 5–7 November carry whole features of 12k–59k added lines each and cannot be reviewed at that size. About 35 codex PRs carry an owner pause hold from 6 November.

## 2. Verdicts

Every open issue got exactly one verdict. Six cross-slice conflicts were resolved by hand (recorded in the lists).

| Verdict | Count | Action |
|---|---|---|
| CLOSE-COMPLETED | 56 | `01-close-completed.sh`: close as completed, naming the commit or PR |
| CLOSE-DUPLICATE | 31 | `02-close-duplicates.sh`: close, pointing at the canonical issue |
| CLOSE-NOT-PLANNED | 48 | `03-close-not-planned.sh`: receipts, review nits, superseded by ADR 0080, parked-era work |
| PARK | 31 | `04-park.sh`: `status:parked`, strip `ready-for-agent` / `in-progress` |
| KEEP-RETRIAGE | 26 | `05-retriage.sh`: strip `ready-for-agent` or reset stale `in-progress` |
| KEEP, trim `ready-for-agent` | 22 | `06-trim-ready-for-agent.sh`: keep the issue, drop the label to meet the cap |
| Label fixes | 11 | `07-label-fixes.sh` |
| NEEDS-OWNER-DECISION | 9 | Section 4 |
| KEEP | 312 | Prioritised in section 3 |

After the scripts run: **377 open issues** (from 513), **20 `ready-for-agent`** (from 86), and every remaining `in-progress` issue has an open PR or recent activity.

Notable closures:

- Monorepo consolidation M1–M3 are done (commit 87d14dd2b0 moved everything into Essentials/, Extensions/ and Studio/): close #2502, #2510, #2503, #2579. What remains is the M4 cutover #2505: the old `elsa-foundation-studio` repository still publishes Studio packages and still has 43 open issues and 9 PRs.
- Runtime Efficiency bugs #3661, #3678, #3683 were delivered by PRs #3702, #3694, #3696 that said only "Part of". Parity ADR #2772 and permission rename #2773 merged. Unified-execution evidence features #2893, #2894, #2897, #2899, #2909 are done by their own reconciliation comments.
- The GitBook spike #2498 is answered on main (7051f33016); v4.elsaworkflows.io serves the Foundation Markdown.
- #2092 and #1144 (two version lines) are superseded by ADR 0080 D4 (one version line until GA).
- 13 bot review follow-ups that are only nits or test-hardening asks close under ADR 0080's risk-based testing decision; the other follow-ups in #3206–#3247 are real defects whose cited symbols are still on main and stay open.
- #3550 (visual QA pass umbrella) and #1322 (PR sweep) are receipts.
- Scaffold folds: single-child epics #3433, #3434, #3740, #3816, #3818 fold into their only child; epic layers #3739, #3741, #3817, #3819 fold into their program; exit-criterion restatements #3527, #3747, #3756, #3825 fold into the program.
- Of #2262's 16 `ready-for-agent` children under a parked program, two are real bugs worth keeping (#2359 reconciler never deactivates a removed artifact; #2353 a throwing observer reverts a committed activation); #2363 is a Nuplane bug to refile upstream; the other 13 park.
- #2809 (bot-filed Entra epic, not a registry program): 12 of 14 children close as not planned; the ADR #2810 and Npgsql spike #2811 park; the epic itself is an owner decision.

## 3. Priorities for what stays open

### P0 (7): security and GA blockers, do first

| Issue | Why |
|---|---|
| #3707 | `System.Security.Cryptography.Pkcs` unpinned; 42 lock files resolve 9.0.13 (CVE-2026-84008) |
| #3735 | Studio host module registry returns package-feed credentials to the browser |
| #3723 | Version GET endpoint checks only `workflows/design:read`, skips the owning definition authorizer |
| #2840 | Refresh tokens keep working for disabled and deleted users |
| #2848 | Token subject collides across providers and tenants (GA blocker; PR #3806 partial) |
| #3138 | Connections accept credentials embedded in settings (GA blocker) |
| #3921 | Converter revision for Elsa 3 imported versions (GA blocker) |

Also security-flavoured P1: #3779 (deleting an OAuth2 connection leaves its token secret for a same-named successor), #2846 (owner decided to refuse credential inputs at the publish gate; not implemented). GitGuardian flags high-entropy secrets in PRs #3858 and #3904; check those before anything else merges from them.

### P1 (60): critical path per Active program

| Program | Critical path now |
|---|---|
| Elsa 3.8/3.9 parity #2771 | #2777 users API and #2778 roles API are now unblocked (their blockers merged) → #2782 Studio permission gating → #2779–#2781. Phase 1 has had no driver for 25 days. |
| Elsa 3 conversion #3052 (initiative, spec-first) | Live import defects confirmed on main: #3295 Done → Unmatched routing, #3202 `{}` headers fail at publish (fix exists on parked PR #2708), #3195, #3296, #3171 (trailing `//` comment breaks every script, one-line fix). |
| Runtime Efficiency #2531 | Four open bugs each with a PR: #3700 (draft #3868, green), #3698 (#3848), #3703 (#3905), #3939 (#3941, red). #2564 closes when #3841 merges. Then #2667 and #2312. The standing order #3648 → #3647 → #3275 → #2831 is complete. |
| Unified execution #2892 | #2905 (7/8 children), #2907, #2908 ETL, #3650 spec 1184 proof rows on real hosts. |
| Weaver #3428 | #3831 (draft #3845) → #3832 → #3833 → #3834. |
| Named blocks #3610 | #3751 T002 (PR #3837 ready) → #3752 T003. |
| Activity Data Selection #3882 | #3922 (PR #3948, changes requested) → #3094. #3977 Decisions depends on it. |
| Entity-aware #3608 | #3907, #3908 (PRs #3923, #3949). |
| StateMachine #3815 | Only #3820 M0 contract (draft #3840); M1–M3 leaves wait. |
| Studio expressions #2310 | #2351 via the re-split PRs (#3989, #3990 merged; #3991 red). |
| Consolidation #2501 | #2505 cutover: disable the old Studio publisher, archive the old repo. |
| Simplification #2559 | Phase 3 APIs do not exist yet (#2570 AddElsa, #2587, #2588, #2589, #2590); #2563 four-week routine review is due. |
| Decisions UX | #3977, #3979 (owner PR #3994). |
| Other | #2792 headless submit skips validators (PR #3929 ready), #3701 packaged definitions all under default tenant, #3947 Decisions in default shell blocks EF CLI tests, #3721 ETL overlapping runs overwrite rows. |

### P2 (160) and P3 (108)

Mostly Studio UX defects (48), parity follow-ups, simplification items and CI flakes. They stay in the backlog as `status:todo` and get pulled only when a program needs them or the `ready-for-agent` buffer has room. CI flakes: #3252, #3259, #3260, #3262 must stay open because `tools/ci/test-tiers.json` quarantine entries name them; the rest fold into the #3263 tracker.

### The 20 `ready-for-agent` after trimming

P0 #2840. P1 #2312, #2665, #2667, #2777, #2778, #2782. P2 #3664, #3663, #3720, #3814, #2359, #2353, #2232, #2234, #2664, #2830, #2746, #2128, #2621. All are reproduced or have an open PR, and none sits under a parked program.

## 4. Decisions only the owner can take

1. **#3035 Native Compensation** looks fully delivered (children closed, qualification in a710adf26). Close it and update its registry row.
2. **Registry drift.** Eight large features carry owner end-to-end authorizations in comments (4–6 November) but are not in the registry's Active table, which claims to be complete: #3579 OIDC, #3582 triggers, #3583 inbox, #3586 jobs, #3590 releases, #3597 bpmn-js, #3604 dashboards, #3849 timeline. #3586 and #3590 carry `type:program`. Register them, or drop the label and fold them under an existing program. The same holds for #3901 regression testing, whose only authority is a line codex wrote.
3. **Elsa Agents #3519**: registry says Active, a 6 November comment says the broader program is paused. Reconcile.
4. **The seven whole-feature PRs** (#3909 +59k, #3912 +52k, #3904 +37k, #3950 +27k, #3913 +27k, #3911 +17k, #3903 +13k) and the related #3850, #3806, #3840, #3951: split or close. None is reviewable.
5. **Elsa 3 conversion decisions** held by `needs:decision`: #3026 (time and unique-value capability), #3194 (resource variables), #3204 (spec 247 JavaScript read model), #3281 (converter defaults). Each verdict line names the ADR or spec PR that settles it. Also #3190 Object default editor, #2551 credentialed http refusal.
6. **#2809 Entra epic**: adopt as a program or close; its body keeps D1–D5.
7. **#1191 RFC** with a DO-NOT-MERGE PR idle 93 days.
8. **Pause hold.** About 35 codex PRs await the 6 November hold being lifted or the PRs being closed.

## 5. Plan

**Day 0 (today, after approval).** Run `01`–`07`. Comment the registry corrections. Expected state: 377 open, 20 ready-for-agent, 0 stale in-progress.

**Week 1: unblock the pipeline.**
- Fix the 7 P0 items; #3707 and #3735 are a pin and a field removal.
- Merge main into the 34 red PRs and rerun; merge-first list: #4007, #3997, #3785, #3286 (owner approved), #3952 (bot approved), #3927, #3938, #3937, #3928, #3942, #2708 (regenerate maps). Close #1192, #2373, #2378, #2380, #2739, #2790, #3999.
- Apply the Code Review fallback (no bot reply in 30 minutes → self-review loop, evidence, self-merge) to the green ready PRs.

**Weeks 2–3: critical path per program, one driver each.** Parity Phase 1 (#2777, #2778), Elsa 3 import defects (#3295, #3202, #3171), Runtime Efficiency's four PR-backed bugs, Weaver #3831, Named blocks #3751, ADS #3922. Pull from the 20-item ready buffer only when a program slot frees up.

**Process changes so it does not regrow (small doc and workflow PRs):**
1. Agents file `needs-triage`, as hygiene already says; `status:todo` is applied only by a maintainer. Then the existing stale rule actually fires.
2. Extend the stale rule to issues with no triage label.
3. Review follow-ups go as PR comments or one checklist on the PR's issue, never as new issues. Retire the "follow-ups from #N review" pattern.
4. Scaffold rule for codex programs: program plus PR-sized leaves only. No single-child epics, no leaves that restate the exit criterion. Fits ADR 0080's smaller meta footprint.
5. Hygiene workflow enforce mode: when `ready-for-agent` exceeds 20, strip the label from the oldest P3 items automatically and say so on the hygiene issue.
6. Close issues referenced by a merged PR's "Part of" when the PR's title matches the issue's ask; the weekly report already lists them, make acting on it part of the Monday routine.
7. PR size gate: a PR over roughly 3k added lines is converted to draft with a split request.
8. Registry hygiene: an owner authorization comment is not enough; the registry row and the ADR 0080 D0 amendment are the record.

## 6. Decisions taken and executed, 9 November 2026

Sipke reviewed the ten open decisions the same day and took the recommended option on each. What was done:

| # | Decision | Executed |
|---|---|---|
| 1 | Run the seven cleanup scripts | All 223 targets re-verified against live GitHub first (none had changed). 215 commands succeeded; 8 retriage lines needed only their comment because the labels were already right (posted separately). Script 07's two StateMachine lines (#3815, #3820 to in-progress) were skipped because decision 5 closes their PR #3840. Result: 513 → 378 open issues, `ready-for-agent` 86 → 20. |
| 2 | #3035 Native Compensation delivered | Closed as completed by script 01 (landing commit a710adf26). Registry row moved to Delivered; goal file status updated. |
| 3 | Register the eight owner-authorized features | Rows added to the registry Active table for #3579, #3582, #3583, #3586, #3590, #3597, #3604 and #3849, with a 9 November owner amendment in ADR 0080 D0 linking each authorization comment. #3901 regression testing stays unregistered and went back to `status:todo`. |
| 4 | Elsa Agents paused | Registry row and goal file set to Paused. #3519 and its open epics #3524–#3527 carry `status:parked`. |
| 5 | Whole-feature PRs | Closed without prejudice, asking for stacked slices of roughly 3k lines: #3909, #3912, #3904, #3950, #3913, #3911, #3903, #3850, #3806, #3840, #3951. Branches and evidence stay; the issues keep their authorization. |
| 6 | Elsa 3 `needs:decision` items | Label removed with the settling reference recorded: #3026 (ADR 0045 §5, PR #3384), #3194 (ADR 0046, PR #3422), #3204 (ADR 0089, PR #3420), #3281 (PR #3945), #3190 (JSON default under the Object syntax). #2551 carried no decision label and stays as the spec 188 leave-outs index. |
| 7 | #2809 Entra epic | Closed as not planned; D1–D5 recorded on #3579 as reference input. |
| 8 | #1191 RFC | Issue and DO-NOT-MERGE PR #1192 closed. |
| 9 | 6 November pause hold | Lift comment posted on the 26 held PRs that remain open. Main is being merged into the red PRs; the merge-first PRs follow the Code Review fallback. |
| 10 | Report and P0s | This folder, the registry, goal-file and ADR edits land through a pull request; the P0 fixes start as one session per issue. |

Not done, for the owner: closing Sipke's own stale PRs #2378, #2380, #2739, #2790, superseded #2373 and the no-op #3999 was not part of the ten decisions and is left to him.
