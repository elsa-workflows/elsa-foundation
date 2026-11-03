# Resulting-main qualification

Program [#3688](https://github.com/elsa-workflows/elsa-foundation/issues/3688), feature
[#3577](https://github.com/elsa-workflows/elsa-foundation/issues/3577), implementation
[PR #3691](https://github.com/elsa-workflows/elsa-foundation/pull/3691).

## Qualified source and review

The implementation merged as `bfe9083349949b0cc8ebf1d3651df60ea2ca7767` on 3 November 2026.
This includes concurrent main deliveries #3662 (continuation segment refresh) and #3671
(Weaver workflow authorization). Qualification below rebuilt this combined commit, rather than
reusing the branch's host binaries.

[Independent round 8](https://github.com/elsa-workflows/elsa-foundation/pull/3691#issuecomment-6597202802)
was clean across correctness, repository standards and acceptance on the exact PR head
`fabc78cfaf1fa7e73a14d75b94bd684c787769d2`. The owner explicitly authorized up to 20 formal
review rounds for this delivery; the
[standing self-merge audit](https://github.com/elsa-workflows/elsa-foundation/pull/3691#issuecomment-6597207602)
records all eight rounds and repairs. No protection or required check was bypassed.

[Exact-head PR CI](https://github.com/elsa-workflows/elsa-foundation/actions/runs/41581773609)
passed Build & test, Generated maps fresh and Full tier, including all native provider shards,
integration shards, Studio browser shards, Windows/Linux backend source proofs and the independent
Weaver package witness. The latter reports 89 required cases, 26 autonomy cases and 20 restart
cases, no gaps, and an observed negative control. Earlier failures and repairs remain in
[the delivery history](delivery-status.md) and PR comments.

## Executed resulting-main journeys

Only owned temporary hosts and stores were used. Existing fixture stores were preserved across
the rebuild; real-host test fixtures separately owned their stores and child processes. No user
host or store was reset. Local logs, TRX and package files are under
`/tmp/elsa-user-tasks-3577`; source fixtures and the linked hosted checks make the proof repeatable.

| Boundary | Command or source fixture | Observed result |
|---|---|---|
| Normal host composition | `dotnet build Essentials/apps/Elsa.Workbench/Elsa.Workbench.csproj --no-restore`; equivalent build of `Studio/src/apps/Elsa.Studio.Web/Elsa.Studio.Web.csproj` | Both rebuilt successfully from the merge commit |
| Custom HTTP application | `pwsh -NoProfile -File e2e-tests/user-tasks/Test-UserTasks.ps1 -BaseUrl <owned-host>` | Approve, Reject and Request More Info each select only their authored branch, preserve exact data and reuse the duplicate receipt; required/read-only validation passes |
| Actual Studio | Generate the open-task receipt with the same script's `-LeaveOpen -ReceiptPath` options, then `ELSA_STUDIO_URL=<owned-studio> ELSA_USER_TASK_RECEIPT=<receipt> pnpm --dir Studio exec playwright test --config tests/live/playwright.live.config.ts user-tasks-journey.spec.ts` | 3/3 pass: signed-in desktop/narrow completion, keyboard/axe, explicit false, long-title wrapping and cleared pending notice; reusable form/activity authoring, three visual action connections, save/reload, publication and selected branch |
| Current identity and group authority | `UserTaskIdentityJourneyTests` in the BPMN Hosting test project | Real users race claims, release/reclaim, lose current permission/membership, reassign and record the trusted actor; passes |
| Native worker through BPMN | `BpmnUserTaskWorkerTests` in the same project | Published native task survives host restart and returns its action/data through the existing BPMN child-result contract once; passes (both hosted tests: 2/2) |
| Runtime scheduler integration | Whole `Essentials/tests/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj` | 3,270 executed/3,259 listed, 11 deferred theory rows, no skips or missing cases; includes the concurrent main continuation refresh changes |
| Durable task recovery | Whole Runtime EF test project, including `UserTaskRuntimeJourneyTests` | 1,880/1,880 pass with no skips or missing listed cases, including three-generation Durable/Balanced recovery, acceptance-before-delivery, canonical acknowledgement, retention, replacement, forged resume and cancellation races |
| External form and packed module consumer | `e2e-tests/user-tasks/qualify_integration.py` against rebuilt public contracts and the actual packed module | External asynchronous form validation, nested case-sensitive response roundtrip and packed Studio feature loading pass outside the repository, without project references |

The initial local readiness helper incorrectly required anonymous HTTP 200 from `/capabilities`,
which is protected. Authenticated health and the complete client journey passed. The initial
`dotnet pack --no-build` selected Release while only Debug had been built; selecting the actual
Debug configuration produced and verified the archive. Neither setup error was treated as an
application pass.

## Descendant-main requalification

Main advanced to `3e34381257b00383f8161d1b52751c58378c7012` through runtime PR #3697.
The closeout integrated it without conflicts. To check the new combination, both normal hosts were
rebuilt from a detached checkout of that actual main commit and restarted on the retained owned
stores. All three HTTP action paths, all three live Studio journeys and both real identity/native-BPMN
restart journeys passed again. Whole Runtime passed 3,278 executed/3,267 listed (11 deferred theory
rows, no missing cases), and whole Runtime EF passed 1,884/1,884 with no skips. The new asynchronous
public-contract/packed-feature consumer also passed. Both owned hosts were stopped afterward.

These are additional executed results; they do not rewrite the earlier `bfe908334` results above.

## Actual archive

`dotnet pack Extensions/UserTasks/studio/Elsa.Studio.UserTasks/Elsa.Studio.UserTasks.csproj
--configuration Debug --no-build --no-restore -o <owned-output>` produced
`Elsa.Studio.UserTasks.1.0.0.nupkg` again from the requalified descendant, with repository commit
`3e34381257b00383f8161d1b52751c58378c7012`.

The repository package verifier checked actual dependency metadata and byte-exact compiled assets:

| Payload | SHA-256 |
|---|---|
| Archive | `ef174bd7cf80f42178bdf06b8c726e2b49115cc6cbad24fda0bd6af68366ec4c` |
| `module.js` | `22b341d1e83451b78497518a546522c1cd083a6efb024e50f3db647bae3f1e0a` |
| `module.css` | `c4e70033c9ec02340368a9c9d96c795d274c96d077a57e6040cadd7f3db2f6db` |
| `entry-styles.json` | `710e4794df8163dd7ffe1591f903f22a155eab01e762566179ce0b060fe8319f` |

This is scoped module/public-contract qualification, not publication of a distribution release.

## Feature acceptance map

| #3577 success criterion | Executed witness |
|---|---|
| Configure assignment/group, actions and optional form; connect each action path | Actual designer journey creates a reusable form, configures the activity, connects all three visual ports, saves/reloads, publishes and executes |
| Durable open wait across restart | Whole EF suite's Durable/Balanced three-generation journey; published native worker with a real host restart |
| Discover and complete in Studio; claim/release group work | Desktop/narrow normal Studio completion; mounted queue/claim/release UI; real independent signed-in users race claims and release/reclaim against Workbench |
| Equivalent API/custom form application | All three HTTP action paths, durable external-form journey, matching mounted custom renderer and outside-workspace public-contract consumer |
| Exact submitted data, matching path and one completion | Client/browser/BPMN journeys and whole EF concurrency, receipt, forged/replaced-wait, post-claim outage and canonical acknowledgement tests |
| Understandable authorized reassignment, cancellation and history | Real current membership/permission revocation and reassignment; EF cancellation/terminal/history tests; API tenant/resource/actor negatives and mounted management/history UI |

The broader reviewed-head suites cover API, publishing, catalog, activity design/behavior, identity,
all supported database providers and Studio module integration; the resulting-main journeys above
repeat the decisive combined-runtime and real-host boundaries. No unexecuted row is inferred solely
from a source inspection.

## Main checks and closeout

[Push CI on the implementation merge](https://github.com/elsa-workflows/elsa-foundation/actions/runs/41591385434)
completed **cancelled**, after main advanced through #3697. Its completed successes are retained,
but canceled jobs are not passes. The repository intentionally
[supersedes older main push runs](../../.github/workflows/ci.yml) when a newer push arrives.
The worker app could not dispatch a separate frozen workflow (HTTP 403); no alternate identity was used.

The earlier closeout candidate imposed an extra requirement that every substantive job on that
exact push finish green. This record explicitly replaces that self-imposed condition with the
canonical [post-merge protocol](../../docs/contributing/merging.md#post-merge-gates): execute
resulting-main qualification, inspect current main for reported failures, and handle actual failures.
Required checks must still pass on the closeout's independently reviewed exact head, with no failed
head check, human hold or protection bypass. Original implementation full-tier/native-provider CI
remains green; no failed check has been waived.

GitHub's normal merge endpoint rejected the initial closeout attempt with “2 of 3 required status
checks are expected”, despite per-head APIs reporting Build & test/Generated maps green from the
draft run and Full tier green from the ready run. The final ready-PR push reruns all three together;
its checked results and normal protected merge are recorded on the PR. The rejected attempt is
retained as evidence, and neither an administrator override nor alternate write identity is used.

[Descendant-main push CI](https://github.com/elsa-workflows/elsa-foundation/actions/runs/41593751946)
is monitored as additional evidence. Its status and the healthy-main inspection are recorded in
[closeout PR #3706](https://github.com/elsa-workflows/elsa-foundation/pull/3706)'s gate-evidence comment.
The executed original/descendant-main acceptance above remains pinned to its actual source commits;
a superseded push is never silently relabeled as successful. There was no open `main is red: CI`
issue at inspection.

The closeout reconciles the task checklist, completed program registry and the User Tasks portion
of the parity/BPMN dependency. Its source bookkeeping transitions take effect on permitted merge;
the Program issue remains the live state during review. Other parity/BPMN objectives remain open.

Both owned normal hosts were stopped with SIGTERM after verifying each PID, DLL and owned content root. Their stores remain available for reproduction. Test fixtures disposed their own child hosts.
