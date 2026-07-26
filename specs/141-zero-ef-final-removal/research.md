# Research: Zero-EF Final Removal

## Decision 1: Final deletion is a gated integration lane

**Decision**: Treat #642 diagnostics, #643 OpenIddict, #646 performance, and #932 dashboard provider parity as hard prerequisites. Verify their accepted evidence on remote `main` before deleting any EF oracle they use.

**Rationale**: The frozen Identity and diagnostics/OpenIddict EF surfaces are temporary correctness/performance oracles. Deleting them early makes the required comparison impossible and can turn missing behavior into an apparently clean dependency audit.

**Alternatives considered**:

- Delete EF as soon as a Groundwork adapter compiles: rejected because correctness and performance evidence would be destroyed.
- Preserve an untracked local oracle: rejected because completion evidence would not be reproducible or reviewable.

## Decision 2: Use a baseline for intake, absolute zero for the permanent gate

**Decision**: Continue shrinking `ef-core-surface.json` during vertical deletions, then delete the baseline and `ELSA_UPDATE_EF_CORE_BASELINE` path. Retain the scanner and change the production assertion so every category must be empty.

**Rationale**: A baseline is useful while the surface is intentionally nonzero, but an allow-list at the end could normalize a regression. Absolute emptiness is the durable boundary.

**Alternatives considered**:

- Keep an empty JSON baseline: rejected because the update mechanism remains an unnecessary bypass surface.
- Replace the scanner with source-text search: rejected because text search does not prove evaluated or transitive dependencies.

## Decision 3: Complete-graph certification must fail closed

**Decision**: Discover every repository project independently of `Elsa.Server.slnx`, require current `project.assets.json` evidence for all of them after a forced evaluated restore, and inspect direct/static/restored/imported dependency surfaces.

**Rationale**: A project can be omitted from the solution or acquire EF through imports, conditions, or transitive packages. Missing restore evidence is unknown, not zero.

**Alternatives considered**:

- Scan only the solution graph: rejected because project omission bypasses the guard.
- Permit projects without assets: rejected because unresolved dependencies can hide EF.

## Decision 4: Test preservation uses a ledger plus a reachability addendum

**Decision**: Inventory direct EF-token tests first, then trace shared fixtures, host builders, and transitive project references to add tests that reach EF without containing a token. Every test method receives a preserve/convert/remove disposition.

**Rationale**: Spec 093 proved token-only inventories miss behavior reached through shared hosts. Framework §2.21.1 requires preserving the subject and objective, not merely deleting EF-referencing files.

**Alternatives considered**:

- Delete entire EF test projects: rejected because provider-neutral behavior and host composition objectives may live there.
- Trust replacement-suite names: rejected until the cited test is opened and shown to cover the objective.

## Decision 5: Delete from leaves to substrate

**Decision**: Remove diagnostics EF, OpenIddict EF, Identity EF oracle, shared `Elsa.Persistence.EFCore{,.Sqlite}`, then EF packages/configuration. Remove each family only after its final dependent and oracle gate clears.

**Rationale**: The shared substrate and central packages are dependencies of the vertical leaves. Leaf-first deletion keeps failures attributable and allows the temporary ratchet to shrink honestly.

**Alternatives considered**:

- One repository-wide delete commit: rejected because it obscures missing replacement behavior and makes test preservation harder to review.
- Delete the substrate first: rejected because it breaks remaining oracles before their gates.

## Decision 6: One host-level provider choice is universal

**Decision**: Each maintained host composition must use one Groundwork provider across every enabled durable lane. Missing capability/schema is a readiness failure, never feature omission, EF fallback, or in-memory substitution.

**Rationale**: Parent #629 promises coherent provider selection. A nominally zero-EF host that silently drops diagnostics, dashboard data, or authorization storage violates that promise.

**Alternatives considered**:

- Provider-specific feature omissions: rejected for required reference-host features.
- Mixed providers inside one reference host: rejected because it falsifies one-choice composition.

## Decision 7: #932 is part of #647

**Decision**: Require SQL Server and MongoDB run-health/portfolio support before final host certification, unless the program owner ratifies an explicit non-support amendment with evidence.

**Rationale**: SQLite/PostgreSQL already wire dashboard sources; SQL Server/MongoDB currently cannot. Closing #647 without parity would silently narrow the supported host shape.

**Alternatives considered**:

- Leave #932 as post-program debt: rejected by the ratified #647 issue body.

## Decision 8: OpenIddict is a separate delivery lane inside the same completion gate

**Decision**: Retain wording that #643 owns OpenIddict delivery, but state everywhere that #647/#629 cannot complete until OpenIddict EF source and dependencies are gone.

**Rationale**: Delivery ownership and program completion membership are different questions. The program goal and ADR require OpenIddict inside zero EF.

**Alternatives considered**:

- Treat "separate migration lane" as out of scope: rejected because it contradicts the accepted completion condition.

## Decision 9: Temporary benchmark artifacts are evaluate-then-delete

**Decision**: Keep the EF oracles and temporary comparison code until #646 verdicts and coverage-ledger imports are complete, then remove the EF-only harness/oracle code in #647 while retaining durable reports, summaries, hashes, and native-plan identities.

**Rationale**: Raw comparison machinery is temporary; the verdict record must remain auditable after EF deletion.

**Alternatives considered**:

- Keep EF benchmark projects permanently: rejected by the program boundary.
- Delete before verdict import: rejected because the evidence chain would be incomplete.

## Decision 10: Serialize shared-file integration

**Decision**: Only the final-removal integration lane edits `Directory.Packages.props`, `Elsa.Server.slnx`, `shells*.json`, or the coverage ledger at one time. Vertical lanes land provider code/evidence first.

**Rationale**: These files are cross-lane merge and truth surfaces. Serialization prevents stale overwrites and keeps the package/host/evidence story coherent.

**Alternatives considered**:

- Let all lanes edit shared files concurrently: rejected because it creates high-risk merge drift and misleading issue/project state.

## Decision 11: Governance changes update the narrowest sources of truth

**Decision**: Amend Elsa constitution §E2.5 only where temporary EF-specific guidance becomes obsolete; keep the accepted provider choice in ADR 0042 and the completion state in the program goal/decision map. Refresh generated maps after implementation inputs settle.

**Rationale**: Constitution carries gates, ADR carries the accepted decision, program goal carries active/completed coordination, and maps carry generated facts. Duplicating explanations would create drift.

**Alternatives considered**:

- Rewrite broad persistence architecture in the constitution: rejected as unnecessary scope expansion.
- Update docs without generated maps: rejected because committed navigation would remain stale.

## Decision 12: Review and closure evidence is exact-head

**Decision**: Freeze the final candidate, run three read-only adversarial reviews over the exact commit range, remediate confirmed findings, have originating reviewers re-verify, merge with a merge commit, then verify remote `main` before closing #647/#629 and Project 33 items.

**Rationale**: The program's prior lanes found real blockers through adversarial review. Issue/project truth must describe code actually present on remote `main`.

**Alternatives considered**:

- Review a moving branch: rejected because verdicts would not bind to the merged candidate.
- Close issues when the PR is green: rejected because green checks do not prove merge presence or every completion criterion.
