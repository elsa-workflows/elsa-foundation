# Extension points — Cluster domain

The per-domain catalog (framework §2.22.1). Anchored at `Elsa.Cluster.Core`, the cluster membership contract of
[ADR 0078](../../../../docs/adr/0078-workflow-executions-are-virtual-actors-and-cluster-membership-is-a-foundation-contract.md)
and spec 183. The domain has no feature project: the in-process default is the helper library `Elsa.Cluster.InProcess`,
and a durable provider is composed once on the host container, never per shell.

---

## Overridable contracts

### `IClusterMembership` *(Core — `Elsa.Cluster.Core`)*
- **Kind:** Replacement contract (§2.6.2), declared by `[ClusterMembershipReplacementContract]`. One provider is active per host process.
- **Signature:** `ProviderKind`; `GetLocalStanding()`; `ReadFleetAsync(FleetReadMode, ct)`; `PublishReportAsync(ct)`; `QueryAsync(MemberQuery, FleetReadMode, ct)`.
- **Default impl:** `InProcessClusterMembership` (`Elsa.Cluster.InProcess`), a cluster of one that writes nothing durable and runs nothing in the background. Consumers register it with `TryAddInProcessClusterMembership()`, so a host composes nothing to get it.
- **Override:** a durable provider registers through `AddClusterMembershipProvider(new ClusterMembershipProviderRegistration(name, ClusterProviderKind.Durable, descriptor))` on the host container. The in-process default yields to it in either order; a second durable provider, or an `IClusterMembership` registered directly, fails at startup with a diagnostic naming both. A durable provider starts only with an explicit `Elsa:Cluster:Membership:HostId`.
- **Durable provider:** `EfClusterMembership` (`Elsa.Cluster.EntityFrameworkCore`, spec 183 B2), one shared table on SQLite, SQL Server, PostgreSQL or MySQL under its own `[EfModule("Cluster.Membership")]`, whose baseline also creates the module's finalization tables (spec 181, FR-002) and whose rows belong to the `ClusterMembership` schema family its `[EfSchemaFamily]` declares. A host composes it from configuration with `AddConfiguredClusterMembership(configuration)` (namespace `Elsa.Cluster.Hosting`), which registers nothing unless `Elsa:Cluster:Membership:EntityFrameworkCore:Enabled` is `true`; its store settings (`Provider`, `ConnectionString`, `ConnectionName`, `Schema`, `Pooling`, `CleanupPeriod`) sit beside that switch, and the host id and timings under `Elsa:Cluster:Membership`. `AddEfClusterMembership(options)` composes it in code. It migrates through the plain-host migrator only, joins while the host starts, heartbeats on a fixed interval, drains as the host stops and leaves once it has.
- **Conformance:** a provider is supported only once it passes `ClusterMembershipConformanceTests` (`tests/essentials/Cluster/Testing`), supplying an `IClusterMembershipConformanceFixture`.

### `ISchemaDormancyCheck` *(Core — `Elsa.Cluster.Core`)*
- **Kind:** Replacement contract (§2.6.2), declared by `[SchemaDormancyReplacementContract]`. The shared dormancy check of [spec 182](../../../../specs/182-dormant-features-until-finalization/spec.md) (B6, [#2102](https://github.com/elsa-workflows/elsa-foundation/issues/2102)), FR-003: the one component every module asks "is schema family F at version V yet?". Modules never read a finalization record or compare versions themselves.
- **Signature:** `Evaluate(requirements)` from memory, no I/O, for background work that skips while dormant (FR-018); `EvaluateAsync(requirements, ct)`, which re-reads an observation older than `SchemaDormancyOptions.RefreshBound` (default two seconds) before it answers unmet (FR-014); `EnsureAvailableAsync(requirements, featureId, ct)`, which raises `SchemaDormancyRefusedException` (`Elsa.Primitives`), spec 180's write refusal with the feature and a caller-neutral reason, which every domain API already answers with 409 (FR-012, FR-013, FR-015); `Observe()`; and `ReadStatusAsync(ct)`, the finalization gate's status with the members that cannot read a version, for operator surfaces only (FR-011).
- **The rule:** `SchemaDormancyRule` (static, beside the contract), which every implementation applies: a requirement is met once this host writes the family at the version or later and, for a completeness requirement, once the finish record it observed names the version or later (spec 186). A family it has observed nothing of, a version its build does not read, or a finalized version it cannot read leaves the requirement unmet. `SchemaDormancyReasons` words each unmet requirement for callers and for operators.
- **Default impl:** `SchemaDormancyCheck` (`Elsa.Cluster.InProcess`, beside the membership default), the rule over this container's `IObservedSchemaFinalization`. `TryAddSchemaDormancyCheck()` adds it unless a check is already composed, which then is the one used, and fails when two are.
- **Call it:** where an operation, request field, query or background task accepts data only the newer version holds, before any write or other side effect (FR-002, FR-016). Nothing about dormancy changes composition (FR-006), and nothing depends on a restart or a shell reload (FR-019).

### `IObservedSchemaFinalization` *(Core — `Elsa.Cluster.Core`)*
- **Kind:** Replacement contract (§2.6.2), declared by `[SchemaDormancyReplacementContract]`; the source the check answers from, per container.
- **Signature:** `Find(family)` and `Observe()` from memory; `RefreshAsync(family, maxAge, ct)`, one shared read per bound; `ReadStatusAsync(ct)`.
- **Default impl:** none here. `EfObservedSchemaFinalization` (`Elsa.Cluster.Readability`) reads the container's EF finalization gates live. `AddEfSchemaDormancy()` composes it with the default check, and `AddEfSchemaReadability()` calls it, both registering by type so each shell reads its own gates. `AddObservedSchemaFinalization<T>()` composes a source and fails beside a different one. A container with no source observes nothing, so every declared requirement is reported unmet rather than available.

---

## Declarations

### `[RequiresSchemaVersion(family, version)]` *(Core — `Elsa.Cluster.Core`)*
- **Kind:** Static declaration on a shell feature class (spec 182, FR-001), in the style of `[UsesEfModule]`: constant arguments only, several allowed, `RequiresCompleteness = true` for a feature that queries the version's data and so also waits for the family's completeness (FR-005). Read by `SchemaVersionRequirement.DeclaredBy(type)` from attribute metadata by name.
- **Consumed by:** the feature catalog's `FeatureAvailabilityCatalogContributor` and Modularity's Attention contributor (`Elsa.Modularity.Api`), which report the feature as dormant with its reason (FR-008 to FR-010), and by the feature's own operations, which pass its requirements to the check.

---

## Implementable contributor interfaces

### `IMemberReportSource<TSection>` *(Core — `Elsa.Cluster.Core`)*
- **Kind:** Source. Returns one section of this host's member report; exactly one source per section.
- **Signature:** `ValueTask<TSection> ReadAsync(CancellationToken ct)`, where `TSection` is a `MemberReportSection` the contract defines: `ReadabilitySection` (spec 183) and `RunnabilitySection` (spec 184).
- **Register:** as `IMemberReportSource<TSection>` on the host container, beside the provider, so every shell publishes the same report.
- **Consumed by:** every provider, through `MemberReportComposition`, each time it publishes. Two sources for one section refuse the provider when it is constructed.
- **Requirement kinds:** a member query's requirements come from a closed vocabulary: `ReadsSchemaVersion` (spec 183), `ObservesFinalizedSchemaVersion` (spec 186, FR-012: the post-finalization backfill's settle condition, from the observed finalized version each readability entry carries, counting only entries whose `ModuleActive` is true; see `ReadabilityEntry.ModuleActive`), and `ActivatesRuntimeConsumer`, `HasStorageDriver` and `ResolvesActivityType` (spec 184, FR-006). The three runnability kinds are met jointly, by one runnability entry that applies to each requirement's database (FR-009).
- **Known implementations:** `WorkflowRuntimeRunnabilitySource` (`Elsa.Workflows.Runtime.Distributed`, B7, [#2103](https://github.com/elsa-workflows/elsa-foundation/issues/2103)) for `RunnabilitySection`: one entry per shell whose distributed runtime is active, derived from exactly the registries that shell's `IRuntimeRequirementChecker` reads. A host composes it with `AddWorkflowRuntimeRunnabilityReport()`, beside its membership provider, so every shell records its entry in one host-level registry. `EfSchemaReadabilitySource` (`Elsa.Cluster.Readability`, B3, [#2099](https://github.com/elsa-workflows/elsa-foundation/issues/2099)) for `ReadabilitySection`: one entry per schema family whose `[EfSchemaFamily]` declaration is loaded in the process, from any load context, at the versions every loaded declaration of it reads, and whether the family's module is active in the host (`ReadabilityEntry.ModuleActive`, spec 183 FR-019). A host composes it with `AddEfSchemaReadability()`, which also registers the in-process default, so an unclustered host reports its readability with nothing else composed.

---

## Cross-references

- Repo-wide index: [`../../../../EXTENSION_POINTS.md`](../../../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.2 + §2.22.1.
