# Quickstart: Durable Diagnostics Persistence

## Planning validation

```bash
rg -n "NEEDS CLARIFICATION|\[FEATURE|\[###|TODO|TBD" specs/139-groundwork-diagnostics-persistence
```

## Focused test progression

```bash
dotnet test tests/Elsa/Diagnostics/StructuredLogs/Persistence/Groundwork/Tests/Elsa.Diagnostics.StructuredLogs.Persistence.Groundwork.Tests.csproj
dotnet test tests/Elsa/Diagnostics/OpenTelemetry/Persistence/Groundwork/Tests/Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.Tests.csproj
dotnet test tests/Elsa/Diagnostics/Persistence/Groundwork/Tests/Elsa.Diagnostics.Persistence.Groundwork.Tests.csproj
```

The replay targets Groundwork `0.0.1-preview.81`. Runs recorded against preview.73, preview.75, or
preview.80 are historical provenance only and cannot promote the lane. After current `main` is merged
into the replay branch, rerun every focused and four-provider command before treating evidence as
exact-head certification.

## Required provider certification

Run the same highest-seam diagnostics fixture against:

1. SQLite on a real database file;
2. SQL Server in the pinned integration container;
3. PostgreSQL in the pinned integration container; and
4. MongoDB in the pinned replica-set integration container.

For each provider, retain test evidence for schema validate/apply, restart, concurrent writers, acknowledgement loss, exact retention, cross-scope isolation, and bounded plans for scale-bearing operations.

## Performance gate

Performance measurement and gate ratification are owned by issue #646 and the spec-094 performance
handoff. This lane consumes the resulting diagnostics verdict and retained artifacts; it does not
create a private harness or self-authorize a replacement gate. Keep the EF oracle until #646 records
a passing or program-owner-ratified verdict.

## Replay-base evidence (not promotion evidence)

On 2026-07-24, before integrating current `main`/Groundwork preview.81, the replay implementation
compiled and passed:

- Structured Logs Groundwork: 17/17.
- OpenTelemetry Groundwork: 68/68.
- Combined diagnostics deployment schema: 6/6.
- Provider-neutral drain and observability slice: 49/49.
- Architecture slice excluding the intentionally pending zero-EF assertion: 19/19.
- Feature, readiness, lifecycle, schema, and durable-operation slice: 34/34.
- OpenTelemetry core/receiver regression suite, preserving the #692 trusted-ingestion seams: 71/71.
- Structured Logs core regression suite, preserving the #649 replay/live-delivery seams: 74/74.

These results prove the replayed implementation is internally coherent on its preview.80 base. They
do not complete T008, T022, T027, T033, T050-T052, or any EF-removal task, and they must not be
represented as preview.81/four-provider certification.

## Final dependency audit

```bash
rg -n "EntityFrameworkCore|Persistence\.EFCore|DbContext|Migration" src/Elsa/Diagnostics tests/Elsa/Diagnostics
rg -n "Groundwork" src/Elsa/Diagnostics/*/Core
```

The first command may match temporary oracle code until the removal phase; it must have no diagnostics EF implementation/dependency matches at completion. The second command must remain empty throughout.

## Delivery rule

Work on the feature branch, implement by user-story slice with tests first, obtain an independent review, push the organization-owned branch, merge the reviewed PR, and leave the repository with all required checks green.
