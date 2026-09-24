# Implementation Plan: Pinned profile selection planner

**Branch**: `codex/1986-profile-planner` | **Date**: 2026-09-24 | **Spec**: [spec.md](spec.md) | **Issue**: [#1986](https://github.com/elsa-workflows/elsa-foundation/issues/1986)

## Summary

Build a side-effect-free planner for immutable profile/group catalogs and authored selections. It validates content pins, expands exact feature IDs with inclusion/removal reasons, assesses a supplied host inventory without installing or activating anything, and compares accepted composition with an explicitly requested candidate. A later developer command and builder call the same planner. This unit ships no released profile membership or activation path.

## Technical Context

**Language/Version**: C# 14 / .NET 10, UTF-8 JSON documents.

**Primary Dependencies**: BCL `System.Text.Json` and `System.Security.Cryptography`; no CShells, Nuplane, database, network, configuration-provider, or UI dependency in the planner library.

**Storage**: The library parses caller-supplied catalog, authored-composition, and custom-profile documents. It neither writes nor owns files; the user workspace owns custom profiles.

**Testing**: xUnit public-boundary tests in a dedicated Modularity planning test project; architecture guards, generated maps, solution-filter freshness, and exact-head/post-merge CI. Test semantic results and refusal codes, not only private set helpers.

**Target Platform**: Offline .NET callers. No target host or package feed is contacted.

**Project Type**: Non-activatable `Elsa.Modularity.Planning` helper library and tests. Consumer adapters remain later work.

**Performance Goals**: Deterministic bounded catalog planning; no separate throughput gate.

**Constraints**: Stable CShells feature IDs, zero-or-one profile, flat groups, no silent dependency addition/catalog upgrade/settings inheritance/secret export/runtime-ready verdict. Unknown authored settings/resources remain opaque and untouched.

**Scale/Scope**: Four exact-set fixture examples and the negative vectors in [the v1 contract](contracts/selection-planner-v1.md), plus old/candidate re-resolution. Candidate profiles are test data only.

## Constitution Check

Pre-design gate:

- Framework §§2.1, 2.3: Modularity owns feature composition. Keep the nontrivial pure algorithm outside contract-only `Elsa.Modularity.Core` and the Nuplane/CShells implementation. The optional helper has no heavy external dependency.
- Framework §2.19: only stable feature IDs are selection keys; labels, categories, project references and package names do not select features.
- Framework §§2.21–2.23: preserve existing Modularity test subjects and add discriminating public-boundary tests for pin refusal, exact selection, uncertainty and re-resolution.
- Framework §2.22: document schema, refusal semantics and limits; the library does not make candidate catalogs runnable.
- Framework §2.12 and Elsa §E4 remain deferred. The planner carries supplied redacted persistence context but defines no general settings taxonomy. Framework §2.24 and Elsa §E2.9 are provisional and are not approval gates here.

Post-design gate:

| Gate | Design result |
|---|---|
| §§2.1, 2.3 | `Elsa.Modularity.Planning` contains immutable models, strict JSON parsing, canonicalization and pure planning. It never references Nuplane or a host. Existing Modularity Core/Nuplane behavior stays unchanged. |
| §2.19 | Membership/dependency/lock keys are exact feature IDs. Definition IDs and package IDs have separate roles. |
| §§2.21–2.23 | Public planner tests prove failure handling and non-mutation. No existing test is removed or weakened. |
| §2.22 | [Data model](data-model.md), [wire contract](contracts/selection-planner-v1.md) and [quickstart](quickstart.md) separate selection evidence from runtime proof. |
| Deferred sections | No rule is inferred from deferred settings classification or provisional sanctioned-pattern catalog. |

No constitutional exception is requested. The helper project is justified by a meaningful algorithm and the planned command/API consumer boundary, rather than a new activation feature.

## Project Structure

```text
specs/174-profile-selection-planner/
  spec.md  plan.md  research.md  data-model.md  quickstart.md  tasks.md
  contracts/selection-planner-v1.md

src/essentials/Modularity/Planning/
  Elsa.Modularity.Planning.csproj
  Models/  Json/  Services/

tests/essentials/Modularity/Planning/Tests/
  Elsa.Modularity.Planning.Tests.csproj
```

**Structure decision**: The existing Nuplane catalog contributors are inventory evidence sources, not the planner. `FeatureCatalogItem` currently omits its builder's `DependenciesResolved` bit, so an empty list in the materialized management response cannot distinguish a loaded descriptor's authoritative empty list from unknown metadata. The v1 planner takes an explicit snapshot with nullable descriptor-edge evidence and manifest read/compatibility states. A later adapter under #1962 must preserve those distinctions; it cannot synthesize them from today's response.

## Phase 0: Research

[Research](research.md) resolves placement, RFC 8785 canonicalization, inventory authority, pin behavior and persistence handoff. The v1 digest projection admits only strings, arrays and objects with fixed fields; versions/schema versions are strings. A bounded canonical writer sorts object properties by UTF-16 code units and serializes strings per RFC 8785 before hashing UTF-8. It is tested against an independent ECMAScript vector. Opaque authored settings/resources never enter a digest.

## Phase 1: Design and contracts

The [data model](data-model.md) names ownership, identities and validation. [The v1 contract](contracts/selection-planner-v1.md) remains the semantic authority and receives concrete JSON fields and refusal codes before coding. Invalid catalog publication refuses. An authored pin that cannot resolve becomes a visible unresolved result without rewriting accepted locks. Explicit re-resolution compares old and candidate pinned inputs and marks unavailable settings/resource impact unverified.

The [quickstart](quickstart.md) specifies exact fixture and negative-vector validation and limits. No host, database, package feed or UI evidence is claimed by this slice.

## Delivery gates

1. Review plan, data model, wire fields and ordered tasks against all FR/SC. Move spec to `Approved` before implementation.
2. Implement parser/canonicalizer, expansion/provenance, inventory assessment, redacted persistence attachment and non-mutating re-resolution through one public planner boundary.
3. Prove four fixture sets and negative vectors, including duplicate keys, tampered hashes, Unicode/cross-runtime JCS, authoritative empty descriptor, incompatible package and old/candidate drift.
4. Run affected tests, architecture guards, generated maps and solution-filter checks; review diff. If source changes generated maps, refresh and stage every changed map including manifest.
5. Commit/push, pass exact-head PR checks, merge, verify exact post-merge main CI/Maps, then close #1986 and move Project 51 to Done. Mark spec `Implemented` in the implementation PR and update parent/program progress only when proved.

## Complexity Tracking

No exception proposed. A helper project keeps the reusable algorithm separate from Nuplane's CShells and package-management dependencies. If implementation demonstrates a simpler placement without breaking that boundary, revise this plan and check the constitution before moving code.
