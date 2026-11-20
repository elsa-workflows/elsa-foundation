---
status: accepted
date: 2026-11-05
---

# Decisions owns business-decision semantics

Status: Accepted on Sipke Schoorstra's explicit authority in the owning session on 5 November 2026. [Approval receipt](https://github.com/elsa-workflows/elsa-foundation/issues/3602#issuecomment-6670626201). This records owner-approved domain admission, not consensus of all architects or completed implementation. Work: [#3602](https://github.com/elsa-workflows/elsa-foundation/issues/3602), [spec 1195](../../specs/1195-business-decisions/spec.md).

## Decision

Admit optional `Elsa.Decisions`, with purpose **“Evaluates versioned business decisions and explains their results.”** It owns business-decision assets, the declared DMN profile, validation/evaluation, actual rule explanations, immutable publication and representative saved cases. Evaluation returns data; external business actions belong to callers.

The initial closed profile is [elsa-dmn-table-v1](../../specs/1195-business-decisions/contracts/dmn-profile.md): bounded DMN 1.3 independent tables, exact scalar values, literal outputs and five hit policies. Unsupported constructs are explicit and cannot execute/publish through silent omission. XML import/rendering alone does not qualify evaluation. This decision does not assert full DMN/FEEL or Camunda compatibility.

## Domain ownership and execution

[ADR0088](0088-rules-owns-its-execution-domain.md) remains intact: Rules owns finite positive fact inference and durable rounds. Those fact/premise/conclusion/receipt contracts are not repurposed as typed decision tables. A separate domain reflects different semantics, while reuse follows existing public contracts.

The direct evaluator is a pure, dependency-light .NET library. Ordinary native/BPMN callers use it inside their existing execution authority, checkpoint, retry, cancellation and recovery lifecycle. Publication freezes exact decision content/profile/semantics rather than making runtime read mutable Design storage.

The shared model execution foundation owns independently durable hosting. If Decisions later needs first-class durable runs, it contributes model material and a protocol driver through the existing model-state/checkpoint ports. It must not introduce another scheduler, ownership/fencing system, accepted-work ledger or coordinator. [Concrete integration contract](../../specs/1195-business-decisions/contracts/execution-integration.md).

Saved-case evaluation is bounded synchronous computation with atomically stored completed receipts and exact request fingerprints. A lost response replays an existing receipt; interruption before receipt commit accepts no durable job and cannot silently retry newer content. This avoids inventing a separate test-job engine.

## Packaging and authority

Core contains immutable values and compiler/evaluator ports only. The default implementation isolates parsing/evaluation from Design, EF, HTTP, Studio and workflows. Asset persistence, API/UI and workflow adapters remain optional dependency boundaries with concrete consumers. No empty provider umbrella is admitted.

Server operations bind trusted tenant/principal context and explicit operation/source/sensitive grants. Workflow calls use trusted execution identity and current authorization, preserving existing value-protection/evidence policies. Execution authority metadata is attribution, not a saved grant. Unsupported authority/protection paths fail explicitly.

## Amendment and qualification

Elsa constitution §E2.1 gains the domain with minor version 6.3.0 → 6.4.0 and sync-impact report; the domain purpose catalog and actual module/extension catalogs are updated in this unit. Framework remains v6.1.0. Templates pin no domain list and need no change. Draft/provisional sections are not ratified.

The admission authorizes implementation. Package-only .NET consumption, real Studio author/test/publish/reload, direct/native/BPMN equivalence/recovery, tenant/sensitivity, DMN interoperability, independent review and exact-head/resulting-main gates remain necessary acceptance evidence.
