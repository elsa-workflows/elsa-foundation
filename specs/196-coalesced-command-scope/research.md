# Research: Coalesced Command Scope

## Default factory lifetime

**Decision**: Register the default drain factory scoped, preserving TryAdd behavior.

**Rationale**: Current `AddCoalescingRuntimeCheckpointPersistence` registers the factory singleton. `RuntimeCoalescingDrainScopeFactory` retains scoped `RuntimeCheckpointCommitter`, inner queue and outbox. These registrations preserve the provider's scoped lifetime. A factory scoped with the command aligns its constructor dependency graph with the existing command persistence scope. No runtime algorithm changes are needed.

**Alternatives considered**: A global shell ValidateScopes change exposes broader graph problems but does not repair this registration; locks/serialization conceal shared ownership; a new nested scope factory would create an additional persistence scope and risk breaking atomic participant/context sharing.

## Root and shell providers

**Decision**: Treat shell provider construction as the relevant DI mechanism, distinct from ASP.NET root development settings.

**Rationale**: Pinned CShells 0.0.30-preview.159, source commit 49c912633d968197ddd430c5bd826acd9cfcb15d, builds a separate shell service collection/provider with no-argument BuildServiceProvider. Pinned Microsoft DI 10.0.0 defaults ValidateScopes/ValidateOnBuild to false. ShellMiddleware assigns RequestServices from a shell scope. The captured default shell selects Coalesced/50. Exact dependency-source proof is retained by T02's private source-audit packet; root source/independent review will include these pins. No deployment/environment conclusion is inferred from development root settings.

## Evidence boundaries

**Decision**: Prove a lifetime contract separately from the original failure attribution.

**Rationale**: The C4 baseline produced 41 correct HTTP 200, 10 HTTP 500, 9 HTTP 202 and 23 query-overlap errors across 22 traces; its records lack exact caller/context/instance joins. The scope defect can be corrected and tested without declaring it the cause of every captured outcome. Final integrated concurrent correctness remains mandatory. #2287 root-write renewal and the historical isolated 202 remain separate attribution candidates.

## Test composition

**Decision**: Reuse AddWorkflowRuntime + AddRuntimeEntityFrameworkCore in the existing provider test project, adding Coalesced registration and harmless connection configuration; resolve only factory/persistence collaborators in child scopes.

**Rationale**: Existing aggregate smoke already proves scope-validated EF registration. The DI test must not belong to a Docker fixture collection or call migration/readiness/query APIs. Provider construction and resolution can prove lifetime separation without a network resource. If unrelated missing host services prevent factory resolution, supply the existing legitimate registration/fixture dependencies and retain the exact before-fix failure type; do not fake a successful scoped graph.
