# Implementation Plan: Toolbox renewals demo
**Branch**: `codex/toolbox-renewals-demo` | **Date**: 2026-10-03 | **Spec**: [spec.md](spec.md)

## Summary
Build feed-loaded Renewals persistence/activity releases and a portable demo authentication bootstrap. Compose authoring, publishing, runtime and identity entirely through packages/configuration on Foundation.Host. A local React/Node cockpit owns setup, two hosts, Studio and a safe reset. Refine the existing static browser presentation to the inspected Elsa Cloud brand.

## Technical Context
C# / .NET 10, CShells, Nuplane, EF Core; React, Vite and Node built-in HTTP/SSE. Shared isolated SQLite initially for portability; PostgreSQL remains an option if actual fleet contention requires it. Validate migration policy. Source-built packages with dev versions for Elsa dependencies, explicit 1.0.0/1.1.0 for demo releases. Frontend runs on loopback, with an in-memory mutation token and fixed action allowlist.

## Constitution Check
No host feature implementation references or contract-layer inversion. Persistence through the existing EF module/schema-family machinery. New sample projects have independent package reasons. No new core abstractions or shared contract changes planned. Existing host baseline, including host-owned membership and Data Protection, is preserved. Use existing schema/upcaster/dormancy contracts; no custom readiness shortcut. Checks remain applicable after design.

## Project Structure
- `samples/Elsa.Samples.Nuplane.Renewals/`: EF sample and two release folders/migrations.
- `samples/Elsa.Samples.Nuplane.Renewals.Activities/`: explicit versioned catalog and runtime activity.
- `samples/Elsa.Samples.Nuplane.Demo.Identity/`: portable local demo identity bootstrap/CORS when existing packages need vendor binding.
- `tools/demo/renewals/`: composition, package closure preparation and isolated host configuration.
- Existing external demo-assets directory: React cockpit, static presentation, cheatsheet, evidence.

## Research and Integration Gates
At the start, authenticated browser operation was unproved in Foundation.Host; the prior authoring fixture only proved anonymous 401. The completed browser proof is recorded in [verification](verification.md). The old Notes/Workbench rehearsal is a reference, never a passed renewal check. Derive package closure from restored/build graphs and the actual host deps, not arbitrary glob pruning. Package release, activity catalog contract and persisted schema stamp are separate. Verify old pinned nodes run the newly loaded compatible implementation. Fleet finalization is one-way; reset uses a fresh isolated DB.

## Verification
Build the unchanged host and CLI once; build/pack the sample releases and portable composition. Inspect package dependencies and actual installed versions. Rehearse real HTTP/persistence/reload under Validate, compare PID and host binary hash. Drive actual Studio sign-in/create/run/upgrade/run/old-node-run in browser. Rehearse two-host dormant/ready transition, reset then repeat. Review cockpit/presentation visually and keyboard flows. Run targeted sample/architecture and relevant existing backend e2e suites; report any unavailable gates. Commit reviewed source; publish only presentation assets, never local operational credentials.
