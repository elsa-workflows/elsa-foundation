# Moderated runtime-builder evaluation packet

Preparation for [#2064](https://github.com/elsa-workflows/elsa-foundation/issues/2064). Use the [three research concepts](index.html) and the [interaction-study context](README.md). This packet is a script and recording aid, **not** participant evidence or a product decision. The mocks use a synthetic source and provisional Worker/diagnostics definitions. Their download is illustrative JSON, not a deployable CShells file. No host, database, package feed, or secret is contacted.

## Recruit and set up

Recruit three people who have configured Elsa as developers and three people who review deployment/configuration changes as operators but are less familiar with individual feature IDs. A participant may know the product, but should not have worked on these mocks. Record only a participant code (`P01`–`P06`), role, approximate relevant experience, and observations; keep contact and scheduling details outside this repository. Ask whether typed notes and anonymous excerpts may be shared. Do not record audio, video, screen, names, real connection values, or customer configuration in this research file. If someone declines notes, thank them and recruit a replacement rather than treating the session as evidence.

Plan about 40 minutes: introduction (3), three concept rounds (10 each), and a final comparison (5). Run each round from a fresh reload of `index.html` in a desktop browser at a legible zoom, then select the assigned concept. Use the same browser, viewport, and input method for all three rounds in a session. If remote, let the participant control the shared browser; the moderator should not click through the task. Do not preload a draft or reveal another concept during a round. Check that the page opens and the buttons, feature decisions, export, and reopen work before the first session. Keep downloaded mock JSON in a disposable local directory and remove it after extracting non-secret observations.

Rotate concept order to balance first exposure within each role:

| Participant | Role | Round 1 | Round 2 | Round 3 |
|---|---|---|---|---|
| P01 | Developer | Profile ledger | Work order | Expert workbench |
| P02 | Developer | Work order | Expert workbench | Profile ledger |
| P03 | Developer | Expert workbench | Profile ledger | Work order |
| P04 | Operator | Profile ledger | Expert workbench | Work order |
| P05 | Operator | Work order | Profile ledger | Expert workbench |
| P06 | Operator | Expert workbench | Work order | Profile ledger |

If a participant is replaced, keep that code/order. If fewer than six people are available, record a pilot and keep the acceptance conclusion open; do not extrapolate it to the planned six-session threshold.

## Say at the start

> We are testing three early ways to describe a runtime configuration, not testing you. Each uses a sample scenario. Please work as you normally would and say what you expect before taking an action. I will mostly observe. You can stop at any time. I will take anonymous notes about the interaction, not record the session. Please avoid entering real credentials or customer information.

Ask whether the participant has configured Elsa or reviewed deployment changes before, and record the role/experience in broad terms. Do not explain the feature dependency, intended resource arrangement, or export boundary beyond the introductory statement. If the participant gets stuck, first ask “What would you expect to try next?” After a sustained impasse, offer the minimum neutral prompt needed to continue and record its exact wording and time. Do not count an assisted task as unassisted completion.

## Give the same four tasks in each round

Read each task verbatim. Start a timer when the task is given; stop when the participant declares it complete or moves on. Observe the route taken rather than naming a control. Each round starts from a fresh page, so the import in task 4 intentionally replaces the earlier draft.

1. **Starting point.** “Prepare a new draft for a runtime that can execute workflows over HTTP. Before moving on, show me how you interpret its individual feature list.”
2. **Persistence.** “Use PostgreSQL for the main workflow state through the `ConnectionStrings:Elsa` reference. Add diagnostics stored with EF, but send the diagnostics stores to `ConnectionStrings:Diagnostics`. Show me which choices are defaults and which are exceptions. Do not enter a connection value.”
3. **Dependency refusal.** “Try turning off `Tasks` while keeping the runtime behavior you chose. If the tool refuses, decide how to recover. Tell me what changed and what stayed selected.”
4. **Existing source and export.** “Start from the sample shell. Change its executable-cache capacity from 128 to 256. Find the unfamiliar imported setting and tell me what you know, and do not know, about its fate. Export the candidate, reopen it, and tell me whether any running host changed.”

After task 3 and **before the sample import resets the draft**, ask: “Which exact features are now selected? Why is `Tasks` there? Where would the two diagnostics stores go?” Before task 4's export action, ask: “What is still unchecked? What would exporting change?” After reopen, ask what they think happened to the unfamiliar sample setting. Record each answer before showing or discussing the downloaded JSON. At the end of each round, ask for confidence in the proposed change on a 1–5 scale and one thing they would inspect before trusting it. After all rounds, ask which concept they would choose for their role, where they expected to find advanced feature edits, and whether the question-led flow helped or delayed them. Do not reveal a preferred concept before this comparison.

## Moderator-only answer key and interpretation

The provisional Worker selection contains 14 authored IDs. The mock also selects `Tasks` because `WorkflowsRuntimeResumption` requires it; the exact proposed set is 15 before diagnostics, 19 with the four diagnostics features. `Tasks` cannot be removed while retaining that resumption chain. “Keep required feature” retains the intended set; removing the dependent chain changes the goal. Record whether the participant understood that tradeoff rather than merely clicking a recovery button.

`primary` is the default PostgreSQL resource reference. The diagnostics gesture selects four features, and isolation creates **two explicit feature bindings** for `DiagnosticsOpenTelemetryEntityFrameworkCore` and `DiagnosticsStructuredLogsEntityFrameworkCore`. It is not persistent group-level settings inheritance. The screen displays reference names, never connection values.

The sample source is synthetic. `AcmeAuditSink.FuturePolicy` is unknown and unchecked. The mock describes what a real file bridge should preserve, but its JSON download explicitly says the unknown value is **not exported by this research mock**. Reopen restores a synthetic source marker, not proof that the value survived a file round trip. Record the participant's understanding of this limitation; actual unknown-value preservation cannot be tested with these mocks. Export and reopen alter only the local mock draft; they do not generate deployable CShells files or change a running host. Package availability, effective process overrides, provider connectivity, database topology, and migration safety remain unchecked. These are factual boundaries of this prototype, not answers to feed participants during a round.

## Record one row per participant and concept

Use a private copy of this table or an equivalent sheet; publish only anonymized findings. For each of the four tasks, record `done / partial / not done`, elapsed seconds, wrong turns, and intervention count. A wrong turn is a distinct action taken toward the wrong state, not time spent reading. Record any accessibility barrier separately, including keyboard/focus or viewport issue.

| Field | Record |
|---|---|
| Participant, role, concept, order | Code and round number; broad experience only |
| Tasks 1–4 | Completion, seconds, wrong turns, interventions, and exact recovery choice |
| Comprehension at task 3 / before export | At task 3: exact set/reason, `Tasks` dependency, default and two explicit diagnostics bindings. At task 4: unknown setting, unchecked evidence, file-only versus live-host answer |
| Unknown setting after reopen | `yes / no / unclear`: did the participant believe its value survived export/reopen? Quote their explanation and mark whether they recognized that the mock cannot prove a round trip |
| Confidence and preference | 1–5 after round; final preferred concept and why |
| Notable words and barriers | Short anonymous quotation if useful, visibility/focus/search issue, moderator prompt used |

Use `yes / no / unclear` for each comprehension item. Do not turn “unclear” into “yes.” Distinguish a participant's mistaken live-host assumption from a later correction prompted by the UI. Note when an answer came only after moderator assistance.

## Synthesize after the sessions

Compare each concept across all six participants, then inspect developer/operator differences and order effects. Report task completion, median elapsed time, wrong turns, assistance, confidence, and verbatim misunderstandings alongside the raw anonymized rows. The predeclared bar from [the interaction study](README.md#real-user-evaluation-before-implementation) is applied **per concept**: all six participants must identify export as file-only and the two diagnostics bindings, and at least five of six must recover from the dependency refusal without losing the intended set. State clearly when a concept misses the bar; do not average away a safety misunderstanding. A pilot with fewer people is useful for fixing the protocol but cannot pass this bar.

Choose a direction only from observed results. Address whether a profile ledger should be the default, whether the expert view should be separate, and whether a work-order path improves comprehension enough to maintain. Record changes to labels, disclosure, review, keyboard behavior, and provenance explanations that participants actually needed. End with a bounded follow-up issue proposal and the unresolved product placement and authenticated server-bridge decisions. Human interaction findings do not certify the provisional Worker profile, runtime activation, persistence topology, migrations, or live apply.
