# Elsa 4 Website and Documentation

Status: Active — discovery and qualification.

Area: Public Elsa 4 entrance, documentation source assembly, and publishing.

Steward(s): Sipke as owner; program control room for coordination and verification.

## Purpose

Give readers a clear path from the public Elsa website to understanding, evaluating, using, and contributing to Elsa 4, while preserving the Elsa 3 documentation and inbound links.

## Current decisions

- The accepted Elsa 4 documentation address is `v4.elsaworkflows.io` ([#2495 decision ledger](https://github.com/elsa-workflows/elsa-foundation/issues/2495)). This does not authorize DNS or production deployment changes.
- Native GitBook is selected for an isolated Git Sync trial using Foundation-owned Markdown ([#2498](https://github.com/elsa-workflows/elsa-foundation/issues/2498)). This selects the trial, not the production generator, hosting plan, or publication process.
- The completed local Docusaurus proof of concept remains the fallback and comparison point ([#2496](https://github.com/elsa-workflows/elsa-foundation/issues/2496)); it is not discarded or treated as production selection.
- Existing Elsa 3 documentation remains in [`elsa-gitbook`](https://github.com/elsa-workflows/elsa-gitbook). Do not migrate or alter it as part of Elsa 4 qualification.

## Scope

- Reuse canonical Foundation and Studio documentation rather than copying whole guides into a parallel content source.
- Qualify the bounded native GitBook sync, rendering, update/revert, fork-preview, editor round-trip, and actual account-plan entitlements in #2498. A local fixture is not hosted-sync proof.
- Keep source and edit links aligned with the owning repositories and prepare content for the version-neutral `elsa` topology in repository-evolution program [#1970](https://github.com/elsa-workflows/elsa-foundation/issues/1970). This program does not rename or consolidate repositories.
- Keep the public Elsa 4 preview entrance coherent with the existing live website; public-site changes use the website repository's own review and deployment gates.

## Active objectives

1. Record hosted trial evidence and limits on #2498, then recommend whether native GitBook is suitable for the next milestone.
2. Resolve source assembly, version/revision labeling, and review-safe editorial flow before creating an implementation-ready production backlog.
3. Preserve #2496 as the Docusaurus fallback evidence and compare it against the actual GitBook trial, including the fork-preview and hosting constraints.
4. Plan the first public slice and any Elsa 3 navigation or URL changes only after source truth, access, ownership, preview, and rollback behavior are verified.

## Out of scope

No Elsa 3 GitBook edits or migration, repository rename/import, plan purchase, account permission change, DNS cutover, production launch, or public publishing follows from this goal note or the local #2498 fixture.

## Review and completion

Record material product, account, cost, publication, and operational decisions on #2495. Keep Project 57 aligned with issue state. Do not promote a trial result, local prototype, or source inventory into production approval. Completion requires the approved public paths to work, Elsa 3 links to remain valid, and publishing, rollback, and maintenance ownership to be evidenced.
