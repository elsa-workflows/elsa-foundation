# Runtime Efficiency (#2531) handoff, 2026-11-04

This is a point-in-time handoff from a Claude Code session to the next agent. It records state, open work and fixture
know-how. Verify each state with `gh` before acting on it.

## State at handoff

| Item | State | Next action |
|---|---|---|
| #3601 (program integration) | Merged 2026-11-02 as `ea5c4de47` | None |
| #3764 (review round 7 of #3601's post-round-6 fixes, docs only) | Merged 2026-11-04 as `4676450b1` (merged by the owner) | None |
| #3770 (independent-machine cross-node Cancel evidence, fixture `i69`) | Ready; squash auto-merge queued on head `ade2f1ddd`; review loop converged; gate evidence posted | Wait for `Build & test`, `Generated maps fresh`, `Full tier`; see "Auto-merge stall" |
| #2831 (cross-node progressing Cancel) | Closed by #3669; i69 result posted on the issue | None |
| #2531 (program) | Open | Remaining leaves below |
| #3678 → PR #3694 (hand-off yields a running drain) | Open, `BLOCKED` with required checks green at last look | Owner merge or diagnose the stall |
| #3683 → PR #3696 (unavailable release vs buffered effect) | Open, `DIRTY` (merge conflicts with `main`) | Merge `main`, re-review, re-qualify |
| #3661 → PR #3702 (coalescing drain misses foreign delivery) | Open, `DIRTY` | Merge `main`, re-review, re-qualify |

The #2531 status comment is
[issuecomment-6615399245](https://github.com/elsa-workflows/elsa-foundation/issues/2531#issuecomment-6615399245);
it is edited in place, so update it rather than posting a new one when the state changes.

## Fixture i69 result (already recorded)

Owner node A, sender node B and PostgreSQL 16.15 each ran on their own Azure Standard_D2s_v6 VM on one private subnet,
with the unchanged `specs/1183-runtime-efficiency-program/evidence/two-host-qualification/cluster-controller.py` and
cluster-control hook, against a `main` build containing #3669. Parked Cancel 1/1, progressing Cancel 3/3, placement on
node A, both envelopes `Deferred`, no incidents. One PostgreSQL `40001` per surviving node log, retried by the
transport's bounded compare-and-swap. Limits: two nodes only (four-node results remain open), no owner failure or
database interruption during a Cancel. The fixture (`rg-elsa-efficiency-i69`) is deleted; no task resource or running
VM remains. Evidence: `docs/reports/evidence/runtime-efficiency-program-2026-11-01/cancel-i69-*` once #3770 merges.

## Auto-merge stall

On 2026-11-03/04, #3764 and #3694 sat at `mergeStateStatus: BLOCKED` with all three required checks green on the exact
head and squash auto-merge queued. Repository rulesets require only those three checks (no review rule); org rulesets
need `admin:org` to read. Re-queueing auto-merge did not help, and the owner merged #3764 by hand. Do not merge
directly from an admin account: that bypasses the gate. Report the stall and ask the owner.

## Working rules for this program

- Post on GitHub through the owner account with a `[Claude]`-style agent tag; end PR bodies with the agent attribution.
- Never write negated closing keywords ("does not close #N" auto-closes); write "Part of #N".
- Self-merge is allowed only after the self-review loop converges and the evidence comment is posted, and only through
  auto-merge queued while the PR is `BLOCKED`, so the required checks gate it. Merge `main` in first.
- After `gh pr ready`, do not cancel the draft CI run; let both finish.
- If Code Review (`elsa-workflows-review-bot`) does not answer within 30 minutes, fall back to the self-review loop.
- Before starting any local Workbench, check the port is free; the owner runs their own Workbench on 5095.
- Azure spend needs the owner's go-ahead; tear fixtures down in the same session and record a cleanup receipt.

## Fixture know-how (from i69)

- The frozen controller hard-codes the database host at `10.238.2.4:15432`, nodes at `.5` and `.6`, root paths
  `/root/elsa-eff-i68` and `/home/elsarunner/elsa-eff-i68`, and the container name `elsa-efficiency-i68-pg`. The hook
  refuses host ids without the `eff-i68-` prefix. A subnet `10.238.2.0/24` makes `.4` the first usable address.
- New Azure VNets have no default outbound access: the nodes could not reach the internet, which was fine (the
  self-contained Workbench needs only libicu, present on Ubuntu 24.04). Only the database host needs outbound for Docker.
- `az` AAZ commands fail to parse tag keys containing a dot (`claude.task`); use `claudeTask`.
- zsh does not word-split unquoted variables: put SSH options in an `ssh -F` config file.
- `dotnet publish -r linux-x64` rewrites every `packages.lock.json`; restore them before committing.
- Run `python3 specs/1183-runtime-efficiency-program/evidence/prepare-projects.py cluster-control` before building
  `Hook.csproj`.
- The controller reuses `node-<host>-<n>.log` names per series, so later series overwrite earlier node logs; collect
  logs after each series if all are needed.
- Its `secondsToSettledReceipt` includes one-second SSH polling; report it as approximate, not as latency.
- Set VM auto-shutdown as a safety net and delete the whole task resource group at the end.
