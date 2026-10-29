# Initiatives: human-led planning

An **initiative** is how people plan an objective that needs architecture decisions before anyone builds it. Technical
architects analyse, propose decisions as ADR pull requests and agree on them. Then the specs are defined, and only then
is the work built. One GitHub issue, labelled `initiative`, is the check-in point: anyone who opens it can see where
the initiative stands, where they are needed and how to contribute.

The [Initiative Coordination](../skills/catalog.md#initiative-coordination) skill (`elsa-initiative`) carries out every
step on this page.

## Two planning systems and one way of working

This repository has two planning systems. They are kept separate on purpose.

| | Initiatives (this page) | [Program goals](../program-goals/README.md) |
|---|---|---|
| Led by | People: a coordinator and technical architects | Agents, on behalf of a steward |
| Answers | Which architecture decisions and specs an objective needs | Which mid-term buckets of work are active |
| Lives in | One issue labelled `initiative`, its ADR PRs and its specs | `docs/program-goals/` and `type:program` issues |
| A decision is | An ADR PR approved by every technical architect of the initiative | As each program records it |

Both systems hand work to the same way of working: a spec goes through the
[Speckit flow](../skills/catalog.md#speckit-flow-guide), and issues are claimed and delivered through Issue Bus Lite
and [triage](../agents/triage-labels.md).

**Boundary rule.** Work belongs to an initiative when its issue is a sub-issue of an `initiative`-labelled issue (at
any depth), or when the issue or PR says `Part of #N` and #N is that issue or one of its sub-issues. A PR belongs to it
through the issues it closes or its `Part of #N`. Such work follows this page. All other work follows the existing
rules and is never pulled into an initiative. The initiative skill does not change program
goal files. The program-goal skills do not add initiative work to goal buckets.

**The bridge.** If someone wants agents to build an initiative's specs, they define a program goal that delivers those
specs and link it from the spec's row on the initiative board. The initiative itself never depends on a program
goal. The program goal governs the delivery work; the initiative only tracks the spec's row and links. The
coordinator's sync sees the resulting PRs and closed issues and reviews them like any other linked work.

## Roles

Roles are derived from GitHub, not kept in a separate list. One person can hold several roles. A person without a
role can still do anything: their session infers the role from what they are doing (drafting or reviewing an ADR is
technical-architect work) and nothing is refused.

| Role | How it is known | What it does |
|---|---|---|
| **Coordinator** | The initiative issue's **assignee**. Reassigning the issue hands the role over. | Does most of the initial research. Prepares ADR PRs. Is the **only writer of the board**: syncs incoming work into it, reports pending reviews and blockers, offers reminders, and prepares meetings. |
| **Technical architect** | Listed in the board's **Who is needed where** table. | Writes analyses, drafts and reviews ADRs, and agrees decisions by approving ADR PRs. |
| **Developer** | Not defined yet. It is defined when an initiative first reaches implementation. | |

"Technical" leaves room for other kinds of architect later, such as domain or user-experience expertise.

## Phases

The board states which phase the initiative is in.

1. **Analysis and decisions.** Architects analyse, propose ADRs and agree on them. Every decision rests on at least
   one thorough analysis. Several analyses are welcome, including analyses that build on another architect's.
   Analyses lead to discussion and, in the end, to decisions.
2. **Specs.** A proposed spec may be defined (specify and plan) once the ADRs it needs are agreed. Each spec row on the
   board names the ADRs and decisions it needs.
3. **Implementation.** A spec may be implemented once it is approved, by the same rule as an ADR: every technical
   architect other than its author approves the spec's PR.

No activation by a program goal is needed for any phase.

## How work flows

**Analysis.**
- An analysis is an `A-n` row on the board plus a comment on the ADR PR it leads to.
- When there is no ADR PR yet, the architect's session drafts the ADR, or the amendment, from the analysis and opens
  it as a draft PR. Analysis and proposal therefore always arrive together.
- Analyses inside an initiative are not filed as spike issues, and are not posted on the initiative issue.

**Review.** The ADR PR requests every other technical architect as a reviewer, so GitHub notifies them at once. Small
points are settled in PR comments and reviews. A topic that cannot be settled in comments goes to a meeting.

**Agreement.** A decision is agreed when every technical architect of the initiative, other than the PR's author, has
approved its ADR PR (GitHub does not let authors approve their own PR). The coordinator then:
- commits `Status: accepted (<date>)` on the PR branch, or asks the author to;
- marks the decision agreed on the board;
- marks the PR ready, so it is merged through the normal [merge path](merging.md).

An ADR PR stays a draft until then, so it cannot be merged before everyone has approved.

**Scope.** Every ADR, and every ADR amendment, whose topic is on an initiative's board is agreed through that
initiative, whoever writes it, including an architect working outside the board. Its PR is opened as a draft, says
`Part of #N`, and requests every other technical architect. An approval by Code Review or another bot checks the
change, not the decision, and never counts as agreement.

**An ADR that reached `main` without agreement** is not agreed, whatever its status line says. The coordinator:
- shows it on the board as in review, not agreed;
- opens a draft review PR that records in the ADR that it is not yet agreed and links the PR, with any amendment
  the review proposes;
- requests every other technical architect on that PR, which then follows the agreement rule above.

Nothing may build on such an ADR (a spec, or another ADR's decision) until its review PR is agreed.

**Reporting work.** Whoever does work for an initiative reports it where it happened (the PR, a review request) and then
posts one short **pointer comment** on the initiative. Everyone does this, including the coordinator when working as
an architect. The pointer looks like this:

```markdown
**Initiative update** (A-6, ADR 0094)
- Done: analysis and ADR draft for <topic>, in #1234.
- Needed: @reviewer-one and @reviewer-two to review #1234 (questions at the end of the ADR).
- Board: add A-6 and the ADR 0094 row; S-4 now also needs ADR 0094.
```

**Sync.** The coordinator's sync handles every pointer that is not yet handled:
- it checks that the work fits the plan, and updates the board;
- it marks the pointer handled with a 👍 reaction from the coordinator;
- it applies agreement to ADR PRs that every other technical architect has approved;
- it lists the changes to `docs/adr/` merged since the last sync, and flags any in the initiative's scope that were
  merged without every technical architect's approval (see *Scope*);
- it sets the "Last synced" line on the board;
- it reports who is blocking whom, and offers reminders and meeting topics, without sending them;
- it also reads the linked PRs and issues, so that work reported without a pointer (for example a PR approved in the
  browser) still reaches the board.

**Check-in.** "Where am I needed on #N?" reads three things:
- the board;
- the pointer comments the coordinator has not handled yet;
- the person's open review requests on PRs linked to the initiative (linked by the boundary rule above).

A new task is therefore visible to the person who needs to act, even before the next sync.

## Size guard

A GitHub comment, issue body or PR body holds at most 65,536 characters, and `gh` cannot upload images or PDFs. Before
posting, the skill checks the text. If it is too long, or depends on content it cannot post, it **stops**, says why,
and offers these choices:
1. split it into numbered comments;
2. post a summary and keep the full text with the author;
3. present the evidence in a meeting or discussion instead.

It never picks a workaround on its own.

## The initiative board

The initiative issue's description is the board. It holds navigation and status only: links, open decisions and who
is needed where. Analyses, discussion and evidence live in the PRs. Only the coordinator edits it.

```markdown
Last synced <date> by the coordinator. Newer updates are in the comments below.

# Initiative: <objective>

**Phase:** analysis and decisions | specs | implementation

## Goal
<one paragraph: what the objective is and how we will know it is reached>

## Who is needed where
| Person | Role | Needed for |
|---|---|---|

## Decisions
| Id | Decision | Status | Unblocks |
|---|---|---|---|

## Analyses
| Id | Question | Lead | Status (with links) | Blocks |
|---|---|---|---|---|

## ADRs
| ADR | Covers | PR and state | Drafting owner |
|---|---|---|---|

## Specs
| Id | Spec | Issues | Needs (ADRs, decisions) | Can define now? |
|---|---|---|---|---|

<!-- Optional: intake notes per spec (checks to settle when it is defined; issues are created only when a spec is
picked up), and a History list of links to meeting notes. -->
```

The ADRs table shows which decisions each ADR records, so the Decisions table needs no ADR column.

Status values for decisions:
- **agreed**: every technical architect other than the author approved the ADR PR;
- **in review**: an ADR PR is open;
- **in analysis**: an analysis is open and no ADR PR exists yet.
