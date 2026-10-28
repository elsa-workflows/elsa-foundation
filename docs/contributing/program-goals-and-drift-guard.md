# Program goals and drift guard

Moved from `AGENTS.md` (#2578). `AGENTS.md` keeps a short summary.

`docs/program-goals/` is the shared backlog and planner for durable work in this workspace. Program goals are mid-term buckets of related short-term objectives, not mandatory doctrine for every session.

Use a named program-goal bucket when work forms part of a mid-term coordination surface. Use `none/free-flow` when the user is exploring, developing, researching, or planning without a named bucket. Free-flow work can still produce reports, specs, docs, maps, or code; it just should not pretend to belong to a bucket.

Work linked to an issue labelled `initiative` (a sub-issue at any depth, or `Part of #N`) is planned by that initiative, a separate human-led planning system described in [Initiatives](initiatives.md). Do not add it to a program-goal bucket, and do not count it when ranking or reviewing drift. To have agents deliver an initiative's specs, define a program goal that delivers them; the initiative links to it. The program goal then governs that delivery work, and the initiative only tracks the spec's row and links.

Reports such as `docs/reports/unfinished-work.md` are inventories of findings and concerns, not the active queue. When a report finding becomes planned durable work, add or move it to the relevant program-goal bucket before implementation, or explicitly mark the state as `none/free-flow` for work that should remain outside a bucket.

Use a lightweight drift guard instead of a mandatory zoom-out at every session start. Trigger a program-goal check when one of these is true:

- The user asks "what next", asks whether the work is drifting, or asks to revisit priorities.
- The same session or handoff chain is moving into a third consecutive plan/work unit under the same topic.
- Recent work is repeatedly deepening one local area while other tracked work is waiting.
- A proposed task would create a new durable tracking item, move work between buckets, split a bucket, or retire a bucket.
- A fresh session resumes from context that already says drift, priority, or roadmap alignment is in question.

When triggered, keep the check concise:

- Which program-goal state applies: a named bucket, `none/free-flow`, or temporarily `unknown/not-assessed`?
- If it is planned durable work, which bucket owns it, or why should it remain `none/free-flow`?
- Is this still the highest-value next step, or are we staying in a local area because it is nearby?
- Does the work preserve the [source-of-truth layers](../../AGENTS.md#source-of-truth-layers)?
- Should the result be a gate, glossary term, reference explanation, report finding, generated map, skill workflow, spec, or code change?

For ordinary fresh sessions with a clear task, do not announce a zoom-out check. Apply the guard quietly unless a trigger is present. If the check suggests drift, surface it briefly so the user can continue knowingly, redirect, or update the relevant program-goal bucket.
