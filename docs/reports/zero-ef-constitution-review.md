# Critical Constitution Review: Zero-EF Persistence Boundary

Status: proposed findings for decision-PR review (2026-07-12).

Program goals: [Zero-EF Persistence](../program-goals/zero-ef-persistence.md) and [Constitution Readiness](../program-goals/constitution-readiness.md).

Target: Elsa constitution §E2.5, with adjacent §E2.4, the §E1 domain/package inventory, §E2.2.1 Design package list, the EF-specific implementation wording in §E2.9.7, the §E5 minimum-project-size example, and framework §§2.9 and 2.20 checked for consistency.

## Findings

### F1 — §E2.5 will become stale implementation documentation, not a useful gate

Elsa §E2.5 currently describes `ElsaDbContextBase`, `EFCoreReadStore`, EF-specific save/load hooks, and generic constraints. The agreed end state removes those types and every EF Core dependency from this repository. Retaining the section afterward would document a capability Elsa Foundation no longer ships and would contradict code reality.

Classification: constitution drift once the zero-EF implementation lands; presently an explicit pending amendment.

### F2 — The durable invariant in §E2.5 is provider independence, not EF extensibility

The reusable rule is already stated by framework §2.9: persistence contracts and invariants are provider-independent, and concrete providers enforce them through their native mechanisms. The current Elsa specialization spends most of its text preserving an optional EF mechanism. The new repository boundary needs a short Elsa-specific gate instead: core contracts remain provider-neutral and Groundwork-free, while this repository ships only Groundwork concrete durable implementations.

Classification: gate obscured by obsolete provider explanation.

### F3 — The zero-EF direction is compatible with framework §§2.9 and 2.20

Framework §2.9 permits application-level EF infrastructure but does not require it. Framework §2.20 requires generic features to depend on contract layers and permits provider-suffixed implementation modules. Choosing Groundwork as the only first-party implementation family in this repository narrows Elsa's product composition without changing either framework rule.

Classification: no framework-constitution contradiction and no framework amendment required for this work unit.

### F4 — §E2.4 is too general to enforce the selected provider boundary by itself

Elsa §E2.4 says the foundation repository contains default implementations needed for local development and that heavy providers may move out. That is compatible with the decision but does not prevent a future EF implementation from returning. The enforceable repository rule belongs in a replacement §E2.5 (or an equally explicit Elsa specialization), backed by an architecture test.

Classification: missing Elsa-specific gate.

### F5 — Current code and host composition still contradict the desired end state

The reference server directly references EF Core implementations for Activities Design, Workflows Design, Structured Logs, OpenTelemetry, and ASP.NET Core Identity in [`Elsa.Server.csproj`](../../src/Apps/Elsa.Server/Elsa.Server.csproj). Its default shell activates the corresponding EF features in [`shells.json`](../../src/Apps/Elsa.Server/shells.json). OpenIddict also consumes its EF Core integration, and [`Directory.Packages.props`](../../Directory.Packages.props) centrally versions both ASP.NET Core Identity EF Core and OpenIddict EF Core packages.

This is expected transition evidence, not a reason to weaken the decision. EF remains a temporary oracle until Groundwork parity gates pass.

Classification: planned code drift relative to the proposed gate.

### F6 — “Only Groundwork” must not be misread as coupling core contracts to Groundwork

Framework §§2.9 and 2.20 already forbid that coupling. The amendment must distinguish the repository's concrete implementation-family policy from the domain contract boundary. It must also allow specialized Groundwork primitives for operational/time-ordered workloads rather than mandating that every store use `IDocumentStore`.

Classification: ambiguity to eliminate in amendment wording and architecture tests.

### F7 — Identity and OpenIddict are required exit criteria, not exceptions

A source-only scan could remove Elsa's obvious `*.Persistence.EFCore` projects while leaving EF transitively through ASP.NET Core Identity or OpenIddict. The gate and its test must cover direct and transitive package dependencies across applications and tests.

Classification: missing explicit exception handling in the current constitution; resolved by declaring no framework-store exception.

### F8 — Greenfield status removes a migration obligation but not schema-evolution obligations

No released EF data needs conversion. That does not remove the need for provider-neutral Groundwork schema planning, backfill, validation, and deployment operations for future versions. The constitution should record only the durable provider boundary; migration mechanics and CLI sequencing belong in Groundwork specs and the decision map.

Classification: scope boundary between gate and implementation plan.

### F9 — EF-specific inventory and worked examples must change with the gate

The constitution contains four additional EF-specific surfaces that would remain stale even if §E2.5 alone were replaced:

- the §E1 `Elsa.Persistence` domain row names `Elsa.Persistence.EFCore{,.Sqlite}`;
- §E2.2.1 lists `Elsa.Workflows.Design.Persistence.EFCore` and `.EFCore.Sqlite` as the Design persistence implementation;
- provisional §E2.9.7 normatively describes `EFCoreReadStore<TDbContext, TEntity>` and tracked `DbContext` behavior inside draft commands;
- §E5 uses `Elsa.Persistence.EFCore.Sqlite` as a current minimum-project-size worked example.

The first, second, and fourth surfaces are inventory/example drift and should be updated when the projects disappear. The §E2.9.7 sentence is more important: retain the named-read-port and unit-of-work intent while removing EF-specific implementation mechanics, or move those mechanics to a worked reference if they still provide historical value.

Classification: amendment/removal checklist required alongside §E2.5; §E2.9.7 needs targeted meaning-preserving review rather than mechanical renaming.

## Proposed Revision Path

1. Review and accept [ADR 0042](../adr/0042-elsa-foundation-ships-only-groundwork-persistence-implementations.md) in a decision-only PR. Keep the ADR `proposed` until that review is complete.
2. Resolve the decision-map tickets that can change the meaning of the boundary, especially framework-store coverage and specialized diagnostics storage. Implementation mechanics need not all be complete before wording is proposed.
3. Prepare a narrow Elsa-constitution amendment that replaces §E2.5 rather than layering exceptions onto its EF-specific text. In the same compliance change, update the §E1 persistence inventory, §E2.2.1 Design implementation list, §E5 worked example, and the EF-specific implementation sentence in provisional §E2.9.7 while preserving its named-read-port and mutation-unit-of-work intent. Preserve superseded text in constitution history/amendment records according to existing governance.
4. Proposed gate shape for review, not direct insertion:

   - Core modules own provider-neutral persistence contracts, models, and invariants and MUST NOT depend on Groundwork or another concrete provider.
   - `elsa-foundation` ships Groundwork-backed concrete durable implementations only; specialized Groundwork storage contracts are allowed when workload semantics require them.
   - Reference hosts and tests MUST have no direct or transitive EF Core dependency after the transition milestone.
   - ASP.NET Core Identity and OpenIddict persistence are within the same boundary, with no EF exception.
   - An architecture test enforces contract independence and the absence of `Microsoft.EntityFrameworkCore*` in the complete project/package graph.

5. Do not ratify the replacement gate as already satisfied. Either merge it with an explicit time-bounded transition exception linked to the Zero-EF program goal, or merge it when the final removal slice makes the repository compliant. The latter is simpler and avoids normalizing a broad temporary exception.
6. Ratify the constitution amendment by consensus among Joey Barten, Sipke Schoorstra, and Frans van Ek. On acceptance, apply the appropriate draft version bump and Sync Impact Report, update the constitution amendment index and history, change the ADR status to accepted if it was not accepted by the earlier decision PR, and link the enforcing architecture test and completion evidence.

## Recommendation

Proceed with ADR review and implementation planning now, but defer the actual §E2.5 meaning change until the amendment PR can either land atomically with compliance or name a tightly bounded transition exception. Do not alter the framework constitution for this repository-specific provider choice.
