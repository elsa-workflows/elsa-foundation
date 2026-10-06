# Expression Code Intelligence Foundation — Verification

## Current integration snapshot — 2026-10-06

The existing Foundation expression stack is being synchronized with main `2a0d1f10fb91bf6928203430d938d99d183c5cda`. The root planning branch changes no production, test, SDK or workflow file relative to that main. Its generated spec-status map retains the reopened expression work (25 completed tasks and 6 open tasks) and the two new draft specs. Root and independent diff review pass; authoritative combined-head Maps and CI remain pending. Historical baseline results below do not establish acceptance of this synchronized stack or a new Studio/Foundation pair.

The owner permits preview NuGet/npm publication only to feedz.io and separately permits Docker images at any endpoint; this delivery keeps the existing Docker Hub workflows. Native Chrome/Safari VoiceOver is **SKIPPED BY OWNER DECISION — NEVER PASSED**. All program PRs remain draft/unmerged pending technical/review gates and explicit final owner acceptance. Required resulting-main checks and public completion records remain open.

## Retained historical baseline

Last reconciled: 2026-10-02 for Program #2310 planning; current-head extension evidence pending

## Program #2310 status

The passing evidence below is historical baseline evidence. It does not prove the current normal host, effective runtime Liquid metadata, runtime-compatible JavaScript help, or the real persisted producer-consumer lifecycle requested by Program #2310. T026-T031 and the corresponding live Studio journey remain open.

## Passing evidence

| Suite | Result |
|---|---:|
| `Elsa.Expressions.Tests` | 108 passed |
| `Elsa.Workflows.Design.Tests` | 344 passed |
| `Elsa.Workflows.Design.Api.Tests` | 89 passed |
| `Elsa.Workflows.Publishing.Api.Tests` | 459 passed |
| Expression-tooling and custom-host architecture filters | 4 passed |
| `dotnet build Elsa.Server.slnx --no-restore --verbosity minimal` | Passed with 0 errors |

The focused evidence covers exact per-expression-type provider routing, JavaScript runtime globals, Liquid symbols, dotted/nested value shapes, authoritative persisted context, policy filtering before paging, host-replaceable authorization and revision fingerprints, descriptor/capability composition, semantic validation state mapping (including validator faults and caller cancellation), publication fail-closed behavior, and Test Run acknowledgement/metadata.

## Full architecture suite

The complete `Elsa.Architecture.Tests` run passes all 320 tests.

The three custom-host composition failures initially exposed by this run were corrected by making the persisted authoring-context source safely optional when a host omits draft persistence. All affected composition tests now pass.

## Contract audit

- Context and symbols are server-authoritative, permission/host-policy filtered, metadata-only, bounded, and `no-store`; hosts can replace `IExpressionAuthoringAuthorizationPolicy`, and its opaque revision participates in stale-result protection.
- Catalog paging occurs after filtering.
- Value-shape members are inlined to depth four; lazy retrieval is explicitly unsupported by v1 descriptors.
- JavaScript, Liquid, and future providers own their own globals/functions/variables; there is no generic “Elsa globals” catalog.
- Known validation errors gate Test Run/publication; unavailable Test Run validation supports explicit acknowledgement only when the result contains no error diagnostics; publication remains fail-closed.
