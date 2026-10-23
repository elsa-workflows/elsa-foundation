# Durable Rules research decisions

Source baseline: `c3243ae05dc26eb4da75328a9af2d59fcacabfe4`; scope approved in [spec.md](spec.md). This consolidates the reviewed [issue contract](https://github.com/elsa-workflows/elsa-foundation/issues/3181#issuecomment-6324393870), not executed acceptance.

## Domain and composition

**Decision:** Admit optional `Elsa.Rules`, whose purpose is “Executes finite rule inference with durable round progression.” Six concrete packages serve demonstrated consumers: Core, Runtime, Persistence.Core, Persistence.EntityFrameworkCore, Workflows.Design and Workflows.Runtime. An own-domain ADR and additive Elsa constitution/domain-tree update accompany implementation.

**Rationale:** ADR 0087 already separates ETL-owned semantics and persistence from native composite semantics. Rules rounds and receipts likewise belong to Rules. Framework CQS requires separate store commands and reader queries. A native adapter is ordinary CLR publication/activity/checkpoint/outbox/bookmark integration.

**Alternatives rejected:** A universal execution registry, workflow checkpoint participants for Rules state, an actor per rule, a second membership service, manual DLL loading, and nesting inference under middleware `Elsa.Pipelines`. Existing native composition stays unchanged. Optional actor qualification and task-runner/pause stay outside this leaf.

## Existing source and overlap

`Extensions/Etl/src/Workflows/Design/Services/EtlPublishedArtifactPinSource.cs` contributes immutable node metadata through `IExecutableCompilationSource`. `RunEtl`, `EtlCheckpointEnricher` and `EtlNativeResultDelivery` show committed native responsibility and proof-before-acknowledgement. The current ordinary CLR path is sufficient; #2891/#1785's capability-key registration is not a prerequisite. No shared registration change is planned; refresh that work before any unforeseen overlap.

ETL runtime storage currently rejects non-SQLite providers. Its schema and store cannot serve as four-provider qualification. Use the four engine contexts/migrations under `Essentials/src/Workflows/Runtime/Persistence/EntityFrameworkCore` and the smaller Secrets domain as mechanics examples, then prove Rules-owned transitions on each engine. `CompensationProviderFixtures` supplies the required-provider failure pattern. `tools/ci/ef-suites.json` is the existing nightly selector; no workflow change is required.

The native ETL result equality check is safe only for its existing scalar-valued result shape. Rules results contain collections; verify canonical bounded content and identity rather than record equality on deserialized list references.

## Finite semantics and recoverable transitions

**Decision:** Preserve the historical finite positive propositional witness at `4b464e1637483e89a11f632f2c99c5779cb4e0de`, `tools/experiments/UnifiedExecution.RuleModelProof/RuleEngine/FiniteRulesPackage.cs`. All rules inspect one frozen round input. Receipt validation recomputes complete firings and additions. Accepted receipt and consumed round are separate atomic transitions.

**Rationale:** Restart must retain the same pending logical result; consumption alone advances facts and can create the next round or terminal result. The no-addition round proves closure. A captured lower positive budget creates a real model fault after its last adding round, not a successful truncated result.

**Alternatives rejected:** Experimental registry/coordinator reuse, work-token cancellation as model cancellation, acknowledgement on enqueue, caller-supplied validated flags, and recomputing a stored receipt as a fresh logical round. Semantic receipt hashes exclude physical attempt attribution; the winning attribution remains retained.

## Package identity and deployment

**Decision:** A clean, external, PackageReference-only host consumes actual locally packed modules and locked dependencies. Use repository-computed versions from `Elsa.Versioning.Calculator` with immutable source and publish-state refs. Record actual package identities, selected semantic asset hashes, NuGet content hashes and raw archive hashes separately. A trusted external deployment receipt is generated after packing and clean restore/publish; it cannot embed its own recursive package hash.

**Rationale:** Source SHA and version text alone do not identify loaded implementation bytes. NuGet content hashes for signed packages are not necessarily raw archive SHA-512. Use NuGet's own content-hash calculation and selected restored asset paths, then verify deployed file hashes and normal loaded assembly identity. The semantic pin initially covers Core, Runtime and Persistence.Core; actual package outputs finalize asset names.

**Alternatives rejected:** Manual version overrides, arbitrary runtime downloads, custom assembly-loading proof, treating identity as publisher authenticity, or proving compatibility by source-host invocation. Initial qualification uses ordinary file-based framework-dependent stable .NET 10 deployments. Single-file/trimmed/ReadyToRun/Nuplane/custom load-context profiles need their own evidence.

## Upgrade and four-provider proof

An additive Rules schema must leave an already persisted native execution unchanged. A Rules run retains its original pin across deployment replacement; an incompatible worker leaves recoverable responsibility and a retained compatible deployment completes it. Rollback stops new admissions while retaining capable workers/readers. No destructive down migration or automatic repinning is allowed.

Use real SQLite, PostgreSQL, SQL Server and MySQL invariant tests and a separate-process PostgreSQL witness. Missing required engines fail qualification rather than becoming green skips. Same-machine processes/containers are the declared topology. Preserve external-effect and historical-timeout limitations of the broader program.

## Remaining implementation work

No unresolved product choice blocks planning. Literal package asset names, generated migrations and runnable harness outputs can be known only after implementation; they are validation tasks rather than claimed facts. The plan fixes finite byte/count caps and maps every acceptance requirement to executable entry points. Runtime code and provider acceptance have not been produced by this research.
