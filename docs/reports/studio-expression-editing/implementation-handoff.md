# Fresh-session implementation handoff

## Mission and authorization

The product owner approved the [assessment and complete delivery plan](assessment.md) on 2026-10-02 and explicitly requested a new session to implement it end-to-end as a program. Own delivery through verified integration; do not stop at a new plan, fixture demonstration or first milestone.

Program: [Foundation #2310](https://github.com/elsa-workflows/elsa-foundation/issues/2310). Cross-repository project: [53](https://github.com/orgs/elsa-workflows/projects/53). Canonical queue/bucket: [Studio Expression Developer Experience](../../program-goals/studio-expression-developer-experience.md).

Read the assessment, independent audit, program bucket, repository AGENTS instructions, relevant constitutions/domain context/ADRs, and existing Studio spec 094/Foundation spec 143. Apply the `agentic-program-lead` skill at `/Users/sipke/.codex/skills/agentic-program-lead/SKILL.md`. The approved user intent resolves product scope; research remaining material choices before asking. Defer a language-service technology decision behind a bounded spike rather than letting it block baseline delivery.

## Repository and artifact locations

- Bootstrap Foundation worktree: `/Users/sipke/.codex-workspaces/worktrees/93e49841-957c-4ea6-a07d-ceb76d69bc70/elsa-foundation-main`, branch `codex/studio-expression-developer-experience-program`. Read the committed bootstrap from this path; the fresh task may open in a different checkout.
- Foundation saved project: `/Users/sipke/Projects/Elsa/elsa-foundation-main`.
- Studio: `/Users/sipke/Projects/Elsa/elsa-foundation-studio`. Its assessment-time HEAD was `69acb2ab6a517249cc3442adbcf93ff185c987b9`; it contains pre-existing untracked output directories which must be preserved.
- Canonical assessment, two browser screenshots, audit and this handoff are committed under `docs/reports/studio-expression-editing/`; the program bucket is registered in `docs/program-goals/README.md`.
- Original standalone artifact copy remains at `/Users/sipke/Documents/Codex/Reports/2026-10-02-expression-editor-assessment/`.

## First actions and continuous ownership

1. Refresh issue comments, referencing PRs, program/project state, current repository refs, dirty worktrees and open `main is red` reports. Adopt matching existing work. Re-check before committing and claim each named executable unit before writing code.
2. Take over program ownership explicitly on #2310; the bootstrap lead owns only assessment preservation and handoff, not product implementation.
3. Reconcile the approved plan with current code and existing specs. Establish the product brief, decision log, repository responsibility map and progressively elaborated Program -> Epic -> Feature -> Task hierarchy. Keep concrete near-term acceptance/dependencies and later work coarse. Configure the project fields/views that drive scheduling, including verification state.
4. Lead review/integration/QA on **gpt-5.6-sol / high** per the supplied workroom instructions. Delegate independently verifiable work on isolated branches/worktrees using Luna at extra-high reasoning, with documented model fallbacks. Serialize writers if isolation is unavailable. Keep one integration/merge lane and avoid competing implementations.
5. Start with real normal-host expression editing, then execute all four assessment milestones. Use Speckit for approved feature units. Retain CodeMirror and the existing engine-neutral contract; Foundation remains authoritative for workflow scope and language/runtime semantics.
6. Record state transitions, PR links, review outcomes and validation evidence on issues and keep project fields synchronized. Preserve dirty user work and other sessions' processes. Finish coherent file-changing units with local commits; follow the chosen Git operating model for remote delivery.
7. Complete affected suites and required repository gates, plus real Studio/backend browser journeys. Fixture-only proof does not close integration. Do not run retired performance measurement/workflows. Do not index disposable worktrees with codebase-memory-mcp. Do not declare a gate passed without executed evidence.

## Known state and unresolved delivery mechanics

- Studio #546 merged at `d0258a1af90d21fb07aab956a8c00d14933c8849` on 2026-10-02; it registers/enables the JavaScript/Liquid editor features. Do not duplicate that fix. Its PR explicitly omitted actual workflow-property browser verification.
- During assessment, an existing Studio on port 7221 used a separate worktree and its backend on 7211 stopped. Treat those addresses/processes as historical clues, not current availability. Build isolated matching previews for proof rather than controlling another session's server.
- The investigator used the actual editor components in the repository's Vite browser fixture on port 4179 with synthetic symbols and `BROWSER001` diagnostics. That temporary fixture server and assessment tabs were stopped/closed. No automated suite ran in the assessment.
- Root persistence started from Foundation `e4a699879791fb7eb2bc1c6874f772f4a7fe5355`; revalidate current main and integrate bootstrap docs safely rather than assuming that commit is current.
- The user explicitly chose a fresh implementation session. A Git operating-model question was sent because the bootstrap worktree had no preference file. Check its current `.agent-prefs/git-operating-model.md` and the fresh task prompt for the answer. If still unset, continue discovery, local specs/implementation/commits and ask only before a push/PR/remote change; do not invent remote authorization. Never commit personal preference files.

## End-to-end acceptance

Demonstrate selecting installed supported text syntaxes in real activity-input editors, meaningful JavaScript/Liquid completion from actual location-scoped inputs/variables/outputs, runtime-compatible syntax and APIs, useful diagnostics, readable theme-aware source/help, and graceful unavailable/permission/incompatibility handling. Verify multiline paste, compact/expanded source/cursor/undo continuity, keyboard exit/help/completion, applicable screen-reader flows, signatures and behavior-preserving formatting. Verify producer metadata through the real consumer lifecycle. Keep the program incomplete until all scoped milestones and required integration gates have evidence.
