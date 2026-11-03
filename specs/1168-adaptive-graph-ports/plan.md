# Plan: adaptive graph ports

Spec reviewed against owner request and #3471. Branch: `agent/codex/3471-adaptive-ports`.

Use a small geometry function and a shared React Flow hook in the Workflows client. Read only the current node and its connected neighbours from the existing store; compare total distances to each side, with a small hysteresis margin after initial placement. Preserve one target and existing source IDs, including BPMN `flow`. Group handles per side and allocate distinct fractions; grow height only when many side handles need spacing. Labels sit outside the card to preserve its content, with bounded width and full-name tooltips. Re-measure internals after positions or handle membership change.

Apply at the two node renderers so every authoring and read-only host inherits placement. Keep geometry out of saved node data and document mutation/history. Verify actual edge endpoints after initial measurement, dragging, connection creation/reconnection, undo/redo and remount. Cover multiple outcomes, shared ports, loops, missing peers and suppressed Sequence ports.

Constitution check: framework v6.1.0 and Elsa v6.3.0 Gate indexes and contributor review checklist loaded. Existing valid tests remain; behavior regressions and a red/green control satisfy framework 2.21.1/2.23.2. No new project, dependency, public module contract, feature, persistence or extension-point change. No provisional/draft constitution material is adopted. Architecture/map and exact-head gates remain required.

Review: correctness, repository standards, acceptance coverage; verify findings against concrete behavior, fix in new commits and repeat on the draft before Ready. Use org protocol for final merge and resulting-main checks.
