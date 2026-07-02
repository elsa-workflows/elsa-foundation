# Elsa 4 Architecture Review 2026-07 — Detail Reports

Supporting material for [the consolidated review](../elsa-4-architecture-review-2026-07.md).
Point-in-time findings against working tree `ffafa32f` (2026-07-02); line numbers drift, re-verify
before implementing.

## Contents

| File | Scope | Finding IDs |
|---|---|---|
| [roadmap.md](roadmap.md) | **Hand-off implementation briefs for work units W1–W21** — start here for execution | W1–W21 |
| [review-runtime.md](review-runtime.md) | Drainer/scheduler/checkpoint spine, `Workflows/Runtime/**` | RT-1..RT-18 |
| [review-elsa3-comparison.md](review-elsa3-comparison.md) | Elsa 3 vs. 4 engine comparison, parity matrix, concept map | E3-1..E3-11 |
| [review-infrastructure.md](review-infrastructure.md) | Mediator, events, pipelines, serialization, primitives; contributor-pattern verdict | IN-1..IN-15 |
| [review-design-activities.md](review-design-activities.md) | Design-time model, activities, expressions, HTTP, publishing | DS-1..DS-16 |
| [review-persistence.md](review-persistence.md) | EF Core stack, Groundwork bridge, state versioning, concurrency | PS-1..PS-12 |
| [review-modularity.md](review-modularity.md) | Project layout, layering guards, constitution compliance | MD-1..MD-10 |
| [review-naming.md](review-naming.md) | Naming-as-a-system audit; rename families A–E | NM-1..NM-14 |
| [review-tests.md](review-tests.md) | Test quality, behavior-vs-implementation audit, gap list | TS-1..TS-10 |
| [review-misc-domains.md](review-misc-domains.md) | Identity, secrets, agent, tenancy, telemetry, apps, security | MS-1..MS-24 |

## Provenance and reliability

- The nine `review-*.md` reports were produced by parallel domain reviews during the 2026-07-02
  session; all **Critical and High** findings were independently re-verified against source before
  the consolidated report was written. Medium/Low findings were spot-checked.
- Where verification changed a conclusion, the sub-report carries an inline
  **"Verification correction"** note and the consolidated report's wording is authoritative.
  Notably: `review-runtime.md` RT-1/RT-5 originally claimed no incident path exists; verification
  showed `ActivityFaultIncidentRecorder` (in the `Elsa.Activities` domain, outside that report's
  scope) does record incidents — the real gaps are the missing workflow-level `Faulted`
  transition, discarded drain results, and dropped handler-crash work items.
- Findings are review evidence, not ratified doctrine. Durable definitions belong in the
  glossary; enforceable rules belong in the constitution (see the
  [report rule](../README.md#report-rule)).
