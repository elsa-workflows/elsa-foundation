# Research and bounded decisions

Baseline: main `867d5696a0e2c24b6e99b9b666fea9ede609d477`, delivered #2172 / PR #2174. These are specification decisions, not production proof.

## R1 — One candidate, one persistence owner

**Decision**: Build the accepted candidate once through [CompositionCandidateBuilder](../../src/essentials/Modularity/Planning/Bridge/CompositionCandidateBuilder.cs), using one immutable source/input capture; supply those candidate bytes to a new opt-in host-owned inspection operation. Use [EfPersistencePreparation](../../src/essentials/Persistence/EntityFramework/ResourceResolution/EfPersistencePreparation.cs) inside the selected host closure.

**Rationale**: Existing [v2 context](../../src/essentials/Persistence/EntityFramework/Tooling/EfToolingConfigurationContext.cs) reads live host files and prepares current host features; #2172 reproduced both candidate-selection divergence and independent-reader capture divergence.

**Alternatives**: Relabel current v2 list (incorrect candidate); resolve persistence in Planning/CLI (second engine, violates ADR0076); publish a temporary candidate directory then read it (unneeded sensitive files, host-assembly location mismatch).

## R2 — Explicit removals and actual host edges

**Decision**: Extend the shared builder's activation patch to materialize every safe explicit Remove as overlay false, including IDs absent from file-enabled selection. Preserve existing unsupported-shape/settings-loss/base-disabled refusals. Host inspection compares accepted exact IDs, actual requested IDs and expanded descriptor closure; missing/extra/unknown IDs and disabled required edges refuse. Do not auto-repair accepted intent.

**Rationale**: Existing builder diffs only against file-derived enabled IDs and readback uses the same file reader. A composer default absent from files therefore survives a remove. Builder calls SelectionPlanner without HostInventory; catalog edges do not prove runtime descriptor edges. Runtime CShells expansion can re-add disabled dependencies. Independent source audit identifies these as separate facts.

**Alternatives**: Ignore host defaults (false target preview); silently replace all host selections (loses declared prerequisites); implement another graph in CLI (divergent selection engine). Use the existing CShells descriptor/dependency resolver and shared EF result instead.

## R3 — Configured-value affinity

**Decision**: New candidate operation calls preparation with verifyConnectionValues:true. Existing offline v2 remains false. Project only safe identities and existing refusal codes.

**Rationale**: Runtime shell preparer already uses true; the shared validator privately compares distinct configured connection values and returns resource-context-conflict when required agreement fails. Neither equal values nor successful preparation proves physical database identity or connectivity.

**Alternatives**: Preserve false in new preview (weaker than runtime, missed configured conflicts); compare connection values in frontend (second owner and unnecessary disclosure).

## R4 — Private transport and selected-host trust

**Decision**: Add a distinct candidate capability/operation to the existing EF-free worker boundary, separate from v1/v2 tooling and their live connection commands. Request carries captured file bytes privately over stdin, never arguments or staged files. Capability/version/closed shape and bounds must be verified. Candidate process redirects and discards raw host console output; only its dedicated response stream may publish a validated result. Select installed host/package closure only; Restore is forbidden.

**Rationale**: Existing WorkerProcess inherits stderr, writes/reads before its cancellation kill try block and uses unbounded ReadToEndAsync. Existing WorkerRunner can include exception text and package warning text. Reusing those paths unchanged cannot meet candidate safety/lifecycle requirements. Share launch arguments/closure ownership but provide a bounded candidate execution path.

**Alternatives**: Forward current worker output (can reveal values); acquire missing packages (changes inspection authority); pretend arbitrary composers are sandboxed (false). Trusted composer code can perform arbitrary side effects; the Elsa-owned operation does not start services or access databases, and tests use verified non-side-effecting composers. No claim constrains malicious user code.

## R5 — Honest provenance and deferred scope

**Decision**: Show current root/shell-composed/shell-authored provenance where supplied by the existing resolver; mark exact winning-file/provider provenance unavailable. Environment/CLI/custom providers are not added by the file-only producer. Unknown local bytes stay captured and retained; portable import remains reviewed safe leaves/references only.

**Rationale**: [Evidence report](../../docs/reports/runtime-composition/developer-persistence-evidence.md) disproves the suspected inline-reference leak (adapter already validates identifier-like fields), but identifies unavailable fine provenance and incomplete portable unknown export. Do not claim those broad outcomes solved.

**Alternatives**: Guess file origin from filenames (providers/defaults can compose differently); export arbitrary unknown values (unreviewed confidentiality policy); use an environment-enabled existing-host context (does not capture a deployed environment).

## Authority and revisit triggers

Issue2175 specifies one coherent implementation slice. No global configuration policy or constitution amendment. #1962 owns future external capture/exact source provenance/portable unknown policy; revisit before broader parity/export claims. #1964 owns save/apply/recovery. #2064 remains blocked on real representative participants. Broader Authoring/Worker profile productization stays #1961. This slice addresses the current developer explanation gap rather than expanding EF testing cadence.
