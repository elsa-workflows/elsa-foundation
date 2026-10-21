# Package identity and acceptance host

This contract describes planned acceptance tooling, not completed package qualification. It supplements [persistence](persistence.md) and the [plan](../plan.md).

## Trusted deployment receipt

After normal repository version calculation, pack all six modules and the required dependency closure. Restore a fresh consumer from that local feed and normal approved dependency feeds into an isolated package cache. Generate an external receipt from the resolved assets and published files. It contains:

- receipt schema version, immutable source SHA and immutable versioning publish-state ref;
- actual NuGet IDs and normalized versions, dependency resolution and selected asset paths;
- NuGet content hash calculated with NuGet's own implementation, plus the separate raw archive SHA-512;
- SHA-256 and assembly full name for every selected semantic asset;
- file-based framework-dependent runtime profile, SDK/runtime identity and machine/process topology as environment provenance;
- the Rules language, invocation, receipt, state, result and pin schema versions.

The semantic pin covers `Elsa.Rules.Core`, `Elsa.Rules.Runtime`, `Elsa.Rules.Persistence.Core` and any explicitly semantic transitive dependency. The canonical pin sorts package IDs and asset paths deterministically and rejects duplicate/ambiguous paths. Artifact identity is separate from this pin. Package archive bytes and provenance are retained without treating signing-related archive changes as semantic content changes. Do not use raw archive SHA-512 as a substitute for NuGet content hash.

The host's trusted identity source validates deployed asset bytes against the receipt and normal loaded assembly file identity before advertising compatibility. It snapshots the validated pin. Callers cannot select an arbitrary receipt/path or override a published native node's pin. No runtime download, manually constructed assembly loader, publisher-trust claim or JIT-memory identity proof is introduced. Semantic dependencies must appear in the explicit pin rather than silently relying on the framework profile for their identity.

## Invocation and recovery

Direct facade calls freeze the trusted pin, artifact, canonical initial facts and resolved round limit once. Retries use the original envelope. Native publication freezes the same pin in immutable executable metadata. A runtime consumes only the executable and Rules artifact; the package host never opens mutable design storage to recover a run.

A worker advertises only pins it actually loaded and verified. Mismatched evaluation is Unavailable, with no new refusal, repinning or semantic reinterpretation. A previously accepted common receipt can still be consumed by a worker supporting its retained schemas; the next evaluation remains owed to a deployment that verifies the original pin. Existing matching invocation identity is checked before local semantic availability so exact admission/terminal replay remains possible. Cancellation uses the common envelope independently of the old language implementation. Delivery support depends on the terminal schema and committed parent proof, not inference availability.

## Clean consumer and process witness

`tools/rules/verify-packaged-rules.py` will compute versions from immutable refs using `tools/versioning/Elsa.Versioning.Calculator`, build/pack with its generated properties, and create a consumer outside repository-inherited build configuration. The consumer uses PackageReference only; inspect its restored graph and ensure no direct or transitive ProjectReference to the engine. Keep the feed, assets file, lock file, receipts, process outputs and result JSON as acceptance artifacts. Ordinary source-host tests do not satisfy this gate.

The same consumer executable supports direct invocation, one-round worker progression, bounded inspection and native-host scenarios. The coordinator starts independent PostgreSQL-backed processes, stops the first after accepted-unconsumed receipt and separately after consumed-round commit, then starts a successor with design storage absent. Compare logical receipt identity, winning physical attempt attribution, committed facts, round ordinal and exactly one terminal result. Same-machine process replacement is the declared proof, not separate-host or power-loss durability.

Upgrade checks use an original pinned deployment plus an incompatible successor and the retained original deployment. A deployment disabled for new admissions still retains readers/workers needed by existing runs. Retain original state/receipt schemas and exact pin; no down migration or implicit interpretation by the replacement package. Repeat with a native workflow persisted before additive Rules schema installation, proving its identity and continuation survive.

Acceptance output must identify every command, source/package/ref/hash, database engine/version and terminal result. Missing required inputs or tools fail the harness explicitly; no placeholder success or green skipped provider gate is allowed.
