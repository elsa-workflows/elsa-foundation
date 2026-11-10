# Initiatives: human-led planning

An **initiative** is how people plan an objective that needs architecture decisions before anyone builds it. Technical
architects analyse, propose decisions as ADR pull requests and agree on them. Then the specs are defined, and only then
is the work built. One GitHub issue of the **Initiative** [issue type](https://docs.github.com/en/issues/tracking-your-work-with-issues/configuring-issues/managing-issue-types-in-an-organization) is the check-in point: anyone who opens it can see where
the initiative stands, where they are needed and how to contribute.

The [Initiative Coordination](../skills/catalog.md#initiative-coordination) skill (`elsa-initiative`) carries out every
step on this page.

## Authority

The project is governed by its steering committee. Decision rights follow the roles, appointments and delegations it
documents, not a person's employer, funding or customer engagement. GitHub write permission, opening a PR, or a bot's
publishing identity grants no decision rights either. This page describes how an initiative works within that
governance; it does not replace the governing charter or its appointments. Historical named ratifications stay
historical records; the rules here refer to roles.

## Two planning systems and one way of working

This repository has two planning systems. They are kept separate on purpose.

| | Initiatives (this page) | [Program goals](../program-goals/README.md) |
|---|---|---|
| Led by | A coordinator and the initiative's approvers | A steward, with human or agent execution |
| Answers | Which architecture decisions and specs an objective needs | Which mid-term buckets of work are active |
| Lives in | One issue of type Initiative, its ADR PRs and its specs | `docs/program-goals/` and `type:program` issues |
| A decision is | An ADR PR agreed under its approval plan | As each program records it |

The two systems keep distinct responsibilities over **shared** specs, issues and PRs: an initiative decides what an
objective needs, a program goal governs the delivery work it takes on. Moving work from human to agent execution does
not by itself need a new planning system or a new goal.

Both systems hand work to the same way of working: a spec goes through the
[Speckit flow](../skills/catalog.md#speckit-flow-guide), and issues are claimed and delivered through Issue Bus Lite
and [triage](../agents/triage-labels.md).

**Boundary rule.** Work belongs to an initiative when its issue is a sub-issue of an issue of type Initiative (at
any depth), or when the issue or PR says `Part of #N` and #N is that issue or one of its sub-issues. A PR belongs to it
through the issues it closes or its `Part of #N`. Such work follows this page. All other work follows the existing
rules and is never pulled into an initiative. The initiative skill does not change program-goal files.
Initiative planning and coordination stay outside program-goal buckets and drift review; delivery taken on by a
linked program goal belongs in that goal's tracking and drift review, even when it is also linked to the initiative.

**The bridge.** When a program goal takes on delivery of an initiative's specs, link the existing goal from the spec's
row on the initiative board. Define a new goal only when the delivery work needs a new mid-term bucket, following
the existing program-goal rules. The initiative governs architecture and spec agreement; the linked goal governs
delivery. Both can reference the same specs, issues and PRs. The initiative tracks their status and links without
duplicating the delivery backlog. Human or agent execution does not by itself require a program goal or a new one.

## Roles

One person can hold several roles. Anyone can contribute, and a session helps with permitted work without requiring
a formal role. Actions reserved for the coordinator, a recorded delegate or an approver require that role.
Contribution never makes someone an approver.

| Role | How it is known | What it does |
|---|---|---|
| **Coordinator** | The initiative issue's **assignee**. Reassigning the issue hands the role over. A delegation (for a sync, or while away) is recorded on the board. | Owns **board consistency** and is the **only writer of the board**, or its recorded delegate: syncs incoming work into it, reports pending reviews and blockers, offers reminders, and prepares meetings. Doing research or drafting ADRs is optional, not part of the role. |
| **Approver** (technical architect) | Listed in the board's **Approvers** list, designated under the project's governance, with the capacity they act in. | Writes analyses, drafts and reviews ADRs and specs, and agrees decisions by approving PRs. |
| **Developer** | Not defined yet. It is defined when an initiative first reaches implementation. | |

The **Approvers** list is the roster. **Who is needed where** is navigation only: it says who should act next, and
being listed there, or opening a PR, does not make someone an approver.

"Technical" leaves room for other kinds of architect later, such as domain or user-experience expertise.

## Phases

The board states which phase the initiative is in.

1. **Analysis and decisions.** Architects analyse, propose ADRs and agree on them. Every decision rests on at least
   one thorough analysis. Several analyses are welcome, including analyses that build on another architect's.
   Analyses lead to discussion and, in the end, to decisions.
2. **Specs.** A spec may be drafted at any time, as long as the draft says it is **provisional**. A provisional spec is
   written as if the ADRs it applies were agreed, so its plan and tasks test whether those ADRs work in the code (see
   *Provisional specs* below). Its formal approval needs the ADRs it depends on to be agreed. Each spec row on the board
   names the ADRs and decisions it needs.
3. **Implementation.** Production implementation needs the approved spec and the decisions it applies, landed on
   `main` (see *Landing* below). ADR and spec PRs both follow the final-commit agreement rule below.

Because the initiative branch lands as a whole, every spec is agreed before implementation of any of them starts. An
initiative may agree further, stricter rules. Such a rule holds until the approvers change it. A bounded research experiment is allowed in any phase, but it
never silently becomes production implementation.

No activation by a program goal is needed for any phase.

## How work flows

**Analysis.**
- An analysis is an `A-n` row on the board. It can stand alone, linked from its row, or arrive as a comment on the
  ADR PR it leads to.
- When there is no ADR PR yet, the analysis's author decides whether and when an ADR is drafted from it. Receiving an
  analysis for discussion is not authorization to publish an ADR from it.
- Analyses inside an initiative are not filed as spike issues, and are not posted on the initiative issue.

**Approval plan.** Every ADR or spec PR names its required human approvers, and the capacity each acts in, in its
description. By default that is the initiative's Approvers list. A different plan, or an exception to the rules on
this page, needs an authorized role, a scope, a reason and a record on the PR.

**Alternatives.** Every ADR records, in an **Alternatives considered** section, the alternatives its author weighed
and why each was not chosen, including doing nothing. The section exposes the angles the author used, so a reviewer
can judge whether every real option was seen — or whether none of them addresses the actual problem. A spec does not
repeat this: it refers to the alternatives in the ADRs it applies, and may note a design choice of its own in its
`research.md` — guidance for the author and reviewers, not a checked field. The pre-merge gate's deterministic checks
fail a **newly added** ADR that carries no such section. They do not force one onto a pre-existing ADR the branch
merely amends — a status-only or supersession edit backfills nothing; whether a material amendment must extend the
section is a judgement for the reviewers.

**Review.** The PR requests every non-author approver in its plan, so GitHub notifies them at once. Small points are
settled in PR comments and reviews. A topic that cannot be settled in comments goes to a meeting. If disagreement
persists or an approver is unavailable, the coordinator brings the question to the role authorized under project
governance to resolve it or arrange a delegation. Record any authorized change in the approval plan. Silence is
never approval, and the coordinator cannot remove an approver merely to unblock a PR. An approver reviews an
initiative proposal through the [Initiative Proposal Review](../skills/catalog.md#initiative-proposal-review) method:
first understanding the problem and where it sits, then judging the direction and the author's alternatives.

**Reconciliation.** A review or a position comment that raises more than a couple of points gets a reconciliation: a
table that answers every point.
- **Who writes it:** the PR's author for a review of one PR; the coordinator for a position that spans several PRs or
  decisions.
- **Where:** on the PR for a single review. On the initiative issue for a position across PRs, with a pointer on each
  PR it changes.
- **Each row** has the point, where it lives (PR, ADR section, spec), a status, a named drafter and the answer:
  - **agreed:** the author of the point and the PR text say the same, or will once the drafter applies it (the row
    names the commit when it is applied);
  - **needs clarification:** a question back, asked where the point lives;
  - **differs:** the positions, the current text, and the answer or options.
- **Every point the PR adopts** also gets a short reply on its review thread, naming the commit.
- **A point that changes a document is handled only when its edit is prepared.** When a point means an ADR or spec
  must change, its drafter makes the concrete edit to that document's text in a pushed commit on the proposal's
  branch, kept in draft, and the row names that commit. A point left as a comment, or as a bare "agreed", is **not**
  handled: the edit, not the intention, is what a later reader sees. If every approver agrees, applying it is
  bookkeeping; if anyone opposes, they counter on the PR and the reconciled text is prepared the same way. The drafter
  is the PR's **author** for a single-PR review (the author deduces the text in their own PR), the **coordinator** for
  a position across PRs; the coordinator, working in that PR's scope, may also deduce the verdict from the forum and
  apply it, then ask the reviewers to verify the proposed text. The sync and the pre-merge gate both check that no such
  point is left comment-only.
- **A reconciliation never makes anything agreed.** It says what each PR's text will be. Agreement still follows the
  final-commit approval rule below.
- **Open rows reach the board:** a clarification names who must answer in *Who is needed where*, and a "differs" that
  comments do not settle becomes a meeting topic.

```markdown
| # | Point | Where | Status | Answer | Drafter |
|---|---|---|---|---|---|
| 1 | Exact member matching | #1234 decision 2 | agreed | Adopted in abc1234 | @drafter |
| 2 | Unmatched-key severity | #1234 | needs clarification | Warning or refusal? | @drafter |
| 3 | Outcome API | #1240 | differs | Setters only until a case shows they cannot express it | @drafter |
```

**Provisional specs.**
- **Findings flow back.** When a spec finds that an ADR it applies is incomplete, wrong or not viable, the spec's author
  raises it on that ADR's PR, as a review comment or a row in its reconciliation, and the spec records it as an open
  question. A spec never silently diverges from its ADR.
- **Changes flow forward.** When an ADR changes before it is agreed, the author of every provisional spec that applies
  it rechecks that spec against the new text. Until then, the spec's row on the board shows that it needs a recheck.
- **General defects.** A defect a spec finds that does not depend on the initiative's decisions gets its own issue
  outside the initiative. The spec does not depend on it.

**Surfaces: the document, the forum, the board.** An initiative keeps its thinking on three surfaces, and each holds
a different thing. Keeping them in their places is what makes a proposal cheap to reason about, both on its own and in
the wider initiative.
- **The document** — the ADR or spec text — records only the decision and how it was reached: the problem it solves,
  what it enables, the alternatives weighed, and the context of its time and place. It is written to be read years
  later. It carries **no personal names and no "who is needed where"**: nothing that is only true now.
- **The PR and the issue are the forum.** Discussion, questions, counter-proposals and the live state of the proposal
  live here. The document is **deduced from** the forum, not edited in lockstep with it, and not everything on the
  forum reaches the document. The **title stays stable and general** — it names the topic, not a mechanism that may
  still change. The **description is the live state**: it states the current direction and points to the latest
  decisive comments, so a reader, often an agent, sees from it alone where the proposal stands. The **document may
  lag** the latest direction; when it does, the description says so. **Open questions live as a checklist in the
  description**, each one open or answered with a link — not in the document, and not tied to a named person.
- **The board** holds the only transient routing: **Who is needed where**. An open question, or a "differs" that
  comments cannot settle, appears there as someone who is needed; two approvals may stand in when a reviewer is away,
  and the row is cleared when the question is answered.

**Everyone is offered to contribute; only some are offered to edit the document.** When a session helps anyone take
part in an initiative PR or issue, it offers to record their thoughts, questions and discussion points as a **forum
comment**, so the forum stays the live record. It offers to edit the **document's text** only to the PR's **author**,
in their own PR, and to the **coordinator working in that PR's scope**, who may deduce the verdict from the forum,
apply it to the document, and then ask the reviewers to verify the proposed text. It never offers anyone else to edit
the document, even though anyone with access may push.

**Reconciling the surfaces.** When a finding changes the direction, the description is brought current at once; the
document's text is reconciled when the direction is agreed, through the prepared edit in *Reconciliation* above. The
coordinator checks **surface consistency** at every sync and before every merge: the title, the description and the
document agree on the current direction, no decisive comment is left unreflected in the description, and no finding
that changes a document is left as a comment only. A mismatch is a contradiction and blocks the merge until the
drafter reconciles it.

**Agreement (ADRs and specs).** Both kinds of PR stay drafts until the following steps are complete:
1. Once review has settled the text, the **author** commits the final record: `Status: accepted (<date>)` for an ADR,
   or `**Status**: Approved` for an initial spec approval. Later spec changes retain the appropriate
   [lifecycle status](../reference/spec-lifecycle.md), including `Implemented` when delivery finishes. An approving
   coordinator asks the author to make this edit instead of committing to the PR. A status line alone is not agreement.
2. Every human approver in the approval plan who is not an author approves the **final text**, including that status
   edit: an `APPROVED` review on the current head commit, or on an earlier commit whose text is still the same (see
   below). A later changes-requested review must be resolved by a fresh approval.
3. An author includes anyone who committed to the PR, as the normal merge rules require. A human author named in
   the approval plan records their agreement in a PR comment naming the final commit, without self-approving.
   A bot's publishing identity or approval never substitutes for a human approver's agreement.
4. The coordinator records the agreed commit on the board and marks the PR ready. The coordinator then runs the
   [pre-merge gate](#the-pre-merge-gate) and merges the PR into the [initiative branch](#the-initiative-branch).
   Agreement is not the merge: a PR can be agreed and still wait for a clean gate or a side track.

**Material changes need renewed agreement.** An approval stays valid for a later commit unless a change could
seriously alter that reviewer's standpoint or decision. Trivial, non-semantic edits — the status line, formatting, a
typo fix, wording that does not change meaning — keep the existing approvals, as does a merge of the PR's base (the
initiative branch, or `main`) that only resolves a conflict elsewhere and leaves the PR's own files (the ADR or spec text, and the approval plan in its
description) unchanged. The coordinator records which commits were compared. Re-review is requested only when a change
could materially change a reviewer's position; when it is, the coordinator requesting it MUST state, in the request,
(a) exactly what changed since that reviewer approved, and (b) what it means for the specific answers or decisions that
reviewer gave before — so the reviewer re-checks against their own prior position, not the whole PR again. A changed
approval plan likewise needs fresh agreement under that plan. Keep the PR in draft, or return it to draft, until that
agreement is complete. If an already agreed PR changes materially, mark its board row in review again; dependent
work may use only the version actually agreed. Agreement and merge remain separate steps.

**Scope.** Every ADR, ADR amendment and spec whose topic is on an initiative's board is agreed through that initiative,
whoever writes it, including an architect working outside the board. Its PR is opened as a draft against the
initiative branch, says `Part of #N`, and requests every non-author approver. The boundary rule also covers links through descendant issues and issues the
PR closes. Missing a link does not exempt a board topic: add the link and apply the same gate. Bot review checks
the change, never architectural agreement. A manual merge follows the same agreement rule.

The gate covers proposals and changes to ADR decisions or spec requirements. A delivery PR that only advances a
spec's lifecycle status links the already agreed spec and follows the normal merge path; it does not reopen the
architectural agreement. If it also changes decisions or requirements, the agreement gate applies to those changes.

**An ADR or spec that reached `main` or the initiative branch without agreement** is not agreed, whatever its status
line says. The coordinator:
- shows it on the board as in review, not agreed;
- opens a draft review PR that records in the ADR or spec that it is not yet agreed and links the PR, with any amendment
  the review proposes;
- requests every non-author approver on that PR, which then follows the agreement rule above.

Nothing may build on the unagreed ADR or spec until its review PR is agreed. If the session cannot open that PR,
record the blocker and who must act; dependent work stays blocked.

**Reporting work.** Whoever does work for an initiative reports it where it happened (the PR, a review request) and then
posts one short **pointer comment** on the initiative. Everyone does this, including the coordinator when working as
an architect. Link the source discussion instead of copying it; the board is not a second delivery backlog.
The source record stays visible before the next sync. The pointer looks like this:

```markdown
**Initiative update** (A-6, ADR 0094)
- Done: analysis and ADR draft for <topic>, in #1234.
- Needed: @reviewer-one and @reviewer-two to review #1234 (questions at the end of the ADR).
- Board: add A-6 and the ADR 0094 row; S-4 now also needs ADR 0094.
```

**Sync.** The coordinator's sync handles every pointer that is not yet handled:
- it checks that the work fits the plan, and updates the board;
- it marks the pointer handled with a 👍 reaction from the coordinator or the recorded delegate;
- it applies the agreement rule to the current head and approval plan of each ADR or spec PR;
- it lists the ADR and spec changes merged since the last sync, distinguishes lifecycle-only delivery updates from
  changed decisions or requirements, and opens the required draft review PR for any in scope that needed agreement
  but merged without it (see *Scope*), keeping dependent work blocked;
- it runs the first two steps of the [pre-merge gate](#the-pre-merge-gate) (merge `main` in, check main's changes),
  so drift from `main` shows up even when no proposal is ready to merge;
- it checks the state of every [side track](#side-tracks) and updates its row;
- it sets the "Last synced" line on the board;
- it reports who is blocking whom, and offers reminders and meeting topics, without sending them;
- it also reads the linked PRs and issues, so that work reported without a pointer (for example a PR approved in the
  browser) still reaches the board;
- it checks that every reconciliation row has reached its PR (applied, asked, or on the meeting list), and offers a
  reconciliation for any multi-point review or position that has none yet;
- it checks surface consistency on every open proposal: the title, the description and the document agree on the
  current direction, the description reflects the latest decisive comments and carries the open-questions checklist,
  and no finding that changes a document is left as a comment only — it offers the drafter the prepared edit wherever
  one is missing.

**Check-in.** "Where am I needed on #N?" reads three things:
- the board;
- the pointer comments the coordinator has not handled yet;
- the person's open review requests on PRs linked to the initiative (linked by the boundary rule above).

A new task is therefore visible to the person who needs to act, even before the next sync.

## The initiative branch

Every initiative has one integration branch in this repository, `initiative/<n>-<slug>`, cut from `main` when the
initiative starts and named in the board's header. It holds the initiative's **agreed decision artifacts** and nothing
else: ADRs and ADR amendments; spec, plan and tasks; and the CONTEXT, glossary or constitution edits those decisions
imply. It holds no production code. Because every proposal is checked against everything already on it, the branch is
the initiative's shared, consistent knowledge base: what the initiative has decided so far, in one place.

Work outside the initiative is unaffected. Every rule in this section applies only to **initiative proposals**: PRs
whose base is an `initiative/*` branch, or the branch of a proposal stacked on one.

- **Proposal PRs target the branch.** An ADR, amendment or spec PR in the initiative's scope uses the initiative branch
  as its base from its first draft, so it is written and reviewed against what the initiative has already agreed.
  Authors cut their branch from `origin/initiative/<n>-<slug>`. A spec author gives `speckit-specify` the reserved
  folder (`SPECIFY_FEATURE_DIRECTORY=specs/<reserved>-<short-name>`) instead of letting it pick a number.
- **Stacked proposals** (a change on top of another proposal, such as a reviewer's clarifications) target their
  parent's branch. They are part of the parent: the **parent's author** merges an agreed child into their own branch
  before the parent's gate, and the change counts toward the parent's agreement under the materiality rule. A child
  still open when its parent merges is retargeted to the initiative branch and becomes a proposal of its own.
- **No required CI.** Initiative proposals carry no required checks. Architecture is judged by agreement and the
  pre-merge gate, not by builds and tests, which would only re-run unchanged code. The landing PR runs every required
  check on `main`.
- **The coordinator merges** proposals into the initiative branch, by squash, after agreement and a clean pre-merge
  gate. Code Review does not merge initiative proposals ([org review and merge rules][org-agents-review]). Each
  accepted proposal is one squash commit, so the branch reads as a decision log. The coordinator also pushes the merges
  of `main`, the ADR index updates and any renumbering of merged items; nobody else pushes to the branch. When the
  coordinator authored or committed to a proposal, the recorded delegate, or else a non-author approver, stands in:
  they run the gate, push what it needs and merge the proposal, because an author never merges their own PR. A stand-in
  who is not the recorded delegate does not edit the board: they post the `main` commit and the merge commit in a
  pointer comment, and the coordinator records them at the next sync.
- **Accepted text is amended in place.** Until it lands, an agreed ADR or spec that must change is edited on the branch
  through an amendment PR that follows the agreement rule. No superseding ADR is written for something `main` has never
  seen. Landing closes the initiative: once its branch has landed and been archived, its ADRs and specs are ordinary
  `main` documents that change under the repository's normal rules — the initiative's agreement rule no longer applies,
  and no further proposals are opened against it. Resuming coordinated work on the topic means starting a new
  initiative, not reopening a closed one.
- **Numbers are reserved on the board.** When a proposal is drafted, the coordinator reserves the next free ADR or spec
  number (across `main`, every `initiative/*` branch and all open PRs) and records it on the proposal's board row.
  Proposal PRs do not edit `docs/adr/README.md`; the coordinator updates the index on the branch after each merge. A
  number collision the gate finds is not an architecture call: a proposal's author renumbers it before it merges, and
  the coordinator renumbers an item already on the branch.
- **`main` is merged in, never rebased.** When a proposal no longer merges cleanly onto the updated initiative branch,
  its author merges the initiative branch into it. A merge that only resolves a conflict elsewhere keeps that PR's
  approvals (see *Material changes need renewed agreement*).
- **Protection.** One repository ruleset on `refs/heads/initiative/*` blocks force-pushes and deletion, with
  repository admins as the only bypass. A repository admin creates it once, before the first initiative branch is
  pushed; starting an initiative checks that it exists. Branch deletion at landing is therefore an admin step. The weekly branch sweep never deletes an
  initiative branch ([repository hygiene](repository-hygiene.md)).

An initiative started before this rule gets its branch at the coordinator's next sync, which retargets its open
proposal PRs to it.

### The pre-merge gate

Text can conflict without any Git conflict: two ADRs can contradict each other, or a change on `main` can make an
agreed decision ambiguous. The coordinator therefore runs this gate before every merge into the initiative branch and
once more before landing. The [Initiative Coordination](../skills/catalog.md#initiative-coordination) skill carries it
out.

1. **Sync `main`.** Merge `origin/main` into the initiative branch, as a merge commit that only resolves conflicts,
   push it, and record the `main` commit on the board.
2. **Check `main`'s changes.** Since the previous recorded `main` commit: every changed ADR, spec, CONTEXT, glossary and
   constitution file, and code changes in the projects the initiative's plans name, summarised rather than reviewed.
   Each is checked against the whole initiative branch.
3. **Check the proposal.** In a local, unpushed checkout of the proposal's head with the updated initiative branch
   merged in, the proposal is checked against everything already on the initiative branch, not only the files it
   touches. If that local merge conflicts, the author merges the initiative branch into the PR first; the coordinator
   never commits to a proposal. The coordinator also checks the **surfaces**: the title, the description and the ADR or
   spec text state the same current direction, the description reflects the latest decisive comments, and every finding
   that changes the document has its prepared edit applied. A stale title or description, or a finding left as a
   comment only, blocks the merge until the drafter reconciles it.
4. **Run the deterministic checks** in that same checkout: `python tools/initiatives/check_decision_docs.py`. They find
   duplicate ADR or spec numbers, malformed status lines and a newly added ADR with no **Alternatives considered**
   section, and show which ADR references do not resolve.
5. **Review independently and verify.** Independent reviewers do steps 2 and 3 without seeing each other's output, and a
   fresh reviewer verifies every finding against the text. A finding names both passages and the kind of problem:
   *contradiction*, *ambiguity*, *overlap*, *stale reference* or *number collision*. Unverified findings are dropped.
6. **Post one gate comment** on the proposal PR. It lists the verified findings and the commits compared: the
   initiative branch head, the `main` commit and the PR head.
7. **Decide.** The coordinator triages every finding.
   - A **contradiction**, including a change on `main` that contradicts or undermines the initiative's decisions, is
     an architecture call. The coordinator lays out the options,
     such as revising the proposal or asking the author of the change on `main` to revise or revert it. The approvers
     decide, and the coordinator follows up with whoever has to act.
   - A **number collision** or another error from the deterministic checks is fixed in the text: by the proposal's
     author for the proposal, by the coordinator on the branch for an item already merged.
   - A finding against an item **already on the branch** marks that item's board row *recheck*. Its approval stands.
     The coordinator clears the recheck when the approvers decide no change is needed (recorded on the row), or when
     the amendment PR that changes it merges.
   - **Ambiguity, overlap and stale references** go back to the proposal's author as review comments. They block only
     when the coordinator says so in the gate comment.
8. **Merge.** The gate is **clean** when no contradiction, number collision, deterministic-check error or
   unreconciled surface is open for the proposal. Then squash-merge the PR, push the ADR index update, and record the
   merge commit on the proposal's board row.

### Side tracks

Sometimes a proposal depends on a fix that falls outside the initiative, such as a defect or an unrelated rule that the
decision relies on. That fix is a **side track**:

- It gets its own issue. The issue is not a sub-issue of the initiative and does not say `Part of #N`, because it is
  outside the initiative's scope. It follows the normal rules and lands on `main`, usually through a separate
  user-led session.
- The board's **Side tracks** table lists the issue, the proposal it blocks, its owner and its state.
- The proposal it blocks may still be agreed, but it is not merged into the initiative branch until the fix is on `main`
  and has been merged into the initiative branch by a sync. The pre-merge gate confirms that.
- Closing the issue through its PR is the report back. The coordinator's next sync picks it up.

An ADR amendment on a topic on the board is never a side track: it goes through the initiative.

### Landing

The initiative branch lands on `main` in one PR, once every board row is agreed and the initiative is ready for
implementation:

- The coordinator runs the pre-merge gate against `main` one last time and posts the evidence on the landing PR:
  every row agreed and merged, no open side track, collision or recheck, and the gate comment.
- One approver records in a PR comment that it is the agreed set. That **attestation** stands in for the per-PR
  agreement rule, because every decision in it was already agreed on its own PR. It is not a fresh review of each
  decision and not a review approval, so having authored a proposal in it does not bar an approver from giving it.
- The coordinator tags the branch tip `archive/initiative/<n>-<slug>` and requests Code Review. Code Review approves
  and merges it through the normal [merge path](merging.md), with a merge commit so that `main` keeps the decision
  log. Every required check runs.
- The coordinator moves the board to the implementation phase, and a repository admin deletes the branch, since the
  ruleset blocks everyone else.

## Size guard

A GitHub comment, issue body or PR body holds at most 65,536 characters, and `gh` cannot upload images or PDFs.

Once publication is authorized, the skill handles routine limits itself, keeping all evidence:
- it splits a long text into numbered comments; or
- it links an existing canonical record instead of repeating it.

It stops and asks only when an alternative would change the content (a summary instead of the full text), the
destination (a meeting or discussion instead of a post), or who can access it (a file `gh` cannot upload).

## The initiative board

The initiative issue's description is the board. It holds navigation and status only: links, open decisions, the
approvers and who is needed where. Analyses, discussion and evidence live in the PRs. Only the coordinator, or the
recorded delegate, edits it.

```markdown
Last synced <date> by the coordinator. Newer updates are in the comments below.

# Initiative: <objective>

**Phase:** analysis and decisions | specs | implementation

**Coordinator:** @<assignee>. Delegate: <none, or @person for what and until when>.

**Branch:** `initiative/<n>-<slug>`. Main merged in at `<main commit>` on <date>.

## Goal
<one paragraph: what the objective is and how we will know it is reached>

## Approvers
| Person | Capacity | Designated by |
|---|---|---|

## Who is needed where
| Person | Needed for |
|---|---|

## Decisions
| Id | Decision | Status | Unblocks |
|---|---|---|---|

## Analyses
| Id | Question | Lead | Status (with links) | Blocks |
|---|---|---|---|---|

## ADRs
| ADR (reserved number) | Covers | PR and state | Drafting owner |
|---|---|---|---|

## Specs
| Id | Spec (reserved number) | Issues | Needs (ADRs, decisions) | Can define now? |
|---|---|---|---|---|

## Side tracks
| Issue | Blocks | Owner | State |
|---|---|---|---|

## Landing
- [ ] Every decision, ADR and spec row agreed and merged into the branch
- [ ] No open side track, collision or recheck
- [ ] Final pre-merge gate against `main` clean
- [ ] Landing PR approved and merged; branch archived

<!-- Optional: intake notes per spec (checks to settle when it is defined; issues are created only when a spec is
picked up), and a History list of links to meeting notes. -->
```

The ADRs table shows which decisions each ADR records, so the Decisions table needs no ADR column.

Status values for decisions:
- **agreed**: the recorded ADR commit meets the agreement rule above;
- **in review**: an ADR PR is open;
- **in analysis**: an analysis is open and no ADR PR exists yet.

ADR and spec rows add two states of their own:
- **merged**: agreed and merged into the initiative branch, with the merge commit;
- **recheck**: something it depends on changed (a later gate finding, or a changed ADR for a provisional spec). The
  coordinator clears it once the author has rechecked, the approvers decide no change is needed, or the amendment
  merges.

[org-agents-review]: https://github.com/elsa-workflows/.github/blob/main/AGENTS.md#review-and-merge-rules
