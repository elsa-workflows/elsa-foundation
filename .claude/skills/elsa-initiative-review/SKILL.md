---
name: "elsa-initiative-review"
description: "Review an initiative ADR or spec proposal in two phases for a human architect: first understand the problem and where it sits (its own context and the zoomed-out architecture, asking whether the proposal is needed at all), then critically judge the direction and the author's alternatives evaluation, detecting workarounds. Use when a person asks to review a proposal on an initiative branch. Not for delivery PRs, not a merge, and it never edits the document."
argument-hint: "The initiative proposal PR number or link"
compatibility: "Requires elsa-foundation docs and an authenticated gh CLI"
metadata:
  author: "elsa-foundation"
  source: "docs/skills/catalog.md#initiative-proposal-review"
user-invocable: true
disable-model-invocation: true
---

## User Input

```text
$ARGUMENTS
```

## Outline

1. Read `docs/skills/catalog.md#initiative-proposal-review` and `docs/contributing/initiatives.md`. Run this only when a human asks to review an initiative proposal (a PR whose base is an `initiative/*` branch, or a proposal stacked on one). It is distinct from plain grilling and from the delivery review loop (`elsa-auto-review-merge`); it edits no document, runs no merge, and does not stand in for the coordinator's sync or pre-merge gate.
2. Find the initiative and the proposal, and review in two phases. Raise no judging question until phase one is done.
3. **Understand first.** Read the proposal at two zoom levels: in its own context (what problem it solves, what it enables or disables, consequences, what it blocks and unblocks) and against the zoomed-out architecture, the final boundary, since an initiative's scope is always a part of the architecture. The guiding question is "is this proposal needed at all?", answerable only once the context and the reason the ADR exists are understood. Raise no answer-question until you can state, in your own words, the problem, where it sits, and that reason. Then distil the understanding and the "needed at all?" verdict, and offer to post it as a forum comment on the PR; the exploratory detail stays in session.
4. **Then grill.** Judge two things: the proposed direction, and the author's *Alternatives considered* section — was every real option seen, or does neither alternative address the real problem? Detect workarounds fast (many problems we try to solve do not exist): when things do not fit, ask when it last fit nicely, and even then whether that was the best way; a fit is a choice with consequences, not proof of correctness.
5. **Contribute, never edit the document.** Output is forum contributions — agreement, disagreement, questions — offered as PR comments or a review, surfaced through the board's *Who is needed where*. A reviewer who is not the author or the coordinator is never offered to edit the ADR or spec text; keep names and routing on the forum and the board.
