# Handoff: Program #3428 "Weaver across Studio": M4 spec 1203 and the remaining program

This is a working note for the next agent. **Delete this file before PR #3757 is marked ready.**

Written 2026-11-04 by Claude, which ran out of credits partway through the spec review loop.

## Standing authorization and constraints (owner: Sipke Schoorstra)

- **Goal, given on 3 November** and recorded on [#3428](https://github.com/elsa-workflows/elsa-foundation/issues/3428#issuecomment-6615643669): "deliver this program end to end. You have standing authorization to enter the self review loop with as many iterations as reasonably necessary and then merge. Get it done."
- **Code Review bot.** It is out of credits. The fallback:
  - run independent reviewers, one per lens (correctness, standards), with fresh agents each round;
  - verify every finding against the code;
  - loop until a round has no P1 or P2;
  - post an evidence comment on the PR, then a merge-gate comment;
  - squash-merge with `--match-head-commit <full sha>`, through the owner account.
- **Posting.** Post as the owner account, starting each comment with `[Claude]` (or your own tag). Comment trailers end with "🤖 Generated with …".
- **Commits:**
  - `git -c user.name="elsa-workflows-claude[bot]" -c user.email="342797135+elsa-workflows-claude[bot]@users.noreply.github.com" commit …`
  - trailer: `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` (adapt it for Codex).
- **Hard limits:**
  - Never authorized: release, deployment, provider purchase, credential extraction or publisher activation.
  - Never print or commit secrets, private keys or private configuration.
  - Never force-push or rebase.
  - Never enable auto-merge or use admin bypass.
- **Local environment:**
  - Port 5095 is Sipke's Workbench, so never assume it is free.
  - Use `/usr/local/share/dotnet/dotnet`, never `~/.dotnet`.
  - Run native witnesses with `nice -n 10`, at low load.
- **CI:**
  - Required checks: `Build & test`, `Generated maps fresh`, `Full tier`.
  - Never cancel a PR's draft-time CI run after `gh pr ready`: the ready run reuses its `Build & test`.
  - Before merging a PR that adds project references, merge `main` into it again.
  - Never write a negated closing keyword such as "does not close #N": GitHub still closes the issue. Write "Part of #N" instead.
- **Spec numbering.** Use the highest number across `specs/` and all open PRs, plus one. Reserve a number by opening a draft PR early: 1198 and 1202 were both lost to other PRs.
- **Consequential decisions** outside the approved scope go to the owner; don't decide them yourself. Sipke answers multiple-choice questions quickly.

## Program state

| Milestone | State |
| --- | --- |
| M0, M1, M2 | Done |
| **M3 #3432** | **Done.** Closed with [closeout](https://github.com/elsa-workflows/elsa-foundation/issues/3432#issuecomment-6616867867). #3442 closed, spec 1193 Implemented. 7 slices merged (#3671, #3704, #3715, #3722, #3727, #3732, #3736). Main CI run 41721202214 on `a1419b9c3` qualified it. |
| **M4 #3443** | In progress (`status:in-progress`). Spec 1203 is in review on **draft PR #3757**, branch `agent/claude/3443-weaver-module-support`, worktree `/Users/sipke/.codex/worktrees/weaver-3443`, head `36636855c`. No implementation yet. |
| M5 #3444 | Not started: qualification across providers and packaged custom hosts, module-author guidance, bundle re-measure. Then close #3433, #3434 and Program #3428. |

### Owner decisions for M4

All are recorded on #3428 and quoted verbatim in spec 1203's Assumptions.

| # | Question | Answer |
| --- | --- | --- |
| 1 | Irrecoverable deletes and revokes of Secrets and Service connections | "Let the integrator decide what privileges weaver has via configuration" |
| 2 | Feature selection apply | "Let the integrator decide via configuration" |
| 3 | Generic outcome for an action that persists nothing | "Add it in leaf 0" (*performed*) |
| 4 | Default of the Admin-risk gate | "Off; Workbench always on" |
| 5 | Whether the Workbench also enables the irrecoverable and activation classes (4 Nov, [6617907022](https://github.com/elsa-workflows/elsa-foundation/issues/3428#issuecomment-6617907022)) | "Enable all three" |

Receipt for decisions 1–4: [6612627295](https://github.com/elsa-workflows/elsa-foundation/issues/3428#issuecomment-6612627295). It says "spec 1202"; that means spec 1203.

### Spec 1203 design, settled over 22 review rounds

- **Structure.** The spec has:
  - FR-001, the full M4 matrix, with every M0 target quoted verbatim;
  - cross-cutting requirements FR-002 to FR-024;
  - leaves 0–3, fully specified as stories 1–4.

  Leaves 4–20 each get their own spec and a native sub-issue of #3443 when they start (FR-025). Spec 1203 stays *In progress* until the last M4 leaf merges.
- **Leaf contents:**
  - Leaf 0: generic Weaver changes.
  - Leaf 1: Secrets read, metadata update and value test.
  - Leaf 2: Service connections.
  - Leaf 3: secret revoke and delete, using a Connections consumer source.
- **Packaging:**
  - Server packages are `Elsa.Agent.<Domain>` under `Extensions/Agent/src/<Domain>/`. The ExtensionBoundary guard forbids Essentials from referencing extensions, which is why they don't live there.
  - Studio contributions stay in the owning module. A separate package uses `Elsa.Studio.Weaver.<Domain>`.
  - ETL's Agent package and its owner sit in different extension buckets, so their reference needs a `DeclaredExtensionEdges` entry.
- **Privilege policy.**
  - It is host configuration of the Agent module, not a session setting.
  - It has three classes: administrative, irrecoverable and activation. Each is off by default.
  - Every `Admin` effect always requires the administrative class. A non-empty class set is frozen into `PreparedDigest`; an empty set is left out.
  - It is enforced at preparation, at approval (core only, since approval calls no module), at execution and at recovery.
  - The Workbench enables all three classes in its base `appsettings.json`.
- **Outcomes and module codes:**
  - *Performed* is a new outcome with a `NoPersistedEffect` scope.
  - Each descriptor declares a closed set of module codes, which core validates. The generic replacement code keeps the owner-unavailable classification.
  - A module reports a partial failure by throwing, which yields *uncertain*. There is no server-side resolution of uncertain outcomes in M4.
- **Agent EF schema:**
  - Schema family 1.1.0 behind the ADR 0078 gate. It finalizes automatically: at activation on a single host, otherwise when the fleet allows.
  - An identity upcaster; defaults are filled in when the payload is deserialized.
  - Dormancy uses spec 182's shared `ISchemaDormancyCheck` and is decided at preparation, from the descriptor.
- **Secrets and Connections:**
  - Owner contracts take the authenticated principal, decide through `IPermissionAuthorizationService` in the owner's `.Api` package, and fail closed as owner-unavailable.
  - A connection-owned secret is one whose name sits under `connection:<name>:`. One convention in Connections `.Core` is shared with the Integrations OAuth2 code.
  - Secret revoke and delete fail closed when no consumer source answers.
  - #3779 (OAuth grant and PKCE secrets left behind when a connection is deleted) is triaged `status:todo`, and leaf 2 fixes it.

## Where the spec review loop stopped (round 22)

The **round 22 standards review** finished. Its findings have **not been applied yet**; the decisions below are already taken:

1. **P1, native connection delete.** Narrow it to exactly #3779. The native delete removes:
   - the owned secret fields, as today;
   - the deterministic grant name;
   - names matching the PKCE nonce pattern, found through the new bounded exact-prefix Secrets enumeration.

   It never refuses natively: if the enumeration can't complete, it deletes what it can and reports what is left. Only Weaver's delete refuses when the answer is incomplete. Don't delete hand-made names under the inline-field pattern. Reclassify the change as Patch, a defect fix.
2. **Dormancy, per spec 182.** Declare the requirement statically on a new leaf 0 feature class that owns the classed, *performed* and module-code behaviour. Existing Agent features stay unaffected. Drop the per-tool hybrid. Also cite spec 181 for the statement that a host with no fleet never finalizes, or drop that statement.
3. **Source of truth.** #3428 owns the acceptance matrix. FR-001 is the M4 detail that #3428's rows link to. Do not call FR-001 the definition.
4. **Program-goal doc.** In `docs/program-goals/weaver-across-studio.md`, remove the lane's mapping of the authorization onto merging.md and its "while Code Review is unavailable" condition. Quote only the owner's words and link the record.
5. **Workflow run controls, library deletes and Executables lifecycle.** M0 names these ("supported wait/stop/recovery operations", "propose supported lifecycle changes"). Spec 1193 handed them back to #3443. Deliver them as a later M4 leaf in FR-025 instead of narrowing them under ground L.
6. **P3s:**
   - Add an acceptance mapping, or a pointer to Issue Traceability, in #3757's body.
   - Right-size the spec: it is 733 lines.
   - Fix FR-007's wording: the Agent package evaluates the disclosure rule the source declares.
   - Move implementation detail into the plan.

The **round 22 correctness review** was stopped before it reported. Rerun it after applying the above.

Then keep looping. Each round, start two fresh reviewers against the branch head: correctness (claims about code, verified against `origin/main` with file:line) and standards (AGENTS.md, both `.specify/memory/` constitutions, merging.md, repository-hygiene.md, spec-lifecycle.md, ADRs 0077/0078/0080, Studio ADRs, glossaries, `docs/reports/weaver-baseline.md`, precedent specs 1186, 1189 and 1193). Stop when a round has no P1 or P2.

Findings recently shifted from structure to details, but each design change created new P2s. Keep design changes minimal.

## Then

1. **Spec PR #3757:**
   - set **Status: Approved** in the PR's own diff;
   - delete this file;
   - post the evidence comment (rounds, findings, decisions);
   - `gh pr ready`, then wait for CI, then the merge-gate comment, then squash-merge.
2. **Plan PR for leaf 0:**
   - `plan.md` plus `contracts/privilege-policy.md` and `contracts/outcomes.md`;
   - add a dated pointer in the contracts of specs 1186 and 1189;
   - resolve plan decisions PD1–PD3.
3. **Native sub-issues.** Create sub-issues of #3443 for leaves 0–3. Each leaf PR says "Fixes #<leaf issue>, Refs #3443".
4. **Implement leaves 0 to 3, one at a time.** For each:
   - run the self-review loop;
   - run the native witness;
   - wait for green CI, merge, then check `main`.
5. **Later leaves 4–20.** Write each leaf's spec when it starts, numbered per spec-lifecycle.
6. **M5 #3444.** Then close #3433, #3434 and #3428.

## Native witness tooling (private, local)

- **Location:** `/Users/sipke/Library/Caches/elsa-weaver-program/`.
- **Package witness:** `run-witness.py <version>` builds the independent package witness from a clean, exact checkout, `/Users/sipke/.codex/worktrees/weaver-integration-qualification/elsa-foundation`. Detach that checkout to the source SHA first.
- **Composed Studio suite:** `run-composed.py` runs it.
- **Results:** `witness-<sha>-<version>/report.json` and `composed-<sha>.controller.json`.
- **Probe:** `tools/studio/Weaver.Conformance.Probe` (unit tests: `python3 -m unittest`). Its packaged consumer is `fixtures/BackendConsumer`.
- **CI** also runs `Weaver independent package witness` on every PR head.

## Open follow-up issues created during M3 and M4

- #3733: Executable detail reads load every execution state.
- #3735: Studio host's module registry returns feed credentials to the browser.
- #3761: Studio's numeric proposal-status fallback disagrees with the C# enum.
- #3779: deleting an OAuth2 connection leaves its token secret behind.
